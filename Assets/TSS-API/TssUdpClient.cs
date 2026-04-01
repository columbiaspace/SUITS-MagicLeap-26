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
                    lock (_lock)
                    {
                        _udpClient.Send(_requestBuffer, _requestBuffer.Length);
                        IPEndPoint sender = null;
                        byte[] raw = _udpClient.Receive(ref sender);
                        return DecodeResponse(raw);
                    }
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"TssUdpClient request failed: {e.Message}");
                    break;
                }
            }

            return null;
        }

        private static Dictionary<string, object> DecodeResponse(byte[] raw)
        {
            if (raw == null || raw.Length == 0)
            {
                return null;
            }

            int payloadOffset = raw.Length > 8 ? 8 : 0;
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

        private static void WriteUIntBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
