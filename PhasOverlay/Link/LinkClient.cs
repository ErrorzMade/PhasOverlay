using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PhasOverlay.Link
{
    public enum RoomCreateResult
    {
        Created,
        CodeTaken,
        Rejected,
        Unavailable
    }

    public sealed class LinkClient : IDisposable
    {
        public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);

        private readonly Uri _service;
        private readonly HttpClient _http;
        private readonly Func<ILinkSocket> _socketFactory;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly TimeSpan _connectTimeout;

        private ILinkSocket? _socket;
        private CancellationTokenSource? _receiveCancel;
        private int _generation;

        public LinkClient(
            Uri service,
            Func<ILinkSocket>? socketFactory = null,
            HttpMessageHandler? handler = null,
            TimeSpan? connectTimeout = null)
        {
            _service = service;
            _socketFactory = socketFactory ?? (() => new ClientWebSocketTransport());
            _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
            if (_connectTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(connectTimeout));
            _http = handler == null ? new HttpClient() : new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(10);
            _http.DefaultRequestHeaders.Add(LinkProtocol.NativeClientHeader, LinkProtocol.NativeClientValue);
        }

        public int Generation => _generation;

        public event Action<string, int>? MessageReceived;
        public event Action<LinkClosedInfo, int>? SocketClosed;

        public async Task<RoomCreateResult> CreateRoomAsync(
            string code, SharedTrackerState state, string hostToken, string contentHash, CancellationToken token)
        {
            if (!LinkProtocol.ValidRoomCode(code) || !LinkProtocol.ValidToken(hostToken)
                || !LinkProtocol.ValidContentHash(contentHash)) return RoomCreateResult.Rejected;

            var body = new JsonObject
            {
                ["protocol"] = LinkProtocol.Version,
                ["schema"] = 1,
                ["content"] = contentHash,
                ["state"] = state.ToJson(),
                ["hostToken"] = hostToken
            };

            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            try
            {
                using var response = await _http.PostAsync(RoomUri(code, false, null), content, token);
                if (response.StatusCode == HttpStatusCode.Conflict) return RoomCreateResult.CodeTaken;
                return response.IsSuccessStatusCode ? RoomCreateResult.Created : RoomCreateResult.Rejected;
            }
            catch (OperationCanceledException) { throw; }
            catch { return RoomCreateResult.Unavailable; }
        }

        public Uri RoomUri(string code, bool websocket, string? contentHash)
        {
            var builder = new UriBuilder(_service)
            {
                Path = _service.AbsolutePath.TrimEnd('/') + "/rooms/" + code,
                Query = websocket
                    ? $"protocol={LinkProtocol.Version}&schema=1&content={Uri.EscapeDataString(contentHash ?? "")}"
                    : ""
            };
            if (websocket) builder.Scheme = _service.Scheme == "http" ? "ws" : "wss";
            return builder.Uri;
        }

        public async Task<bool> ConnectAsync(string code, string contentHash, CancellationToken token)
        {
            await DisconnectAsync(1000, "replaced");

            int generation = Interlocked.Increment(ref _generation);
            var socket = _socketFactory();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            attempt.CancelAfter(_connectTimeout);
            try { await socket.ConnectAsync(RoomUri(code, true, contentHash), attempt.Token); }
            catch
            {
                socket.Dispose();
                return false;
            }

            _socket = socket;
            _receiveCancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ = Task.Run(() => ReceiveLoopAsync(socket, generation, _receiveCancel.Token));
            return true;
        }

        private async Task ReceiveLoopAsync(ILinkSocket socket, int generation, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string? message;
                try { message = await socket.ReceiveTextAsync(token); }
                catch (OperationCanceledException) { return; }
                catch { message = null; }

                if (generation != Volatile.Read(ref _generation)) return;
                if (message == null)
                {
                    SocketClosed?.Invoke(socket.Closed ?? new LinkClosedInfo { Code = 1006, Reason = "dropped" }, generation);
                    return;
                }
                MessageReceived?.Invoke(message, generation);
            }
        }

        /// <summary>Sends one intent. Every caller funnels through a single lock, since a socket
        /// cannot take concurrent sends and UI events can overlap.</summary>
        public async Task<bool> SendAsync(JsonNode intent, CancellationToken token)
        {
            var socket = _socket;
            if (socket == null) return false;

            string payload = intent.ToJsonString();
            if (Encoding.UTF8.GetByteCount(payload) > LinkProtocol.MaxIntentBytes) return false;

            int generation = Volatile.Read(ref _generation);
            await _sendLock.WaitAsync(token);
            try
            {
                if (generation != Volatile.Read(ref _generation) || _socket != socket) return false;
                await socket.SendTextAsync(payload, token);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
            finally { _sendLock.Release(); }
        }

        public async Task DisconnectAsync(int code, string reason)
        {
            var socket = DetachSocket();
            if (socket == null) return;

            await socket.CloseAsync(code, reason);
            socket.Dispose();
        }

        public void Abort()
        {
            var socket = DetachSocket();
            if (socket == null) return;
            try { socket.Abort(); } catch { }
            socket.Dispose();
        }

        private ILinkSocket? DetachSocket()
        {
            var socket = Interlocked.Exchange(ref _socket, null);
            if (socket == null) return null;

            Interlocked.Increment(ref _generation);
            var receiveCancel = Interlocked.Exchange(ref _receiveCancel, null);
            try { receiveCancel?.Cancel(); } catch { }
            receiveCancel?.Dispose();
            return socket;
        }

        public static JsonNode? ParseServerMessage(string payload)
        {
            if (Encoding.UTF8.GetByteCount(payload) > LinkProtocol.MaxServerMessageBytes) return null;
            try { return JsonNode.Parse(payload); }
            catch (JsonException) { return null; }
        }

        public void Dispose()
        {
            var socket = DetachSocket();
            socket?.Dispose();
            _http.Dispose();
            _sendLock.Dispose();
        }
    }
}
