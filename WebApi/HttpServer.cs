using AndreasBehrend.NINA.Phd2Api.Phd2;
using NINA.Core.Utility;
using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    public class HttpServer : IDisposable {
        private HttpListener _listener;
        private readonly Phd2Client _phd2Client;
        private readonly WebSocketHandler _wsHandler;
        private CancellationTokenSource _cts;
        private bool _disposed;
        private int _port;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        public HttpServer(Phd2Client phd2Client, WebSocketHandler wsHandler) {
            _phd2Client = phd2Client;
            _wsHandler = wsHandler;
        }

        public void Start(int port) {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/api/v1/");
            try {
                _listener.Start();
            } catch (HttpListenerException ex) when (ex.ErrorCode == 5) {
                // Access denied with wildcard - fall back to localhost only
                Logger.Warning($"PHD2 API: Cannot bind to all interfaces on port {port} (run 'netsh http add urlacl url=http://+:{port}/ user=%USERNAME%' for network access). Falling back to localhost.");
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/api/v1/");
                _listener.Start();
            }
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            Logger.Info($"PHD2 API: HTTP server started on port {port}");
        }

        public void Stop() {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            Logger.Info("PHD2 API: HTTP server stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                try {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleContextAsync(ctx), ct);
                } catch (HttpListenerException) when (ct.IsCancellationRequested) {
                    break;
                } catch (ObjectDisposedException) {
                    break;
                } catch (Exception ex) {
                    if (!ct.IsCancellationRequested) {
                        Logger.Error($"PHD2 API: Accept loop error: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleContextAsync(HttpListenerContext ctx) {
            try {
                // CORS preflight
                if (ctx.Request.HttpMethod == "OPTIONS") {
                    ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                // WebSocket upgrade
                if (ctx.Request.IsWebSocketRequest) {
                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                    _ = _wsHandler.HandleAsync(wsCtx);
                    return;
                }

                var path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                var method = ctx.Request.HttpMethod.ToUpperInvariant();

                // Swagger UI
                if (path == "/api/v1/swagger") {
                    await ServeTextAsync(ctx, SwaggerUi.GetHtml(_port), "text/html; charset=utf-8");
                    return;
                }

                // Swagger UI static assets (embedded resources)
                if (path.StartsWith("/api/v1/swagger-assets/")) {
                    await ServeSwaggerAssetAsync(ctx, path);
                    return;
                }

                // OpenAPI specification
                if (path == "/api/v1/openapi.json") {
                    await ServeTextAsync(ctx, OpenApiSpec.Generate(_port), "application/json; charset=utf-8");
                    return;
                }

                // Star image as PNG
                if (path == "/api/v1/phd2/starimage.png" && method == "GET") {
                    await ServeStarImagePngAsync(ctx);
                    return;
                }

                ApiResponse response;
                int statusCode = 200;

                try {
                    response = await RouteRequestAsync(path, method, ctx.Request);
                    if (!response.Success && response.Message?.StartsWith("Unknown endpoint") == true) {
                        statusCode = 404;
                    }
                } catch (InvalidOperationException ex) {
                    response = ApiResponse.Fail($"PHD2 not connected: {ex.Message}");
                    statusCode = 503;
                } catch (Exception ex) {
                    response = ApiResponse.Fail(ex.Message);
                    statusCode = 500;
                }

                var json = JsonSerializer.Serialize(response, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode = statusCode;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            } catch (Exception ex) {
                Logger.Error($"PHD2 API: Error handling request: {ex.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        private async Task<ApiResponse> RouteRequestAsync(string path, string method, HttpListenerRequest request) {
            if (method == "GET") {
                switch (path) {
                    case "/api/v1/phd2/appstate":
                        return ApiResponse.Ok(new { state = _phd2Client.AppState });
                    case "/api/v1/phd2/version":
                        return ApiResponse.Ok(new { version = _phd2Client.PhDVersion });
                    case "/api/v1/phd2/wsclients":
                        return ApiResponse.Ok(new { clients = _wsHandler.ClientCount });
                    case "/api/v1/phd2/connected":
                        return ApiResponse.Ok(new { connected = await _phd2Client.GetConnectedAsync() });
                    case "/api/v1/phd2/calibrated":
                        return ApiResponse.Ok(new { calibrated = await _phd2Client.GetCalibratedAsync() });
                    case "/api/v1/phd2/exposure":
                        return ApiResponse.Ok(new { exposure = await _phd2Client.GetExposureAsync() });
                    case "/api/v1/phd2/exposuredurations":
                        return ApiResponse.Ok(new { durations = await _phd2Client.GetExposureDurationsAsync() });
                    case "/api/v1/phd2/lockposition":
                        return ApiResponse.Ok(new { lockPosition = await _phd2Client.GetLockPositionAsync() });
                    case "/api/v1/phd2/paused":
                        return ApiResponse.Ok(new { paused = await _phd2Client.GetPausedAsync() });
                    case "/api/v1/phd2/pixelscale":
                        return ApiResponse.Ok(new { pixelScale = await _phd2Client.GetPixelScaleAsync() });
                    case "/api/v1/phd2/searchregion":
                        return ApiResponse.Ok(new { searchRegion = await _phd2Client.GetSearchRegionAsync() });
                    case "/api/v1/phd2/guideoutput":
                        return ApiResponse.Ok(new { enabled = await _phd2Client.GetGuideOutputEnabledAsync() });
                    case "/api/v1/phd2/profile":
                        return ApiResponse.Ok(await _phd2Client.GetProfileAsync());
                    case "/api/v1/phd2/profiles":
                        return ApiResponse.Ok(await _phd2Client.GetProfilesAsync());
                    case "/api/v1/phd2/equipment":
                        return ApiResponse.Ok(await _phd2Client.GetCurrentEquipmentAsync());
                    case "/api/v1/phd2/calibrationdata":
                        return ApiResponse.Ok(await _phd2Client.GetCalibrationDataAsync());
                    case "/api/v1/phd2/starimage":
                        return await HandleGetStarImageAsync();
                    default:
                        return ApiResponse.Fail($"Unknown endpoint: {path}");
                }
            }

            if (method == "POST") {
                switch (path) {
                    case "/api/v1/phd2/exposure":
                        return await HandleSetExposureAsync(request);
                    case "/api/v1/phd2/paused":
                        return await HandleSetPausedAsync(request);
                    case "/api/v1/phd2/guide":
                        return await HandleGuideAsync(request);
                    case "/api/v1/phd2/dither":
                        return await HandleDitherAsync(request);
                    case "/api/v1/phd2/loop":
                        await _phd2Client.LoopAsync();
                        return ApiResponse.Ok();
                    case "/api/v1/phd2/stopcapture":
                        await _phd2Client.StopCaptureAsync();
                        return ApiResponse.Ok();
                    case "/api/v1/phd2/connect":
                        return await HandleSetConnectedAsync(request);
                    case "/api/v1/phd2/flipcalibration":
                        await _phd2Client.FlipCalibrationAsync();
                        return ApiResponse.Ok();
                    case "/api/v1/phd2/clearcalibration":
                        return await HandleClearCalibrationAsync(request);
                    case "/api/v1/phd2/setprofile":
                        return await HandleSetProfileAsync(request);
                    case "/api/v1/phd2/findstar":
                        await _phd2Client.FindStarAsync();
                        return ApiResponse.Ok();
                    case "/api/v1/phd2/guidepulse":
                        return await HandleGuidePulseAsync(request);
                    case "/api/v1/phd2/guideoutput":
                        return await HandleSetGuideOutputAsync(request);
                    default:
                        return ApiResponse.Fail($"Unknown endpoint: {path}");
                }
            }

            return ApiResponse.Fail($"Method not allowed: {method}");
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest request) {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            return await reader.ReadToEndAsync();
        }

        private async Task<ApiResponse> HandleSetExposureAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            int ms = doc.RootElement.GetProperty("exposure").GetInt32();
            await _phd2Client.SetExposureAsync(ms);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleSetPausedAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            bool paused = doc.RootElement.GetProperty("paused").GetBoolean();
            bool full = doc.RootElement.TryGetProperty("full", out var fullEl) && fullEl.GetBoolean();
            await _phd2Client.SetPausedAsync(paused, full);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleGuideAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            var settle = ParseSettle(doc.RootElement.GetProperty("settle"));
            bool recalibrate = doc.RootElement.TryGetProperty("recalibrate", out var r) && r.GetBoolean();
            await _phd2Client.GuideAsync(settle, recalibrate);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleDitherAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            double amount = doc.RootElement.GetProperty("amount").GetDouble();
            bool raOnly = doc.RootElement.TryGetProperty("raOnly", out var r) && r.GetBoolean();
            var settle = ParseSettle(doc.RootElement.GetProperty("settle"));
            await _phd2Client.DitherAsync(amount, raOnly, settle);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleSetConnectedAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            bool connect = doc.RootElement.GetProperty("connect").GetBoolean();
            await _phd2Client.SetConnectedAsync(connect);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleClearCalibrationAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            string which = "both";
            if (!string.IsNullOrWhiteSpace(body)) {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("which", out var w)) {
                    which = w.GetString() ?? "both";
                }
            }
            await _phd2Client.ClearCalibrationAsync(which);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleSetProfileAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            int profileId = doc.RootElement.GetProperty("profileId").GetInt32();
            await _phd2Client.SetProfileAsync(profileId);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleGuidePulseAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            int amount = doc.RootElement.GetProperty("amount").GetInt32();
            string direction = doc.RootElement.GetProperty("direction").GetString() ?? "N";
            string which = doc.RootElement.TryGetProperty("which", out var w) ? w.GetString() ?? "Mount" : "Mount";
            await _phd2Client.GuidePulseAsync(amount, direction, which);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleSetGuideOutputAsync(HttpListenerRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            bool enabled = doc.RootElement.GetProperty("enabled").GetBoolean();
            await _phd2Client.SetGuideOutputEnabledAsync(enabled);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleGetStarImageAsync() {
            var result = await _phd2Client.GetStarImageAsync();
            if (result == null)
                return ApiResponse.Fail("No star image available");

            int width    = result.Value.GetProperty("width").GetInt32();
            int height   = result.Value.GetProperty("height").GetInt32();
            int frame    = result.Value.GetProperty("frame").GetInt32();
            var starPos  = result.Value.GetProperty("star_pos");
            byte[] raw16 = Convert.FromBase64String(result.Value.GetProperty("pixels").GetString()!);
            byte[] png   = PngEncoder.EncodeFrom16Bit(raw16, width, height);

            return ApiResponse.Ok(new {
                frame,
                width,
                height,
                star_pos = new[] { starPos[0].GetDouble(), starPos[1].GetDouble() },
                pixels = Convert.ToBase64String(png)
            });
        }

        private static SettleParams ParseSettle(JsonElement el) => new SettleParams {
            Pixels = el.GetProperty("pixels").GetDouble(),
            Time = el.GetProperty("time").GetDouble(),
            Timeout = el.GetProperty("timeout").GetDouble(),
        };

        private async Task ServeStarImagePngAsync(HttpListenerContext ctx) {
            try {
                int size = int.TryParse(ctx.Request.QueryString["size"], out var s) ? s : 15;
                var result = await _phd2Client.GetStarImageAsync(size);
                if (result == null) {
                    ctx.Response.StatusCode = 503;
                    ctx.Response.Close();
                    return;
                }

                int width  = result.Value.GetProperty("width").GetInt32();
                int height = result.Value.GetProperty("height").GetInt32();
                byte[] pixels = Convert.FromBase64String(result.Value.GetProperty("pixels").GetString()!);
                byte[] png = PngEncoder.EncodeFrom16Bit(pixels, width, height);

                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = png.Length;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                await ctx.Response.OutputStream.WriteAsync(png);
                ctx.Response.Close();
            } catch (Exception ex) {
                Logger.Error($"PHD2 API: Error serving star image PNG: {ex.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        private static async Task ServeSwaggerAssetAsync(HttpListenerContext ctx, string path) {
            var fileName = Path.GetFileName(path);
            var resourceName = $"AndreasBehrend.NINA.Phd2Api.WebApi.SwaggerAssets.{fileName}";
            var contentType = fileName.EndsWith(".css") ? "text/css; charset=utf-8" : "application/javascript; charset=utf-8";

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null) {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes);
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Add("Cache-Control", "public, max-age=86400");
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }

        private static async Task ServeTextAsync(HttpListenerContext ctx, string content, string contentType) {
            var bytes = Encoding.UTF8.GetBytes(content);
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _listener?.Close();
        }
    }
}
