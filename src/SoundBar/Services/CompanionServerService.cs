using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoundBar.Helpers;
using SoundBar.Models;

namespace SoundBar.Services
{
    /// <summary>
    /// Hosts a lightweight HTTP + WebSocket server that serves the companion PWA
    /// and syncs audio state with connected mobile clients in real time.
    /// </summary>
    public class CompanionServerService : IDisposable
    {
        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private string? _lastBroadcastJson;
        private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
        private readonly ConcurrentDictionary<string, bool> _pairedClients = new();
        private readonly Random _random = new();

        private readonly IAudioMixerService _audioService;
        private readonly MediaInfoService _mediaInfoService;
        private readonly Func<ObservableCollection<AudioAppModel>> _getApps;
        private readonly Func<ObservableCollection<AudioDeviceModel>> _getDevices;
        private readonly Func<AudioDeviceModel?> _getSelectedDevice;
        private readonly Action<string> _setSelectedDevice;

        // Current media state cached from events
        private string _currentTitle = string.Empty;
        private string _currentArtist = string.Empty;
        private string? _currentAlbumArtBase64;
        private double _currentPositionSeconds;
        private double _currentDurationSeconds;
        private bool _currentIsPlaying;
        private DateTimeOffset _lastTimelineUpdate = DateTimeOffset.MinValue;

        /// <summary>
        /// The two-digit pairing code that mobile clients must enter to connect.
        /// Regenerated each time the server starts.
        /// </summary>
        public string PairingCode { get; private set; } = "00";

        /// <summary>
        /// The port the server is listening on.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Whether the server is currently running and accepting connections.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// The number of companion clients currently connected.
        /// </summary>
        public int ConnectedClientCount => _pairedClients.Count;

        /// <summary>
        /// Fired whenever the server state changes (started, stopped, client connected/disconnected).
        /// </summary>
        public event Action? StateChanged;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public CompanionServerService(
            IAudioMixerService audioService,
            MediaInfoService mediaInfoService,
            Func<ObservableCollection<AudioAppModel>> getApps,
            Func<ObservableCollection<AudioDeviceModel>> getDevices,
            Func<AudioDeviceModel?> getSelectedDevice,
            Action<string> setSelectedDevice,
            int port = 6767)
        {
            _audioService = audioService;
            _mediaInfoService = mediaInfoService;
            _getApps = getApps;
            _getDevices = getDevices;
            _getSelectedDevice = getSelectedDevice;
            _setSelectedDevice = setSelectedDevice;
            Port = port;

            // Subscribe to media info events so we always have fresh data
            _mediaInfoService.MediaInfoChanged += OnMediaInfoChanged;
            _mediaInfoService.TimelineInfoChanged += OnTimelineInfoChanged;
        }

