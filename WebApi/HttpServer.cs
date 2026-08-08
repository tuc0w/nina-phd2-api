using AndreasBehrend.NINA.Phd2Api.Phd2;
using EmbedIO;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    public class HttpServer : IDisposable {
        private WebServer _server;
        private readonly Phd2Client _phd2Client;
        private readonly WebSocketHandler _wsHandler;
        private CancellationTokenSource _cts;
        private bool _disposed;

        public int Port { get; private set; }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        public HttpServer(Phd2Client phd2Client, WebSocketHandler wsHandler) {
            _phd2Client = phd2Client;
            _wsHandler = wsHandler;
        }

        public void Start(int port) {
            Port = port;

            // EmbedIO's own socket-based listener does not require a URL ACL
            // reservation (unlike System.Net.HttpListener / http.sys), so it
            // can bind to all interfaces without elevated privileges.
            _server = new WebServer(o => o
                    .WithUrlPrefix($"http://*:{port}/")
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithCors("*", "*", "GET, POST, OPTIONS")
                .WithModule(_wsHandler)
                .WithModule(new ApiModule("/api/v1", _phd2Client, _wsHandler, JsonOptions));

            _cts = new CancellationTokenSource();
            _ = _server.RunAsync(_cts.Token);
            Logger.Info($"PHD2 API: HTTP server started on port {port}");
        }

        public void Stop() {
            _cts?.Cancel();
            Logger.Info("PHD2 API: HTTP server stopped");
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _server?.Dispose();
        }

        /// <summary>
        /// Returns all IPv4 addresses of local network interfaces that are up
        /// (excluding loopback), plus "localhost", each combined with the current port.
        /// Used to display all reachable base URLs in the plugin options.
        /// </summary>
        public IReadOnlyList<string> GetBoundAddresses() {
            var addresses = new List<string> { "localhost" };

            try {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var addrInfo in ni.GetIPProperties().UnicastAddresses) {
                        if (addrInfo.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        addresses.Add(addrInfo.Address.ToString());
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"PHD2 API: Failed to enumerate network interfaces: {ex.Message}");
            }

            return addresses;
        }
    }

    /// <summary>
    /// Handles all REST endpoints, Swagger UI, OpenAPI spec and star image delivery
    /// under the "/api/v1" base route.
    /// </summary>
    internal class ApiModule : WebModuleBase {
        private readonly Phd2Client _phd2Client;
        private readonly WebSocketHandler _wsHandler;
        private readonly JsonSerializerOptions _jsonOptions;

        public override bool IsFinalHandler => true;

        public ApiModule(string baseRoute, Phd2Client phd2Client, WebSocketHandler wsHandler, JsonSerializerOptions jsonOptions)
            : base(baseRoute) {
            _phd2Client = phd2Client;
            _wsHandler = wsHandler;
            _jsonOptions = jsonOptions;
        }

        protected override async Task OnRequestAsync(IHttpContext context) {
            var path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var method = context.Request.HttpMethod.ToUpperInvariant();

            try {
                // Swagger UI
                if (path == "/api/v1/swagger") {
                    await ServeTextAsync(context, SwaggerUi.GetHtml(), "text/html; charset=utf-8");
                    return;
                }

                // Swagger UI static assets (embedded resources)
                if (path.StartsWith("/api/v1/swagger-assets/")) {
                    await ServeSwaggerAssetAsync(context, path);
                    return;
                }

                // OpenAPI specification
                if (path == "/api/v1/openapi.json") {
                    var host = context.Request.Headers["Host"] ?? context.Request.LocalEndPoint.ToString();
                    await ServeTextAsync(context, OpenApiSpec.Generate(host), "application/json; charset=utf-8");
                    return;
                }

                // Star image as PNG
                if (path == "/api/v1/phd2/starimage.png" && method == "GET") {
                    await ServeStarImagePngAsync(context);
                    return;
                }

                ApiResponse response;
                int statusCode = 200;

                try {
                    response = await RouteRequestAsync(path, method, context);
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

                var json = JsonSerializer.Serialize(response, _jsonOptions);
                await SendJsonAsync(context, json, statusCode);
            } catch (Exception ex) {
                Logger.Error($"PHD2 API: Error handling request: {ex.Message}");
                try { context.Response.StatusCode = 500; } catch { }
            }
        }

        private async Task<ApiResponse> RouteRequestAsync(string path, string method, IHttpContext context) {
            var request = context.Request;

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

        private static async Task<string> ReadBodyAsync(IHttpRequest request) {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            return await reader.ReadToEndAsync();
        }

        private async Task<ApiResponse> HandleSetExposureAsync(IHttpRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            int ms = doc.RootElement.GetProperty("exposure").GetInt32();
            await _phd2Client.SetExposureAsync(ms);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleSetPausedAsync(IHttpRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            bool paused = doc.RootElement.GetProperty("paused").GetBoolean();
            bool full = doc.RootElement.TryGetProperty("full", out var fullEl) && fullEl.GetBoolean();
            await _phd2Client.SetPausedAsync(paused, full);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleGuideAsync(IHttpRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            var settle = ParseSettle(doc.RootElement.GetProperty("settle"));
            bool recalibrate = doc.RootElement.TryGetProperty("recalibrate", out var r) && r.GetBoolean();
            await _phd2Client.GuideAsync(settle, recalibrate);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleDitherAsync(IHttpRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            double amount = doc.RootElement.GetProperty("amount").GetDouble();
            bool raOnly = doc.RootElement.TryGetProperty("raOnly", out var r) && r.GetBoolean();
            var settle = ParseSettle(doc.RootElement.GetProperty("settle"));
            await _phd2Client.DitherAsync(amount, raOnly, settle);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleSetConnectedAsync(IHttpRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            bool connect = doc.RootElement.GetProperty("connect").GetBoolean();
            await _phd2Client.SetConnectedAsync(connect);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleClearCalibrationAsync(IHttpRequest request) {
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

        private async Task<ApiResponse> HandleSetProfileAsync(IHttpRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            int profileId = doc.RootElement.GetProperty("profileId").GetInt32();
            await _phd2Client.SetProfileAsync(profileId);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleGuidePulseAsync(IHttpRequest request) {
            var body = await ReadBodyAsync(request);
            using var doc = JsonDocument.Parse(body);
            int amount = doc.RootElement.GetProperty("amount").GetInt32();
            string direction = doc.RootElement.GetProperty("direction").GetString() ?? "N";
            string which = doc.RootElement.TryGetProperty("which", out var w) ? w.GetString() ?? "Mount" : "Mount";
            await _phd2Client.GuidePulseAsync(amount, direction, which);
            return ApiResponse.Ok();
        }

        private async Task<ApiResponse> HandleSetGuideOutputAsync(IHttpRequest request) {
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

        private async Task ServeStarImagePngAsync(IHttpContext context) {
            try {
                int size = int.TryParse(context.Request.QueryString["size"], out var s) ? s : 15;
                var result = await _phd2Client.GetStarImageAsync(size);
                if (result == null) {
                    context.Response.StatusCode = 503;
                    return;
                }

                int width  = result.Value.GetProperty("width").GetInt32();
                int height = result.Value.GetProperty("height").GetInt32();
                byte[] pixels = Convert.FromBase64String(result.Value.GetProperty("pixels").GetString()!);
                byte[] png = PngEncoder.EncodeFrom16Bit(pixels, width, height);

                context.Response.ContentType = "image/png";
                context.Response.ContentLength64 = png.Length;
                context.Response.Headers.Add("Cache-Control", "no-cache");
                await context.Response.OutputStream.WriteAsync(png);
            } catch (Exception ex) {
                Logger.Error($"PHD2 API: Error serving star image PNG: {ex.Message}");
                try { context.Response.StatusCode = 500; } catch { }
            }
        }

        private static async Task ServeSwaggerAssetAsync(IHttpContext context, string path) {
            var fileName = Path.GetFileName(path);
            var resourceName = $"AndreasBehrend.NINA.Phd2Api.WebApi.SwaggerAssets.{fileName}";
            var contentType = fileName.EndsWith(".css") ? "text/css; charset=utf-8" : "application/javascript; charset=utf-8";

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null) {
                context.Response.StatusCode = 404;
                return;
            }

            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers.Add("Cache-Control", "public, max-age=86400");
            await context.Response.OutputStream.WriteAsync(bytes);
        }

        private static async Task ServeTextAsync(IHttpContext context, string content, string contentType) {
            var bytes = Encoding.UTF8.GetBytes(content);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }

        private static async Task SendJsonAsync(IHttpContext context, string json, int statusCode) {
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
    }
}
