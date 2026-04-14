using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace TssApi
{
    public sealed class TssUdpClient : IDisposable
    {
        private readonly int _retries;
        private readonly byte[] _requestBuffer = new byte[8];
        private readonly object _lock = new object();
        private UdpClient _udpClient;

        public TssUdpClient(string host, int port, int timeoutMs, int retries)
        {
            _retries = Mathf.Max(1, retries);
            _udpClient = new UdpClient();
            _udpClient.Connect(host, port);
            _udpClient.Client.ReceiveTimeout = Mathf.Max(1, timeoutMs);
        }

        public void Dispose()
        {
            if (_udpClient == null)
            {
                return;
            }

            _udpClient.Close();
            _udpClient = null;
        }

        public Dictionary<string, object> RequestJson(int command)
        {
            if (_udpClient == null)
            {
                return null;
            }

            uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteUIntBigEndian(_requestBuffer, 0, timestamp);
            WriteUIntBigEndian(_requestBuffer, 4, (uint)command);

            for (int attempt = 0; attempt < _retries; attempt++)
            {
                try
                {
                    int sentBytes;
                    lock (_lock)
                    {
                        sentBytes = _udpClient.Send(_requestBuffer, _requestBuffer.Length);
                    }
                    Debug.Log($"[TssUDP] cmd={command} attempt={attempt} sent={sentBytes}B to {_udpClient.Client.RemoteEndPoint}");

                    IPEndPoint sender = null;
                    byte[] raw;
                    lock (_lock)
                    {
                        raw = _udpClient.Receive(ref sender);
                    }

                    Debug.Log($"[TssUDP] cmd={command} got {raw?.Length ?? 0}B from {sender}");
                    var result = DecodeResponse(raw);
                    if (result == null)
                        Debug.LogWarning($"[TssUDP] cmd={command} — received {raw?.Length ?? 0}B but JSON decode returned null. First bytes: {HexPreview(raw, 16)}");
                    return result;
                }
                catch (SocketException se)
                {
                    Debug.LogWarning($"[TssUDP] cmd={command} attempt={attempt} SocketException: {se.SocketErrorCode} — {se.Message}");
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TssUDP] cmd={command} unexpected error: {e.Message}");
                    break;
                }
            }

            Debug.LogWarning($"[TssUDP] cmd={command} — all {_retries} attempts failed, returning null");
            return null;
        }

        private static Dictionary<string, object> DecodeResponse(byte[] raw)
        {
            if (raw == null || raw.Length == 0)
            {
                return null;
            }

            int payloadOffset = FindJsonObjectStart(raw);
            if (payloadOffset < 0)
            {
                return null;
            }

            string json = Encoding.UTF8
                .GetString(raw, payloadOffset, raw.Length - payloadOffset)
                .TrimEnd('\0', ' ', '\n', '\r', '\t');

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            object parsed = MiniJson.Deserialize(json);
            return parsed as Dictionary<string, object>;
        }

        /// <summary>
        /// Locates the first '{' byte in the UDP payload.
        /// TSS2026 GET replies are plain UTF-8 JSON with no binary prefix, so '{' is at index 0.
        /// A full linear scan is used so that any leading bytes (whitespace or a legacy binary
        /// header) are skipped correctly without assuming a fixed offset.
        /// </summary>
        private static int FindJsonObjectStart(byte[] raw)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == (byte)'{')
                    return i;
            }
            return -1;
        }

        private static void WriteUIntBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static string HexPreview(byte[] data, int maxBytes)
        {
            if (data == null) return "(null)";
            int len = System.Math.Min(maxBytes, data.Length);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < len; i++)
                sb.Append(data[i].ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }
}
