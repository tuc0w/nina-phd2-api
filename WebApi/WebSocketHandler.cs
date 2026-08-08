using EmbedIO;
using EmbedIO.WebSockets;
using NINA.Core.Utility;
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;

namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    /// <summary>
    /// EmbedIO WebSocket module that broadcasts PHD2 / plugin events to connected clients.
    /// </summary>
    public class WebSocketHandler : WebSocketModule {
        private readonly ConcurrentDictionary<string, IWebSocketContext> _clients = new ConcurrentDictionary<string, IWebSocketContext>();

        public int ClientCount => _clients.Count;

        public WebSocketHandler(string urlPath)
            : base(urlPath, true) {
        }

        protected override Task OnClientConnectedAsync(IWebSocketContext context) {
            _clients[context.Id] = context;
            Logger.Info($"PHD2 API: WebSocket client {context.Id} connected (total: {_clients.Count})");
            return Task.CompletedTask;
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context) {
            _clients.TryRemove(context.Id, out _);
            Logger.Info($"PHD2 API: WebSocket client {context.Id} disconnected (total: {_clients.Count})");
            return Task.CompletedTask;
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] rxBuffer, IWebSocketReceiveResult rxResult) {
            // Incoming client messages are not used by this API.
            return Task.CompletedTask;
        }

        public async Task BroadcastAsync(string message) {
            if (_clients.IsEmpty) return;

            var bytes = Encoding.UTF8.GetBytes(message);

            foreach (var (id, context) in _clients) {
                try {
                    await SendAsync(context, bytes);
                } catch (Exception ex) {
                    Logger.Warning($"PHD2 API: Error sending to WebSocket client {id}: {ex.Message}");
                    _clients.TryRemove(id, out _);
                }
            }
        }
    }
}