        /// <summary>
        /// Starts the HTTP + WebSocket server and begins broadcasting state.
        /// </summary>
        public void Start()
        {
            if (IsRunning) return;

            try
            {
                // Generate a fresh two-digit pairing code
                PairingCode = _random.Next(10, 100).ToString();

                _cts = new CancellationTokenSource();
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://+:{Port}/");

                _httpListener.Start();
                IsRunning = true;

                // Ensure the firewall rule exists so external devices (phones) can connect
                _ = Task.Run(() => EnsureFirewallRule(Port));

                // Force initial load of media state
                _mediaInfoService.Refresh();

                // Start accepting connections on a background thread
                _ = Task.Run(() => AcceptConnectionsAsync(_cts.Token));

                // Broadcast state to all paired clients every 500ms via an async loop
                _ = Task.Run(() => BroadcastLoopAsync(_cts.Token));

                StateChanged?.Invoke();
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                // Access denied — we need to request admin elevation to add the URL ACL + firewall rule
                try
                {
                    // Step 1: Add the URL ACL reservation
                    var aclProcess = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"http add urlacl url=http://+:{Port}/ user=Everyone",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    var proc1 = System.Diagnostics.Process.Start(aclProcess);
                    proc1?.WaitForExit();

                    // Step 2: Add the firewall rule (separate elevated process to guarantee execution)
                    EnsureFirewallRule(Port);

                    // Try starting again after granting permission
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://+:{Port}/");
                    _httpListener.Start();
                    IsRunning = true;

                    _ = Task.Run(() => AcceptConnectionsAsync(_cts!.Token));
                    _ = Task.Run(() => BroadcastLoopAsync(_cts!.Token));

                    StateChanged?.Invoke();
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Companion server failed to start after UAC prompt: {innerEx.Message}");
                    IsRunning = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Companion server failed to start: {ex.Message}");
                IsRunning = false;
            }
        }

        /// <summary>
        /// Ensures a Windows Firewall inbound rule exists for the companion server port.
        /// Runs an elevated netsh command if the rule doesn't exist yet.
        /// </summary>
        public static void EnsureFirewallRule(int port)
        {
            try
            {
                // Check if rule already exists
                var checkPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall show rule name=\"SoundBar Companion\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var checkProc = System.Diagnostics.Process.Start(checkPsi);
                string output = checkProc?.StandardOutput.ReadToEnd() ?? "";
                checkProc?.WaitForExit();

                if (output.Contains("SoundBar Companion"))
                {
                    System.Diagnostics.Debug.WriteLine("Firewall rule already exists.");
                    return;
                }

                // Rule doesn't exist — add it with elevation
                var addPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"SoundBar Companion\" dir=in action=allow protocol=TCP localport={port}",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                var addProc = System.Diagnostics.Process.Start(addPsi);
                addProc?.WaitForExit();
                System.Diagnostics.Debug.WriteLine("Firewall rule added successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to ensure firewall rule: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the server and disconnects all clients.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();

            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }
            finally
            {
                _httpListener = null;
            }

            // Close all WebSocket connections gracefully in a background task to avoid UI deadlocks
            var sockets = _clients.Values.ToList();
            _ = Task.Run(async () =>
            {
                foreach (var ws in sockets)
                {
                    try
                    {
                        if (ws.State == WebSocketState.Open)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", cts.Token).ConfigureAwait(false);
                        }
                    }
                    catch { }
                }
            });

            _clients.Clear();
            _pairedClients.Clear();
            IsRunning = false;
            
            _cts?.Dispose();
            _cts = null;
            _lastBroadcastJson = null;

            StateChanged?.Invoke();
        }

        /// <summary>
        /// Gets the local IP address of this machine on the LAN.
        /// </summary>
        public static string? GetLocalIpAddress()
        {
            try
            {
                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (netInterface.OperationalStatus != OperationalStatus.Up) continue;
                    if (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = netInterface.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = addr.Address.ToString();
                            // Prefer 192.168.x.x or 10.x.x.x addresses
                            if (ip.StartsWith("192.168.") || ip.StartsWith("10."))
                            {
                                return ip;
                            }
                        }
                    }
                }

                // Fallback: use DNS
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets the full URL that the companion app should connect to.
        /// </summary>
        public string GetConnectionUrl()
        {
            string? ip = GetLocalIpAddress();
            return $"http://{ip ?? "localhost"}:{Port}";
        }

        private async Task AcceptConnectionsAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _httpListener != null && _httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();

                        if (context.Request.IsWebSocketRequest)
                        {
                            _ = Task.Run(() => HandleWebSocketAsync(context, ct));
                        }
                        else
                        {
                            HandleHttpRequest(context);
                        }
                    }
                    catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Companion server request error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Companion listener loop crashed: {ex.Message}");
            }
            finally
            {
                if (IsRunning)
                {
                    IsRunning = false;
                    StateChanged?.Invoke();
                }
            }
        }

        private void HandleHttpRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.LocalPath ?? "/";
                if (path == "/") path = "/index.html";

                // Serve static files from wwwroot
                string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
                string filePath = Path.Combine(basePath, path.TrimStart('/'));

                // Security: prevent directory traversal
                string fullPath = Path.GetFullPath(filePath);
                if (!fullPath.StartsWith(Path.GetFullPath(basePath)))
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    return;
                }

                if (File.Exists(fullPath))
                {
                    byte[] fileBytes = File.ReadAllBytes(fullPath);
                    context.Response.ContentType = GetMimeType(fullPath);
                    context.Response.ContentLength64 = fileBytes.Length;

                    // Cache static assets for 1 hour, but not HTML, SW, or manifest (so PWA updates instantly)
                    if (!fullPath.EndsWith(".html") && !fullPath.EndsWith("sw.js") && !fullPath.EndsWith("manifest.json"))
                    {
                        context.Response.Headers.Add("Cache-Control", "public, max-age=3600");
                    }
                    else
                    {
                        context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
                    }

                    context.Response.OutputStream.Write(fileBytes, 0, fileBytes.Length);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    byte[] notFound = Encoding.UTF8.GetBytes("Not Found");
                    context.Response.OutputStream.Write(notFound, 0, notFound.Length);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP serve error: {ex.Message}");
                context.Response.StatusCode = 500;
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private async Task HandleWebSocketAsync(HttpListenerContext httpContext, CancellationToken ct)
        {
            string clientId = Guid.NewGuid().ToString("N")[..8];
            WebSocket? ws = null;

            try
            {
                var wsContext = await httpContext.AcceptWebSocketAsync(null);
                ws = wsContext.WebSocket;
                _clients[clientId] = ws;

                System.Diagnostics.Debug.WriteLine($"Companion client connected: {clientId}");

                // Send a challenge requesting the pairing code
                var challenge = JsonSerializer.Serialize(new { type = "pairingRequired" }, _jsonOptions);
                await SendTextAsync(ws, challenge, ct);

                var buffer = new byte[4096];

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        ProcessClientMessage(clientId, ws, message);
                    }
                }
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebSocket error for {clientId}: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                _pairedClients.TryRemove(clientId, out _);
                ws?.Dispose(); // Prevent unmanaged handle leak
                StateChanged?.Invoke();
                System.Diagnostics.Debug.WriteLine($"Companion client disconnected: {clientId}");
            }
        }

        private void ProcessClientMessage(string clientId, WebSocket ws, string message)
        {
            try
            {
                var command = JsonSerializer.Deserialize<CompanionCommand>(message, _jsonOptions);
                if (command == null) return;

                // If the client hasn't paired yet, only accept pairing commands
                if (!_pairedClients.ContainsKey(clientId))
                {
                    if (command.Action == "pair" && command.PairingCode == PairingCode)
                    {
                        _pairedClients[clientId] = true;
                        var response = JsonSerializer.Serialize(new { type = "paired", success = true }, _jsonOptions);
                        _ = SendTextAsync(ws, response, _cts?.Token ?? CancellationToken.None);
                        StateChanged?.Invoke();
                    }
                    else if (command.Action == "pair")
                    {
                        var response = JsonSerializer.Serialize(new { type = "paired", success = false }, _jsonOptions);
                        _ = SendTextAsync(ws, response, _cts?.Token ?? CancellationToken.None);
                    }
                    return;
                }

                // Dispatch the command
                switch (command.Action)
                {
                    case "setAppVolume":
                        if (command.App != null && command.Value.HasValue)
                        {
                            float level = (float)(command.Value.Value / 100.0);
                            _audioService.SetVolume(command.App, Math.Clamp(level, 0f, 1f));
                        }
                        break;

                    case "setAppMute":
                        if (command.App != null && command.BoolValue.HasValue)
                        {
                            _audioService.SetMute(command.App, command.BoolValue.Value);
                        }
                        break;

                    case "setMasterVolume":
                        if (command.Value.HasValue)
                        {
                            float level = (float)(command.Value.Value / 100.0);
                            _audioService.SetMasterVolume(Math.Clamp(level, 0f, 1f));
                        }
                        break;

                    case "setMasterMute":
                        if (command.BoolValue.HasValue)
                        {
                            _audioService.SetMasterMute(command.BoolValue.Value);
                        }
                        break;

                    case "mediaPlayPause":
                        MediaHelper.PlayPause();
                        break;

                    case "mediaNext":
                        MediaHelper.NextTrack();
                        break;

                    case "mediaPrevious":
                        MediaHelper.PreviousTrack();
                        break;

                    case "mediaSeek":
                        if (command.Value.HasValue)
                        {
                            _ = _mediaInfoService.SeekAsync(TimeSpan.FromSeconds(command.Value.Value));
                        }
                        break;

                    case "setOutputDevice":
                        if (command.DeviceId != null)
                        {
                            _setSelectedDevice(command.DeviceId);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to process companion command: {ex.Message}");
            }
        }

        private async Task BroadcastLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(500, ct);

                    if (_pairedClients.IsEmpty) continue;

                    var snapshot = BuildStateSnapshot();
                    string json = JsonSerializer.Serialize(new { type = "state", data = snapshot }, _jsonOptions);

                    // Skip re-allocating bytes and sending if state is completely identical
                    if (json == _lastBroadcastJson) continue;
                    _lastBroadcastJson = json;

                    byte[] bytes = Encoding.UTF8.GetBytes(json);

                    foreach (var kvp in _pairedClients)
                    {
                        if (_clients.TryGetValue(kvp.Key, out var ws) && ws.State == WebSocketState.Open)
                        {
                            try
                            {
                                // Async non-blocking send with its own cancellation timeout
                                using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                sendCts.CancelAfter(2000);
                                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, sendCts.Token).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Client dropped, timeout, or closed
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Broadcast error: {ex.Message}");
                }
            }
        }

        private CompanionStateSnapshot BuildStateSnapshot()
        {
            var snapshot = new CompanionStateSnapshot
            {
                MasterVolume = (int)Math.Round(_audioService.GetMasterVolume() * 100),
                MasterMuted = _audioService.GetMasterMute()
            };

            // Apps
            try
            {
                var apps = _getApps();
                foreach (var app in apps)
                {
                    snapshot.Apps.Add(new CompanionAppState
                    {
                        Name = app.Name ?? app.DisplayName ?? "Unknown",
                        RawProcessName = app.RawProcessName ?? "",
                        Volume = app.VolumePercentage,
                        IsMuted = app.IsMuted,
                        IconBase64 = GetAppIconBase64(app.IconPath)
                    });
                }
            }
            catch { }

            // Now playing
            if (!string.IsNullOrEmpty(_currentTitle))
            {
                // Calculate live position based on timeline update time
                double livePosition = _currentPositionSeconds;
                if (_currentIsPlaying && _lastTimelineUpdate != DateTimeOffset.MinValue)
                {
                    double elapsed = (DateTimeOffset.Now - _lastTimelineUpdate).TotalSeconds;
                    livePosition = Math.Min(_currentPositionSeconds + elapsed, _currentDurationSeconds);
                }

                snapshot.NowPlaying = new CompanionNowPlaying
                {
                    Title = _currentTitle,
                    Artist = _currentArtist,
                    AlbumArtBase64 = _currentAlbumArtBase64,
                    PositionSeconds = livePosition,
                    DurationSeconds = _currentDurationSeconds,
                    IsPlaying = _currentIsPlaying
                };
            }

            // Devices
            try
            {
                var devices = _getDevices();
                foreach (var device in devices)
                {
                    snapshot.Devices.Add(new CompanionAudioDevice
                    {
                        Id = device.Id,
                        Name = device.Name
                    });
                }

                var selectedDevice = _getSelectedDevice();
                snapshot.SelectedDeviceId = selectedDevice?.Id;
            }
            catch { }

            return snapshot;
        }

        // Icon cache to avoid re-encoding icons every broadcast
        private readonly ConcurrentDictionary<string, string?> _iconCache = new();

        private string? GetAppIconBase64(string? iconPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return null;

            if (_iconCache.TryGetValue(iconPath, out var cached))
            {
                return cached;
            }

            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath);
                if (icon == null) return null;

                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                string base64 = Convert.ToBase64String(ms.ToArray());
                _iconCache[iconPath] = base64;
                return base64;
            }
            catch
            {
                _iconCache[iconPath] = null;
                return null;
            }
        }

        private void OnMediaInfoChanged(object? sender, MediaInfoEventArgs e)
        {
            _currentTitle = e.Title;
            _currentArtist = e.Artist;

            // Convert album art thumbnail to base64 JPEG
            if (e.Thumbnail != null)
            {
                _ = ConvertThumbnailAsync(e.Thumbnail);
            }
            else
            {
                _currentAlbumArtBase64 = null;
            }
        }

        private async Task ConvertThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumbnail)
        {
            try
            {
                using var stream = await thumbnail.OpenReadAsync();
                using var netStream = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await netStream.CopyToAsync(ms);
                _currentAlbumArtBase64 = Convert.ToBase64String(ms.ToArray());
            }
            catch
            {
                _currentAlbumArtBase64 = null;
            }
        }

        private void OnTimelineInfoChanged(object? sender, TimelineInfoEventArgs e)
        {
            _currentPositionSeconds = e.Position.TotalSeconds;
            _currentDurationSeconds = e.EndTime.TotalSeconds;
            _currentIsPlaying = e.IsPlaying;
            _lastTimelineUpdate = e.LastUpdatedTime;
        }

        private static async Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
        {
            if (ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private static string GetMimeType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".webmanifest" => "application/manifest+json",
                _ => "application/octet-stream"
            };
        }

        public void Dispose()
        {
            _mediaInfoService.MediaInfoChanged -= OnMediaInfoChanged;
            _mediaInfoService.TimelineInfoChanged -= OnTimelineInfoChanged;
            Stop();
            _cts?.Dispose();
        }
    }
}
