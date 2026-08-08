using AndreasBehrend.NINA.Phd2Api.Phd2;
using AndreasBehrend.NINA.Phd2Api.Properties;
using AndreasBehrend.NINA.Phd2Api.WebApi;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Settings = AndreasBehrend.NINA.Phd2Api.Properties.Settings;

namespace AndreasBehrend.NINA.Phd2Api {
    /// <summary>
    /// PHD2 API Plugin for N.I.N.A.
    /// Connects to PHD2 via TCP (port 4400), exposes a REST API and WebSocket server.
    ///
    /// REST endpoints: http://localhost:{ApiPort}/api/v1/phd2/
    /// WebSocket:      ws://localhost:{ApiPort}/api/v1/events/
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class Phd2Api : PluginBase, INotifyPropertyChanged {
        private readonly IProfileService profileService;
        private readonly Phd2Client _phd2Client;
        private readonly WebSocketHandler _wsHandler;
        private readonly HttpServer _httpServer;
        private CancellationTokenSource _reconnectCts;
        private bool _isRestarting;

        public ICommand RestartCommand { get; }
        public ICommand OpenUrlCommand { get; }

        [ImportingConstructor]
        public Phd2Api(IProfileService profileService) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            this.profileService = profileService;
            profileService.ProfileChanged += ProfileService_ProfileChanged;

            _phd2Client = new Phd2Client();
            _wsHandler = new WebSocketHandler("/api/v1/events/");
            _httpServer = new HttpServer(_phd2Client, _wsHandler);

            _phd2Client.EventReceived += OnPhd2EventReceived;
            _phd2Client.Phd2Connected += OnPhd2Connected;
            _phd2Client.Phd2Disconnected += OnPhd2Disconnected;

            RestartCommand = new RelayCommand(_ => RestartAsync(), _ => !IsRestarting);
            OpenUrlCommand = new RelayCommand(url => OpenUrl(url as string));

            if (Settings.Default.ApiEnabled) {
                StartApiServer();
                _ = ConnectToPhd2WithRetryAsync();
            }
        }

        private void StartApiServer() {
            try {
                _httpServer.Start(Settings.Default.ApiPort);
                RaisePropertyChanged(nameof(ApiEndpoints));
            } catch (Exception ex) {
                Logger.Error($"PHD2 API: Failed to start HTTP server on port {Settings.Default.ApiPort}: {ex.Message}");
            }
        }

        private async Task ConnectToPhd2WithRetryAsync() {
            _reconnectCts = new CancellationTokenSource();
            var ct = _reconnectCts.Token;

            while (!ct.IsCancellationRequested) {
                if (!_phd2Client.IsConnectedToPhd2) {
                    await _phd2Client.ConnectAsync(Settings.Default.Phd2Host, Settings.Default.Phd2Port, ct);
                }
                try {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }

        private void OnPhd2Connected(object sender, EventArgs e) {
            RaisePropertyChanged(nameof(Phd2ConnectionStatus));
            _ = _wsHandler.BroadcastAsync(JsonSerializer.Serialize(new {
                Event = "Phd2ApiConnected",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            }));
        }

        private void OnPhd2Disconnected(object sender, EventArgs e) {
            RaisePropertyChanged(nameof(Phd2ConnectionStatus));
            _ = _wsHandler.BroadcastAsync(JsonSerializer.Serialize(new {
                Event = "Phd2ApiDisconnected",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            }));
        }

        private void OnPhd2EventReceived(object sender, Phd2EventBase evt) {
            // Broadcast the event to all WebSocket clients using the concrete type
            var json = JsonSerializer.Serialize(evt, evt.GetType());
            _ = _wsHandler.BroadcastAsync(json);

            // Notify WPF bindings when state-relevant events arrive
            switch (evt.Event) {
                case "AppState":
                case "GuideStep":
                case "Paused":
                case "Resumed":
                case "StarLost":
                case "GuidingStopped":
                case "LoopingExposures":
                case "LoopingExposuresStopped":
                case "StartCalibration":
                case "CalibrationComplete":
                case "CalibrationFailed":
                    RaisePropertyChanged(nameof(CurrentAppState));
                    break;
            }
        }

        public bool IsRestarting {
            get => _isRestarting;
            private set {
                _isRestarting = value;
                RaisePropertyChanged();
                (RestartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private static void OpenUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) return;
            try {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } catch (Exception ex) {
                Logger.Error($"PHD2 API: Failed to open URL '{url}': {ex.Message}");
            }
        }

        private async void RestartAsync() {
            IsRestarting = true;
            try {
                _reconnectCts?.Cancel();
                _phd2Client.Disconnect();
                _httpServer.Stop();
                await Task.Delay(500);
                if (Settings.Default.ApiEnabled) {
                    StartApiServer();
                    _ = ConnectToPhd2WithRetryAsync();
                }
            } finally {
                IsRestarting = false;
            }
        }

        public override Task Teardown() {
            _reconnectCts?.Cancel();
            profileService.ProfileChanged -= ProfileService_ProfileChanged;
            _phd2Client.EventReceived -= OnPhd2EventReceived;
            _phd2Client.Phd2Connected -= OnPhd2Connected;
            _phd2Client.Phd2Disconnected -= OnPhd2Disconnected;
            _phd2Client.Disconnect();
            _httpServer.Stop();
            _phd2Client.Dispose();
            _httpServer.Dispose();
            return base.Teardown();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
        }

        public string Phd2ConnectionStatus => _phd2Client.IsConnectedToPhd2 ? "Connected" : "Disconnected";

        public string CurrentAppState => _phd2Client.AppState;

        public bool ApiEnabled {
            get => Settings.Default.ApiEnabled;
            set {
                Settings.Default.ApiEnabled = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string Phd2Host {
            get => Settings.Default.Phd2Host;
            set {
                Settings.Default.Phd2Host = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int Phd2Port {
            get => Settings.Default.Phd2Port;
            set {
                Settings.Default.Phd2Port = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int ApiPort {
            get => Settings.Default.ApiPort;
            set {
                Settings.Default.ApiPort = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ApiEndpoints));
            }
        }

        public IEnumerable<Phd2ApiEndpoint> ApiEndpoints {
            get {
                var port = Settings.Default.ApiPort;
                foreach (var address in _httpServer.GetBoundAddresses()) {
                    yield return new Phd2ApiEndpoint(address, port);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents a reachable base address of the API server (REST, WebSocket and Swagger UI),
    /// used to display clickable links in the plugin options.
    /// </summary>
    public class Phd2ApiEndpoint {
        public Phd2ApiEndpoint(string host, int port) {
            RestUrl = $"http://{host}:{port}/api/v1/phd2/";
            WebSocketUrl = $"ws://{host}:{port}/api/v1/events/";
            SwaggerUrl = $"http://{host}:{port}/api/v1/swagger";
        }

        public string RestUrl { get; }
        public string WebSocketUrl { get; }
        public string SwaggerUrl { get; }
    }
}

