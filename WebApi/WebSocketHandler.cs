using NINA.Core.Utility;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    public class WebSocketHandler {
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new ConcurrentDictionary<Guid, WebSocket>();

        public int ClientCount => _clients.Count;

        public async Task HandleAsync(HttpListenerWebSocketContext wsContext) {
            var ws = wsContext.WebSocket;
            var id = Guid.NewGuid();
            _clients[id] = ws;
            Logger.Info($"PHD2 API: WebSocket client {id} connected (total: {_clients.Count})");

            try {
                var buffer = new byte[4096];
                while (ws.State == WebSocketState.Open) {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                }
            } catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) {
            } catch (Exception ex) {
                Logger.Warning($"PHD2 API: WebSocket client {id} error: {ex.Message}");
            } finally {
                _clients.TryRemove(id, out _);
                Logger.Info($"PHD2 API: WebSocket client {id} disconnected (total: {_clients.Count})");
                if (ws.State != WebSocketState.Closed) {
                    try { ws.Dispose(); } catch { }
                }
            }
        }

        public async Task BroadcastAsync(string message) {
            if (_clients.IsEmpty) return;

            var bytes = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(bytes);

            foreach (var (id, ws) in _clients) {
                try {
                    if (ws.State == WebSocketState.Open) {
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    } else {
                        _clients.TryRemove(id, out _);
                    }
                } catch (Exception ex) {
                    Logger.Warning($"PHD2 API: Error sending to WebSocket client {id}: {ex.Message}");
                    _clients.TryRemove(id, out _);
                }
            }
        }
    }
}
