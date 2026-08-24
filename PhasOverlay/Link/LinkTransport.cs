using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhasOverlay.Link
{
    public enum LinkCloseKind
    {
        Dropped,
        Replaced,
        RoomEnded,
        Rejected
    }

    public sealed class LinkClosedInfo
    {
        public int Code { get; init; }
        public string Reason { get; init; } = "";

        public LinkCloseKind Kind => Code switch
        {
            4001 => LinkCloseKind.Replaced,
            4004 => LinkCloseKind.RoomEnded,
            1008 => LinkCloseKind.Rejected,
            _ => LinkCloseKind.Dropped
        };
    }

    public interface ILinkSocket : IDisposable
    {
        Task ConnectAsync(Uri uri, CancellationToken token);
        Task SendTextAsync(string payload, CancellationToken token);
        Task<string?> ReceiveTextAsync(CancellationToken token);
        Task CloseAsync(int code, string reason);
        void Abort();
        LinkClosedInfo? Closed { get; }
    }

    public sealed class ClientWebSocketTransport : ILinkSocket
    {
        public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(10);

        private readonly ClientWebSocket _socket = new();
        private readonly byte[] _buffer = new byte[8192];

        public ClientWebSocketTransport()
        {
            ConfigureKeepAlive(_socket.Options);
        }

        public LinkClosedInfo? Closed { get; private set; }

        internal static void ConfigureKeepAlive(ClientWebSocketOptions options)
        {
            options.KeepAliveInterval = KeepAliveInterval;
            options.KeepAliveTimeout = KeepAliveTimeout;
        }

        public async Task ConnectAsync(Uri uri, CancellationToken token)
        {
            _socket.Options.SetRequestHeader(LinkProtocol.NativeClientHeader, LinkProtocol.NativeClientValue);
            await _socket.ConnectAsync(uri, token);
        }

        public Task SendTextAsync(string payload, CancellationToken token) =>
            _socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, token);

        public async Task<string?> ReceiveTextAsync(CancellationToken token)
        {
            using var message = new LinkTextMessageBuffer();
            while (true)
            {
                WebSocketReceiveResult result;
                try { result = await _socket.ReceiveAsync(new ArraySegment<byte>(_buffer), token); }
                catch (WebSocketException) { Closed ??= new LinkClosedInfo { Code = 1006, Reason = "dropped" }; return null; }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Closed = new LinkClosedInfo
                    {
                        Code = (int)(_socket.CloseStatus ?? WebSocketCloseStatus.Empty),
                        Reason = _socket.CloseStatusDescription ?? ""
                    };
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    Closed = new LinkClosedInfo { Code = 4002, Reason = "invalid_frame" };
                    return null;
                }
                if (!message.Append(_buffer.AsSpan(0, result.Count)))
                {
                    Closed = new LinkClosedInfo { Code = 4003, Reason = "message_too_large" };
                    return null;
                }
                if (!result.EndOfMessage) continue;
                if (message.TryGetText(out string text)) return text;

                Closed = new LinkClosedInfo { Code = 4002, Reason = "invalid_utf8" };
                return null;
            }
        }

        public async Task CloseAsync(int code, string reason)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync((WebSocketCloseStatus)code, reason, timeout.Token);
            }
            catch { }
        }

        public void Abort()
        {
            try { _socket.Abort(); } catch { }
        }

        public void Dispose() => _socket.Dispose();
    }

    internal sealed class LinkTextMessageBuffer : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly MemoryStream _bytes = new();

        public bool Append(ReadOnlySpan<byte> value)
        {
            if (_bytes.Length > LinkProtocol.MaxServerMessageBytes - value.Length) return false;
            _bytes.Write(value);
            return true;
        }

        public bool TryGetText(out string value)
        {
            try
            {
                value = StrictUtf8.GetString(_bytes.GetBuffer(), 0, (int)_bytes.Length);
                return true;
            }
            catch (DecoderFallbackException)
            {
                value = "";
                return false;
            }
        }

        public void Dispose() => _bytes.Dispose();
    }

    public static class LinkBackoff
    {
        public const int BaseDelayMs = 1000;
        public const int MaxDelayMs = 15000;

        public static int DelayMs(int attempt, double jitter)
        {
            int capped = Math.Min(MaxDelayMs, BaseDelayMs * (int)Math.Pow(2, Math.Clamp(attempt, 0, 4)));
            double factor = 0.5 + Math.Clamp(jitter, 0.0, 1.0) * 0.5;
            return Math.Max(BaseDelayMs / 2, (int)(capped * factor));
        }
    }
}
