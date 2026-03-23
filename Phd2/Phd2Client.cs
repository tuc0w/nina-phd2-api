using NINA.Core.Utility;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AndreasBehrend.NINA.Phd2Api.Phd2 {

    public class Phd2Client : IDisposable {
        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private int _nextId = 0;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Phd2RpcResponse>> _pending = new ConcurrentDictionary<int, TaskCompletionSource<Phd2RpcResponse>>();
        private CancellationTokenSource _cts;
        private bool _disposed;

        public string AppState { get; private set; } = "Unknown";
        public string PhDVersion { get; private set; } = string.Empty;
        public bool IsConnectedToPhd2 { get; private set; }

        public event EventHandler<Phd2EventBase> EventReceived;
        public event EventHandler Phd2Connected;
        public event EventHandler Phd2Disconnected;

        public async Task<bool> ConnectAsync(string host, int port, CancellationToken ct = default) {
            try {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port, ct);
                var stream = _tcpClient.GetStream();
                _reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                _writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true, NewLine = "\r\n" };
                IsConnectedToPhd2 = true;
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ReadLoopAsync(_cts.Token));
                Phd2Connected?.Invoke(this, EventArgs.Empty);
                Logger.Info("PHD2 Client: Connected to PHD2");
                return true;
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                Logger.Error($"PHD2 Client: Failed to connect to PHD2 at {host}:{port} - {ex.Message}");
                return false;
            }
        }

        public void Disconnect() {
            IsConnectedToPhd2 = false;
            _cts?.Cancel();
            try { _tcpClient?.Close(); } catch { }
        }

        private async Task ReadLoopAsync(CancellationToken ct) {
            try {
                while (!ct.IsCancellationRequested) {
                    var line = await _reader.ReadLineAsync(ct);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ProcessMessage(line);
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error($"PHD2 Client: Read loop error: {ex.Message}");
            } finally {
                IsConnectedToPhd2 = false;
                CancelPendingRequests();
                Phd2Disconnected?.Invoke(this, EventArgs.Empty);
                Logger.Info("PHD2 Client: Disconnected from PHD2");
            }
        }

        private void CancelPendingRequests() {
            foreach (var kv in _pending) {
                kv.Value.TrySetException(new IOException("PHD2 connection lost"));
            }
            _pending.Clear();
        }

        private void ProcessMessage(string json) {
            try {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("jsonrpc", out _)) {
                    if (root.TryGetProperty("id", out var idEl)) {
                        int id = idEl.GetInt32();
                        if (_pending.TryRemove(id, out var tcs)) {
                            var response = JsonSerializer.Deserialize<Phd2RpcResponse>(json);
                            tcs.TrySetResult(response);
                        }
                    }
                    return;
                }

                if (!root.TryGetProperty("Event", out var eventEl)) return;
                string eventName = eventEl.GetString() ?? string.Empty;

                Phd2EventBase evt = eventName switch {
                    "Version" => JsonSerializer.Deserialize<VersionEvent>(json),
                    "AppState" => JsonSerializer.Deserialize<AppStateEvent>(json),
                    "GuideStep" => JsonSerializer.Deserialize<GuideStepEvent>(json),
                    "StarLost" => JsonSerializer.Deserialize<StarLostEvent>(json),
                    "Settling" => JsonSerializer.Deserialize<SettlingEvent>(json),
                    "SettleDone" => JsonSerializer.Deserialize<SettleDoneEvent>(json),
                    "LockPositionSet" => JsonSerializer.Deserialize<LockPositionSetEvent>(json),
                    "StarSelected" => JsonSerializer.Deserialize<StarSelectedEvent>(json),
                    "Calibrating" => JsonSerializer.Deserialize<CalibratingEvent>(json),
                    "CalibrationComplete" => JsonSerializer.Deserialize<CalibrationCompleteEvent>(json),
                    "CalibrationFailed" => JsonSerializer.Deserialize<CalibrationFailedEvent>(json),
                    "CalibrationDataFlipped" => JsonSerializer.Deserialize<CalibrationDataFlippedEvent>(json),
                    "StartCalibration" => JsonSerializer.Deserialize<StartCalibrationEvent>(json),
                    "GuidingDithered" => JsonSerializer.Deserialize<GuidingDitheredEvent>(json),
                    "Alert" => JsonSerializer.Deserialize<AlertEvent>(json),
                    "GuideParamChange" => JsonSerializer.Deserialize<GuideParamChangeEvent>(json),
                    "LoopingExposures" => JsonSerializer.Deserialize<LoopingExposuresEvent>(json),
                    _ => JsonSerializer.Deserialize<Phd2EventBase>(json),
                };

                // Keep AppState in sync based on event type
                switch (eventName) {
                    case "AppState":
                        AppState = ((AppStateEvent)evt).State;
                        break;
                    case "GuideStep":
                        AppState = "Guiding";
                        break;
                    case "Paused":
                        AppState = "Paused";
                        break;
                    case "StartCalibration":
                        AppState = "Calibrating";
                        break;
                    case "LoopingExposures":
                        AppState = "Looping";
                        break;
                    case "LoopingExposuresStopped":
                        AppState = "Stopped";
                        break;
                    case "StarLost":
                        AppState = "LostLock";
                        break;
                    case "Version":
                        PhDVersion = ((VersionEvent)evt).PHDVersion;
                        break;
                }

                EventReceived?.Invoke(this, evt);
            } catch (Exception ex) {
                Logger.Error($"PHD2 Client: Error processing message: {ex.Message}");
            }
        }

        private async Task<Phd2RpcResponse> CallRpcAsync(string method, object parameters = null) {
            if (!IsConnectedToPhd2) {
                throw new InvalidOperationException("Not connected to PHD2");
            }

            int id = Interlocked.Increment(ref _nextId);
            var request = new Phd2RpcRequest { Method = method, Params = parameters, Id = id };
            var json = JsonSerializer.Serialize(request) + "\r\n";

            var tcs = new TaskCompletionSource<Phd2RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            await _writeLock.WaitAsync();
            try {
                await _writer.WriteAsync(json);
            } finally {
                _writeLock.Release();
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            timeoutCts.Token.Register(() => {
                if (_pending.TryRemove(id, out _)) {
                    tcs.TrySetException(new TimeoutException($"PHD2 RPC '{method}' timed out"));
                }
            });

            return await tcs.Task;
        }

        public async Task<string> GetAppStateAsync() {
            var resp = await CallRpcAsync("get_app_state");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetString() ?? AppState;
        }

        public async Task<bool> GetConnectedAsync() {
            var resp = await CallRpcAsync("get_connected");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetBoolean() ?? false;
        }

        public async Task<bool> GetCalibratedAsync() {
            var resp = await CallRpcAsync("get_calibrated");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetBoolean() ?? false;
        }

        public async Task<int> GetExposureAsync() {
            var resp = await CallRpcAsync("get_exposure");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetInt32() ?? 0;
        }

        public async Task SetExposureAsync(int milliseconds) {
            var resp = await CallRpcAsync("set_exposure", new object[] { milliseconds });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task<int[]> GetExposureDurationsAsync() {
            var resp = await CallRpcAsync("get_exposure_durations");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            if (resp.Result == null) return Array.Empty<int>();
            return JsonSerializer.Deserialize<int[]>(resp.Result.Value.GetRawText()) ?? Array.Empty<int>();
        }

        public async Task<double[]> GetLockPositionAsync() {
            var resp = await CallRpcAsync("get_lock_position");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            if (resp.Result == null || resp.Result.Value.ValueKind == JsonValueKind.Null) return null;
            var arr = resp.Result.Value;
            return new[] { arr[0].GetDouble(), arr[1].GetDouble() };
        }

        public async Task<bool> GetPausedAsync() {
            var resp = await CallRpcAsync("get_paused");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetBoolean() ?? false;
        }

        public async Task SetPausedAsync(bool paused, bool full = false) {
            object parms = (paused && full) ? new object[] { true, "full" } : new object[] { paused };
            var resp = await CallRpcAsync("set_paused", parms);
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task<double> GetPixelScaleAsync() {
            var resp = await CallRpcAsync("get_pixel_scale");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetDouble() ?? 0d;
        }

        public async Task<int> GetSearchRegionAsync() {
            var resp = await CallRpcAsync("get_search_region");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetInt32() ?? 0;
        }

        public async Task<bool> GetGuideOutputEnabledAsync() {
            var resp = await CallRpcAsync("get_guide_output_enabled");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result?.GetBoolean() ?? false;
        }

        public async Task SetGuideOutputEnabledAsync(bool enabled) {
            var resp = await CallRpcAsync("set_guide_output_enabled", new object[] { enabled });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task<JsonElement?> GetProfileAsync() {
            var resp = await CallRpcAsync("get_profile");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result;
        }

        public async Task<JsonElement?> GetProfilesAsync() {
            var resp = await CallRpcAsync("get_profiles");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result;
        }

        public async Task SetProfileAsync(int profileId) {
            var resp = await CallRpcAsync("set_profile", new object[] { profileId });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task<JsonElement?> GetCurrentEquipmentAsync() {
            var resp = await CallRpcAsync("get_current_equipment");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result;
        }

        public async Task<JsonElement?> GetCalibrationDataAsync(string which = "Mount") {
            var resp = await CallRpcAsync("get_calibration_data", new object[] { which });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result;
        }

        public async Task<JsonElement?> GetStarImageAsync(int size = 15) {
            var resp = await CallRpcAsync("get_star_image", new object[] { size });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
            return resp.Result;
        }

        public async Task SetConnectedAsync(bool connect) {
            var resp = await CallRpcAsync("set_connected", new object[] { connect });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task GuideAsync(SettleParams settle, bool recalibrate = false) {
            var p = new { settle, recalibrate };
            var resp = await CallRpcAsync("guide", p);
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task DitherAsync(double amount, bool raOnly, SettleParams settle) {
            var p = new { amount, raOnly, settle };
            var resp = await CallRpcAsync("dither", p);
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task LoopAsync() {
            var resp = await CallRpcAsync("loop");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task StopCaptureAsync() {
            var resp = await CallRpcAsync("stop_capture");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task FlipCalibrationAsync() {
            var resp = await CallRpcAsync("flip_calibration");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task ClearCalibrationAsync(string which = "both") {
            var resp = await CallRpcAsync("clear_calibration", new object[] { which });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task FindStarAsync(int[] roi = null) {
            var resp = roi != null
                ? await CallRpcAsync("find_star", new { roi })
                : await CallRpcAsync("find_star");
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public async Task GuidePulseAsync(int amount, string direction, string which = "Mount") {
            var resp = await CallRpcAsync("guide_pulse", new object[] { amount, direction, which });
            if (resp.Error != null) throw new Exception(resp.Error.Message);
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _tcpClient?.Dispose();
            _writeLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
