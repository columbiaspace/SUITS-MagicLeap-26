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

            SocketException lastSocketException = null;
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
                catch (SocketException socketException)
                {
                    lastSocketException = socketException;
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

            // Don't spam — log only when we transition from "working" to "broken" or every Nth poll.
            // Most callers poll at 4Hz; logging once per second of consistent failure is enough to
            // diagnose without flooding the device console.
            if (lastSocketException != null)
            {
                LogThrottledSocketFailure(command, lastSocketException);
            }

            return null;
        }

        private long _lastFailureLogTicks;
        private void LogThrottledSocketFailure(int command, SocketException socketException)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            long elapsedSeconds = (nowTicks - _lastFailureLogTicks) / TimeSpan.TicksPerSecond;
            if (_lastFailureLogTicks == 0 || elapsedSeconds >= 5)
            {
                _lastFailureLogTicks = nowTicks;
                Debug.LogWarning($"[Tss] UDP request failed (cmd={command}, code={socketException.SocketErrorCode}): {socketException.Message}. Will retry on next poll.");
            }
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
