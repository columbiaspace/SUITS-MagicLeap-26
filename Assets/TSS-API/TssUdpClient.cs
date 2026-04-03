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

        /// <summary>Set on every <see cref="RequestJson"/> call: null on success, otherwise why the dict was not returned.</summary>
        public string LastRequestError { get; private set; }

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
            LastRequestError = null;

            if (_udpClient == null)
            {
                LastRequestError = "udp_client_null";
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
                        if (TryDecodeResponse(raw, out Dictionary<string, object> dict, out string decodeErr))
                        {
                            LastRequestError = null;
                            return dict;
                        }

                        LastRequestError = decodeErr ?? "decode_failed";
                        return null;
                    }
                }
                catch (SocketException ex)
                {
                    LastRequestError = $"socket {ex.SocketErrorCode}: {ex.Message}";
                }
                catch (ObjectDisposedException)
                {
                    LastRequestError = "udp_client_disposed";
                    break;
                }
                catch (Exception e)
                {
                    LastRequestError = $"error: {e.Message}";
                    Debug.LogWarning($"TssUdpClient request failed: {e.Message}");
                    break;
                }
            }

            if (string.IsNullOrEmpty(LastRequestError))
            {
                LastRequestError = $"no_response_after_{_retries}_attempt(s)";
            }

            return null;
        }

        private static bool TryDecodeResponse(byte[] raw, out Dictionary<string, object> dict, out string error)
        {
            dict = null;
            error = null;

            if (raw == null || raw.Length == 0)
            {
                error = "empty_udp_payload";
                return false;
            }

            int payloadOffset = FindJsonObjectStart(raw);
            if (payloadOffset < 0)
            {
                error = "no_json_object_prefix_in_payload";
                return false;
            }

            string json = Encoding.UTF8
                .GetString(raw, payloadOffset, raw.Length - payloadOffset)
                .TrimEnd('\0', ' ', '\n', '\r', '\t');

            if (string.IsNullOrEmpty(json))
            {
                error = "json_trimmed_empty";
                return false;
            }

            object parsed;
            try
            {
                parsed = MiniJson.Deserialize(json);
            }
            catch (Exception ex)
            {
                error = $"mini_json: {ex.Message}";
                return false;
            }

            dict = parsed as Dictionary<string, object>;
            if (dict == null)
            {
                error = "json_root_not_object";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Locates the first byte of the JSON object in the UDP payload.
        /// TSS2026 GET replies are UTF-8 JSON beginning with '{' (possibly after whitespace).
        /// </summary>
        private static int FindJsonObjectStart(byte[] raw)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                byte b = raw[i];
                if (b == (byte)'{')
                {
                    return i;
                }

                // Stop scanning if we hit non-whitespace that is not '{' — payload may be legacy "[8-byte prefix][json]".
                if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n')
                {
                    break;
                }
            }

            // Legacy fallback: some older/custom stacks may prepend 8 binary bytes before the JSON body
            // (mirroring the 8-byte request shape). TSS2026 does not; healthy packets take the '{' branch above.
            return raw.Length > 8 ? 8 : 0;
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
