using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Net.Http;
using Microsoft.Win32;
using System.Security.Principal;
using WinForms = System.Windows.Forms;

namespace LaptopStatus
{
    public partial class MainWindow : Window
    {
        private const uint WmDropFiles = 0x0233;
        private const int Port = 8765;
        private const int ScreenPort = 8767;
        private const int ScreenPasswordIterations = 310000;
        private const int MaxScreenPasswordFailuresPerMinute = 5;
        private readonly PerformanceCounter _cpuCounter;
        private readonly HardwareTelemetry _hardwareTelemetry;
        private readonly Dictionary<string, ImageSource?> _brandLogoCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _connectionChimeLastPlayed = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<MediaPlayer> _activeConnectionChimes = new();
        private readonly DispatcherTimer _timer;
        private readonly TcpListener _listener;
        private readonly TcpListener _screenListener;
        private readonly CancellationTokenSource _stop = new();
        private readonly string _pairingKey;
        private readonly X509Certificate2 _screenCertificate;
        private readonly WinForms.NotifyIcon _trayIcon;
        private PowerScheduleNotificationWindow? _powerScheduleNotificationWindow;
        private readonly System.Drawing.Icon? _applicationIcon;
        private readonly DispatcherTimer _screenSessionTimer;
        private readonly string _screenAccessSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "screen-access.json");
        private readonly string _pairedDevicesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "paired-devices.json");
        private readonly string _cloudflareSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "cloudflare-tunnel.json");
        private readonly string _cloudflareTokenRuntimePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "cloudflare-tunnel-runtime.token");
        private readonly object _screenPasswordGate = new();
        private readonly object _pairedDevicesGate = new();
        private readonly Queue<DateTimeOffset> _screenPasswordFailures = new();
        private readonly Queue<string> _windowsHistory = new();
        private readonly string _deviceName = Environment.MachineName;
        private readonly string _deviceId;
        private readonly string _manufacturer;
        private readonly string _model;
        private readonly string _macAddress;
        private List<PairedDevice> _pairedDevices = new();
        private double _cpu, _ram;
        private HardwareSnapshot _hardwareSnapshot = new(null, null, "", Array.Empty<GpuSnapshot>());
        private string _hardwareCardsSignature = "";
        private HardwareMeterControls? _cpuHardwareMeters;
        private readonly List<HardwareMeterControls> _gpuHardwareMeters = new();
        private TextBlock? _fanSystemSpecValue;
        private TextBlock? _cpuTemperatureSystemSpecValue;
        private int? _battery;
        private JokeWindow? _jokeWindow;
        private ScreenControlIndicatorWindow? _screenIndicator;
        private volatile bool _screenControlActive;
        private volatile bool _screenControlPending;
        private DateTimeOffset _screenControlExpires;
        private byte[] _screenAccessSalt = Array.Empty<byte>();
        private byte[] _screenAccessHash = Array.Empty<byte>();
        private int _loadedScreenPasswordIterations = ScreenPasswordIterations;
        private volatile bool _screenPasswordAccessEnabled;
        private bool _allowExit;
        private bool _processRefreshInProgress;
        private int? _armedProcessKillPid;
        private Button? _armedProcessKillButton;
        private DispatcherTimer? _processKillArmTimer;
        private string? _screenAccessPasswordForDisplay;
        private bool _screenAccessPasswordVisible;
        private TcpListener? _cloudflareOriginListener;
        private Process? _cloudflaredProcess;
        private CloudflareTunnelSettings? _cloudflareSettings;
        private string _cloudflarePublicUrl = "";
        private bool _stoppingCloudflareTunnel;
        private bool _cloudflareRetryScheduled;
        private int _cloudflareRestartAttempt;
        private readonly PairingRateLimiter _pairingLimiter = new();
        private readonly string _cloudflareWorkDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "cloudflared");
        private readonly string _scheduledPowerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "scheduled-power.json");
        private readonly string _scheduledRoutinesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "scheduled-routines.json");
        private const string ScheduledPowerTaskName = @"\LaptopStatus-ScheduledPower";
        private readonly string _appearanceSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "appearance.json");
        private readonly string _jokeSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "joke-settings.json");
        private readonly string _sharedFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "SharedFromAndroid");
        private readonly object _shareGate = new();
        private SharedFile? _pendingShareFile;
        private string _pendingShareClipboard = "";
        private readonly ConcurrentDictionary<string, RemoteShellSession> _remoteShellSessions = new(StringComparer.Ordinal);
        private bool _motionAnimationsEnabled = true;
        private bool _classicThemeEnabled;
        private bool _automaticDualRouteEnabled = true;
        private bool _connectionChimesEnabled = true;
        private bool _androidJokeNotificationEnabled;
        private DateTimeOffset _androidJokeTriggerUntilUtc = DateTimeOffset.MinValue;
        private JokeWindowSettings _jokeSettings = JokeWindowSettings.Default;
        private bool _loadingAppearanceSettings;
        private bool _shareDropHighlighted;
        private readonly object _scheduledPowerGate = new();
        private readonly DispatcherTimer _scheduledPowerTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private ScheduledPower? _scheduledPower;
        private ScheduledPower? _expiredScheduledPowerOnStartup;
        private readonly object _scheduledRoutinesGate = new();
        private List<ScheduledRoutine> _scheduledRoutines = CreateDefaultScheduledRoutines();
        private bool _cloudflareSetupRunning;
        private CancellationTokenSource? _cloudflareSetupCts;
        private readonly Dictionary<TextBlock, double> _metricDisplayedValues = new();
        private readonly Dictionary<TextBlock, DispatcherTimer> _metricAnimationTimers = new();
        private readonly Dictionary<TextBlock, int> _cloudflareTextTransitionIds = new();
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private bool _cloudflareRouteCheckInProgress;
        private bool _showingPerformanceView;
        private readonly bool _resumingScheduledPower;

        private sealed record HardwareMeterControls(TextBlock FirstLabel, TextBlock FirstValue, ProgressBar FirstBar, TextBlock SecondLabel, TextBlock SecondValue, ProgressBar SecondBar);
        private sealed record ScheduledRoutine(int Index, string Name, string Action, string Time, string Days, bool Enabled, string LastRunDate);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool accept);

        [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint DragQueryFile(IntPtr dropHandle, uint fileIndex, System.Text.StringBuilder? fileName, uint bufferLength);

        [DllImport("shell32.dll", EntryPoint = "DragQueryPoint", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DragQueryPoint(IntPtr dropHandle, out NativePoint point);

        [DllImport("shell32.dll", EntryPoint = "DragFinish", ExactSpelling = true)]
        private static extern void DragFinish(IntPtr dropHandle);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        private sealed record SharedFile(string Id, string Name, byte[] Bytes);

        private bool MotionAnimationsEnabled => _motionAnimationsEnabled && SystemParameters.ClientAreaAnimation;

        public MainWindow(bool resumeScheduledPower = false)
        {
            _resumingScheduledPower = resumeScheduledPower;
            InitializeComponent();
            SourceInitialized += EnableShellFileDrop;
            LoadAppearanceSettings();
            LoadJokeSettings();
            _loadingAppearanceSettings = true;
            MotionAnimationsToggle.IsChecked = _motionAnimationsEnabled;
            ClassicThemeToggle.IsChecked = _classicThemeEnabled;
            AutomaticDualRouteToggle.IsChecked = _automaticDualRouteEnabled;
            ConnectionChimeToggle.IsChecked = _connectionChimesEnabled;
            AndroidJokeNotificationToggle.IsChecked = _androidJokeNotificationEnabled;
            _loadingAppearanceSettings = false;
            ApplyClassicTheme();
            RemoveRetiredAndroidAppJokeData();
            TryDeleteCloudflareRuntimeToken();
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))?.AddValueChanged(ConnectionInfo, (_, _) => AnimateConnectionInfo());
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))?.AddValueChanged(CloudflareTunnelStatusText, (_, _) => AnimateCloudflareStatus());
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))?.AddValueChanged(CloudflareStageText, (_, _) => AnimateCloudflareStage());
            _pairingKey = LoadOrCreateKey();
            _screenCertificate = LoadOrCreateScreenCertificate();
            _deviceId = LoadOrCreateDeviceId();
            (_manufacturer, _model) = LoadHardwareIdentity();
            _macAddress = LoadActiveNetworkMac();
            LoadPairedDevices();
            LoadScheduledPower();
            LoadScheduledRoutines();
            Loaded += (_, _) => MoveNavIndicator(SummaryNav, animate: false);
            PairingKeyText.Text = _pairingKey;
            CertificateFingerprintText.Text = Convert.ToHexString(SHA256.HashData(_screenCertificate.RawData));
            LoadScreenAccessSettings();
            UnattendedAccessToggle.IsChecked = _screenPasswordAccessEnabled;
            ScreenAccessStatus.Text = _screenPasswordAccessEnabled
                ? "Akses password tersimpan di PC ini dan aktif untuk akun Windows ini. Android harus sudah dipasangkan."
                : "Akses dengan password mati. Setiap sesi memerlukan persetujuan di laptop.";
            ToggleSavedScreenPasswordButton.Visibility = string.IsNullOrEmpty(_screenAccessPasswordForDisplay)
                ? Visibility.Collapsed
                : Visibility.Visible;
            try { AddressText.Text = string.Join("   ", GetPairingAddresses()); }
            catch (SocketException) { AddressText.Text = "Alamat IP belum tersedia"; }
            if (string.IsNullOrWhiteSpace(AddressText.Text)) AddressText.Text = "Tidak ada alamat Wi-Fi/LAN";

            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
            _hardwareTelemetry = new HardwareTelemetry();
            try { _applicationIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? ""); }
            catch { _applicationIcon = null; }
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) => RefreshStats();
            _timer.Start();
            RefreshStats();
            _scheduledPowerTimer.Tick += (_, _) => { if (!CheckScheduledPower()) CheckScheduledRoutines(); };
            _scheduledPowerTimer.Start();

            _trayIcon = CreateTrayIcon();
            if (_expiredScheduledPowerOnStartup is { } expiredSchedule)
            {
                ShowExpiredScheduledPowerNotice(expiredSchedule);
                _expiredScheduledPowerOnStartup = null;
            }
            _listener = new TcpListener(IPAddress.Any, Port);
            _screenListener = new TcpListener(IPAddress.Any, ScreenPort);
            var listenersStarted = false;
            try
            {
                _listener.Start();
                _screenListener.Start();
                listenersStarted = true;
            }
            catch (Exception ex)
            {
                _listener.Stop();
                _screenListener.Stop();
                StatusText.Text = "Layanan Tidak Aktif";
                StatusText.Foreground = Brushes.OrangeRed;
                StatusIndicator.Fill = Brushes.OrangeRed;
                ConnectionInfo.Text = $"Port {Port} atau {ScreenPort} sedang dipakai aplikasi lain. Tutup aplikasi Status Perangkat yang sudah berjalan, lalu coba lagi. ({ex.Message})";
            }
            if (listenersStarted)
            {
                ConnectionInfo.Text = $"Layanan TLS aktif di port {Port} dan {ScreenPort}. Tutup jendela untuk memindahkan aplikasi ke tray. Untuk jaringan berbeda, login Tailscale di kedua perangkat lalu pindai QR; Cloudflare tetap tersedia sebagai pilihan lain.";
                _ = AcceptClientsAsync(_stop.Token);
                _ = AcceptScreenClientsAsync(_stop.Token);
                _ = BroadcastDiscoveryAsync(_stop.Token);
            }
            _screenSessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _screenSessionTimer.Tick += (_, _) =>
            {
                if (_screenControlActive && DateTimeOffset.UtcNow >= _screenControlExpires) StopScreenSession();
            };
            _screenSessionTimer.Start();
            _cloudflareSettings = LoadCloudflareTunnelSettings();
            if (_cloudflareSettings is not null)
            {
                CloudflareHostnameBox.Text = _cloudflareSettings.Hostname;
                if (_cloudflareSettings.Enabled)
                    Dispatcher.BeginInvoke(new Action(async () => await AutoStartCloudflareTunnelAsync(_cloudflareSettings)));
                else
                    CloudflareTunnelStatusText.Text = _cloudflareSettings.Mode is "managed" or "account"
                        ? "Tunnel akun tersimpan aman. Tekan Sambungkan lewat Cloudflare untuk memulai."
                        : "Tunnel cepat terakhir kali dimatikan. Tekan Sambungkan lewat Cloudflare untuk membuat alamat baru.";
            }
            RefreshCloudflareLoginStatus();
        }

        private void EnableShellFileDrop(object? sender, EventArgs e)
        {
            // The UI stays at the user's normal integrity level so Explorer's OLE
            // drag-and-drop target is accepted. Keep WM_DROPFILES as a shell fallback.
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            DragAcceptFiles(hwnd, true);
            System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(HandleLegacyFileDrop);
        }

        private IntPtr HandleLegacyFileDrop(IntPtr hwnd, int message, IntPtr dropHandle, IntPtr lParam, ref bool handled)
        {
            if (message != WmDropFiles) return IntPtr.Zero;
            handled = true;
            try
            {
                if (!DragQueryPoint(dropHandle, out var point) || !IsShareDropPoint(point)) return IntPtr.Zero;
                if (DragQueryFile(dropHandle, uint.MaxValue, null, 0) == 0) return IntPtr.Zero;
                var length = DragQueryFile(dropHandle, 0, null, 0);
                if (length == 0 || length > 32768) return IntPtr.Zero;
                var path = new System.Text.StringBuilder(checked((int)length + 1));
                if (DragQueryFile(dropHandle, 0, path, (uint)path.Capacity) > 0)
                    QueueShareFile(path.ToString());
            }
            catch (Exception ex)
            {
                ShareStatusText.Text = "File tidak dapat dibagikan: " + ex.Message;
            }
            finally
            {
                DragFinish(dropHandle);
            }
            return IntPtr.Zero;
        }

        private bool IsShareDropPoint(NativePoint point)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var clientPoint = new Point(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
            var zoneOrigin = ShareDropZone.TranslatePoint(new Point(0, 0), this);
            var zone = new Rect(zoneOrigin, new Size(ShareDropZone.ActualWidth, ShareDropZone.ActualHeight));
            return zone.Contains(clientPoint);
        }

        private static string LoadOrCreateKey()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "pairing-key.txt");
            if (File.Exists(path)) return File.ReadAllText(path).Trim();
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            File.WriteAllText(path, key);
            return key;
        }

        private static string LoadOrCreateDeviceId()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "device-id.txt");
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (Guid.TryParseExact(existing, "N", out _)) return existing;
            }
            var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, id);
            return id;
        }

        private sealed record PairedDevice(string ClientId, string DeviceName, string TokenHash, DateTimeOffset PairedAt);
        private void LoadPairedDevices()
        {
            try
            {
                if (!File.Exists(_pairedDevicesPath)) return;
                var loaded = JsonSerializer.Deserialize<List<PairedDevice>>(File.ReadAllText(_pairedDevicesPath));
                _pairedDevices = loaded?.Where(device => Guid.TryParseExact(device.ClientId, "N", out _) &&
                    device.TokenHash.Length == 64 && device.TokenHash.All(Uri.IsHexDigit)).ToList() ?? new();
            }
            catch { _pairedDevices = new(); }
        }

        private void SavePairedDevices()
        {
            var directory = Path.GetDirectoryName(_pairedDevicesPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _pairedDevicesPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_pairedDevices));
            File.Move(temporaryPath, _pairedDevicesPath, overwrite: true);
        }

        private static void RemoveRetiredAndroidAppJokeData()
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus");
            foreach (var fileName in new[] { "android-app-catalogs.json", "android-app-catalogs.json.tmp", "android-joke-settings.json", "android-joke-settings.json.tmp" })
            {
                try { File.Delete(Path.Combine(dataDirectory, fileName)); } catch { }
            }
        }

        private async Task<string?> IssuePairingTokenAsync(string clientId, string deviceName, string code)
        {
            if (!Guid.TryParseExact(clientId, "N", out _) || code.Length != 6 || code.Any(character => character is < '0' or > '9'))
                return null;
            var safeName = new string((deviceName ?? "Android").Where(character => !char.IsControl(character)).Take(48).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Android";
            var approved = await Dispatcher.InvokeAsync(() => new PairingApprovalWindow(safeName, code).ShowDialog() == true);
            if (!approved) return null;

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            lock (_pairedDevicesGate)
            {
                _pairedDevices.RemoveAll(device => device.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
                _pairedDevices.Add(new PairedDevice(clientId, safeName, tokenHash, DateTimeOffset.UtcNow));
                SavePairedDevices();
            }
            RecordWindowsHistory($"Perangkat Android “{safeName}” dipasangkan.");
            return token;
        }

        private bool IsAuthorized(string authorizationHeader)
        {
            if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
            var token = authorizationHeader[7..].Trim();
            var suppliedBytes = Encoding.UTF8.GetBytes(token);
            var legacyBytes = Encoding.UTF8.GetBytes(_pairingKey);
            if (suppliedBytes.Length == legacyBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, legacyBytes)) return true;

            var suppliedHash = SHA256.HashData(suppliedBytes);
            lock (_pairedDevicesGate)
            {
                foreach (var device in _pairedDevices)
                {
                    try
                    {
                        var expectedHash = Convert.FromHexString(device.TokenHash);
                        if (expectedHash.Length == suppliedHash.Length && CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
                            return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private static (string Manufacturer, string Model) LoadHardwareIdentity()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                var manufacturer = key?.GetValue("SystemManufacturer")?.ToString()?.Trim();
                var model = key?.GetValue("SystemProductName")?.ToString()?.Trim();
                return (string.IsNullOrWhiteSpace(manufacturer) ? "Windows PC" : manufacturer,
                    string.IsNullOrWhiteSpace(model) ? Environment.MachineName : model);
            }
            catch { return ("Windows PC", Environment.MachineName); }
        }

        private static string LoadActiveNetworkMac()
        {
            try
            {
                var activeAddresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    .Select(address => address.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .FirstOrDefault(item => item.GetIPProperties().UnicastAddresses.Any(unicast => activeAddresses.Contains(unicast.Address.ToString())));
                var bytes = nic?.GetPhysicalAddress().GetAddressBytes();
                return bytes is { Length: 6 } ? string.Join(":", bytes.Select(value => value.ToString("X2"))) : "";
            }
            catch { return ""; }
        }

        private List<string> GetPairingAddresses()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                    item.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
                .OrderByDescending(item => item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address) && !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    .Select(address => address.Address.ToString()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tailscale = GetTailscaleAddresses();
            var all = interfaces.Concat(tailscale).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (all.Count > 0) return all;
            return AddressText.Text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(value => IPAddress.TryParse(value, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> GetTailscaleAddresses()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                        (item.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                         item.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Select(address => address.Address)
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsTailscaleIPv4(address))
                    .Select(address => address.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (NetworkInformationException) { return new List<string>(); }
        }

        private static bool IsTailscaleIPv4(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            // Tailscale assigns node addresses from the CGNAT block 100.64.0.0/10.
            return bytes.Length == 4 && bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
        }

        private void ShowPairingQr_Click(object sender, RoutedEventArgs e)
        {
            var localAddresses = GetPairingAddresses();
            var tailscaleAddress = GetTailscaleAddresses().FirstOrDefault();
            var addresses = _automaticDualRouteEnabled
                ? new List<string>(localAddresses)
                : new List<string>();
            if (_automaticDualRouteEnabled && !string.IsNullOrWhiteSpace(_cloudflarePublicUrl)) addresses.Add(_cloudflarePublicUrl);
            if (!_automaticDualRouteEnabled)
            {
                var preferredOnly = tailscaleAddress ?? (!string.IsNullOrWhiteSpace(_cloudflarePublicUrl) ? _cloudflarePublicUrl : localAddresses.FirstOrDefault());
                if (!string.IsNullOrWhiteSpace(preferredOnly)) addresses.Add(preferredOnly);
            }
            if (addresses.Count == 0)
            {
                MessageBox.Show(this, "Tidak ada alamat Wi-Fi atau Ethernet yang siap dipakai. Sambungkan laptop ke jaringan lokal lalu coba lagi.",
                    "Alamat jaringan belum tersedia", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var payload = JsonSerializer.Serialize(new
            {
                type = "LaptopStatusPairing",
                version = 1,
                deviceId = _deviceId,
                deviceName = _deviceName,
                manufacturer = _manufacturer,
                model = _model,
                macAddress = _macAddress,
                addresses,
                preferredAddress = _automaticDualRouteEnabled
                    ? localAddresses.FirstOrDefault() ?? tailscaleAddress ?? _cloudflarePublicUrl ?? addresses[0]
                    : addresses[0],
                tailscaleAddress,
                cloudflareTunnel = !string.IsNullOrWhiteSpace(_cloudflarePublicUrl),
                port = Port,
                fingerprint = Convert.ToHexString(SHA256.HashData(_screenCertificate.RawData))
            });
            new QrPairingWindow(payload, _deviceName, string.Join("  •  ", addresses), MotionAnimationsEnabled) { Owner = this }.ShowDialog();
        }

        private sealed record CloudflareTunnelSettings(string Mode, string Hostname, string? ProtectedToken, bool Enabled, string? DeviceId = null, string? TunnelId = null, string? CredentialsPath = null);

        private async Task AutoStartCloudflareTunnelAsync(CloudflareTunnelSettings? settings)
        {
            try
            {
                await StartCloudflareTunnelAsync(settings);
                _cloudflareRestartAttempt = 0;
            }
            catch (Exception ex)
            {
                TryDeleteCloudflareRuntimeToken();
                CloudflareTunnelButton.IsEnabled = true;
                CloudflareTunnelStatusText.Text = $"Tunnel otomatis belum tersambung: {ex.Message}";
                SetCloudflareStage("Tunnel otomatis belum tersambung", busy: false, failed: true);
                if (settings is { Enabled: true })
                    _ = ScheduleCloudflareReconnectAsync(settings);
            }
        }

        private async Task ScheduleCloudflareReconnectAsync(CloudflareTunnelSettings settings)
        {
            if (_cloudflareRetryScheduled || _stop.IsCancellationRequested || !settings.Enabled) return;
            _cloudflareRetryScheduled = true;
            var attempt = ++_cloudflareRestartAttempt;
            var delaySeconds = Math.Min(30, 2 * (1 << Math.Min(attempt - 1, 4)));
            CloudflareTunnelButton.Content = "Menyambungkan ulang…";
            CloudflareTunnelStatusText.Text = $"Koneksi Cloudflare terputus. Mencoba lagi dalam {delaySeconds} detik.";
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _stop.Token);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_stop.IsCancellationRequested || _cloudflareSettings is not { Enabled: true } current ||
                        _cloudflaredProcess is { HasExited: false }) return;
                    _cloudflareRetryScheduled = false;
                    _ = AutoStartCloudflareTunnelAsync(current);
                });
            }
            catch (OperationCanceledException) { }
            finally { _cloudflareRetryScheduled = false; }
        }

        private CloudflareTunnelSettings? LoadCloudflareTunnelSettings()
        {
            try
            {
                if (!File.Exists(_cloudflareSettingsPath)) return null;
                var settings = JsonSerializer.Deserialize<CloudflareTunnelSettings>(File.ReadAllText(_cloudflareSettingsPath));
                if (settings is null || (!string.IsNullOrWhiteSpace(settings.DeviceId) && !settings.DeviceId.Equals(_deviceId, StringComparison.OrdinalIgnoreCase)))
                    return null;
                return settings with { DeviceId = _deviceId };
            }
            catch { return null; }
        }

        private void PersistCloudflareTunnelSettings(CloudflareTunnelSettings settings)
        {
            var directory = Path.GetDirectoryName(_cloudflareSettingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _cloudflareSettingsPath + ".tmp";
            _cloudflareSettings = settings with { DeviceId = _deviceId };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_cloudflareSettings));
            File.Move(temporaryPath, _cloudflareSettingsPath, overwrite: true);
        }

        private static bool TryNormalizePublicHostname(string input, out string hostname)
        {
            hostname = "";
            var candidate = input.Trim();
            if (!candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) candidate = "https://" + candidate;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.Query.Length > 0 || uri.Fragment.Length > 0 || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0)) return false;
            hostname = uri.IsDefaultPort ? $"https://{uri.IdnHost}" : $"https://{uri.IdnHost}:{uri.Port}";
            return true;
        }

        private async void ToggleCloudflareTunnel_Click(object sender, RoutedEventArgs e)
        {
            if (_cloudflaredProcess is { HasExited: false })
            {
                if (_cloudflareSettings is not null)
                {
                    PersistCloudflareTunnelSettings(_cloudflareSettings with { Enabled = false });
                }
                StopCloudflareTunnelRuntime();
                CloudflareTunnelButton.Content = "Sambungkan lewat Cloudflare";
                SetCloudflareStage("", busy: false);
                CloudflareTunnelStatusText.Text = "Tunnel mati. Aktifkan lagi dari aplikasi Windows kapan saja.";
                return;
            }

            var settings = _cloudflareSettings;
            var hasManagedTunnel = settings is not null && settings.Mode == "managed" && !string.IsNullOrWhiteSpace(settings.ProtectedToken);
            var hasAccountTunnel = settings is not null && settings.Mode == "account" &&
                !string.IsNullOrWhiteSpace(settings.TunnelId) && !string.IsNullOrWhiteSpace(settings.CredentialsPath);
            if (settings is null || (!hasManagedTunnel && !hasAccountTunnel))
                settings = new CloudflareTunnelSettings("quick", "", null, true);
            else
                settings = settings with { Enabled = true };
            try
            {
                PersistCloudflareTunnelSettings(settings);
                await StartCloudflareTunnelAsync(settings);
            }
            catch (Exception ex)
            {
                CloudflareTunnelStatusText.Text = $"Tunnel tidak dapat dimulai: {ex.Message}";
                SetCloudflareStage("Tunnel gagal dimulai", busy: false, failed: true);
                CloudflareTunnelButton.Content = "Sambungkan lewat Cloudflare";
                CloudflareTunnelButton.IsEnabled = true;
                TryDeleteCloudflareRuntimeToken();
                if (settings.Enabled) _ = ScheduleCloudflareReconnectAsync(settings);
            }
        }

        private async void SaveCloudflareTunnel_Click(object sender, RoutedEventArgs e)
        {
            var token = CloudflareTokenBox.Password.Trim();
            if (!TryNormalizePublicHostname(CloudflareHostnameBox.Text, out var hostname))
            {
                CloudflareTunnelStatusText.Text = "Masukkan hostname HTTPS Cloudflare yang valid.";
                return;
            }
            if (token.Length < 32 || token.Any(char.IsWhiteSpace))
            {
                CloudflareTunnelStatusText.Text = "Token tunnel tampaknya belum lengkap. Salin token dari perintah cloudflared di dashboard.";
                CloudflareTokenBox.Clear();
                return;
            }

            SaveCloudflareTunnelButton.IsEnabled = false;
            CloudflareTunnelStatusText.Text = "Menyimpan token tunnel secara terenkripsi untuk akun Windows ini...";
            byte[]? tokenBytes = null;
            byte[]? protectedBytes = null;
            try
            {
                if (_cloudflaredProcess is { HasExited: false }) StopCloudflareTunnelRuntime();
                tokenBytes = Encoding.UTF8.GetBytes(token);
                protectedBytes = await Task.Run(() => ProtectCloudflareToken(tokenBytes));
                var settings = new CloudflareTunnelSettings("managed", hostname, Convert.ToBase64String(protectedBytes), true);
                PersistCloudflareTunnelSettings(settings);
                CloudflareHostnameBox.Text = hostname;
                CloudflareTokenBox.Clear();
                await StartCloudflareTunnelAsync(settings);
            }
            catch (Exception ex)
            {
                CloudflareTunnelStatusText.Text = $"Tunnel akun tidak dapat disimpan atau dijalankan: {ex.Message}";
                SetCloudflareStage("Tunnel tidak dapat dijalankan", busy: false, failed: true);
                CloudflareTunnelButton.IsEnabled = true;
                TryDeleteCloudflareRuntimeToken();
                if (_cloudflareSettings is { Enabled: true } settings)
                    _ = ScheduleCloudflareReconnectAsync(settings);
            }
            finally
            {
                CloudflareTokenBox.Clear();
                if (tokenBytes is not null) CryptographicOperations.ZeroMemory(tokenBytes);
                if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
                SaveCloudflareTunnelButton.IsEnabled = true;
            }
        }

        private async Task<string> EnsureCloudflaredPathAsync()
        {
            var ownPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "cloudflared.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(ownPath)!);
            if (File.Exists(ownPath) && new FileInfo(ownPath).Length >= 1_000_000) return ownPath;

            // Pakai cloudflared yang sudah terpasang di komputer bila ada, daripada mengunduh lagi.
            var installed = CloudflareAccountSetup.FindInstalledCloudflared();
            if (installed is not null) return installed;

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            var binary = await http.GetByteArrayAsync("https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe");
            if (binary.Length < 1_000_000) throw new InvalidDataException("File cloudflared dari sumber resmi tidak lengkap.");
            var temporary = ownPath + ".download";
            await File.WriteAllBytesAsync(temporary, binary);
            File.Move(temporary, ownPath, overwrite: true);
            return ownPath;
        }

        private void RefreshCloudflareLoginStatus()
        {
            var settings = _cloudflareSettings;
            string text;
            if (settings is { Mode: "account" } && !string.IsNullOrWhiteSpace(settings.TunnelId))
                text = $"Tunnel akun otomatis siap: {settings.Hostname}";
            else if (new CloudflareAccountSetup("cloudflared", _cloudflareWorkDirectory).FindOriginCert() is not null)
                text = "Login Cloudflare terdeteksi. Jika domain ada di akun lain, gunakan tombol Pilih akun Cloudflare lain.";
            else
                text = "Belum login Cloudflare. Setup otomatis akan membuka browser sekali untuk login.";
            CloudflareAccountStatusText.Text = text;
        }

        private async void SetupCloudflareAccount_Click(object sender, RoutedEventArgs e)
        {
            if (_cloudflareSetupRunning)
            {
                _cloudflareSetupCts?.Cancel();
                return;
            }

            var clickedButton = sender as Button ?? SetupCloudflareAccountButton;
            var forceLogin = ReferenceEquals(clickedButton, LoginOtherCloudflareButton);
            if (!TryNormalizePublicHostname(CloudflareHostnameBox.Text, out var hostname) || new Uri(hostname).Port != 443)
            {
                CloudflareTunnelStatusText.Text = "Isi hostname HTTPS tanpa port, contoh laptop.domainanda.com, yang berada di domain akun Cloudflare kamu.";
                return;
            }

            _cloudflareSetupRunning = true;
            _cloudflareSetupCts = new CancellationTokenSource();
            var originalContent = clickedButton.Content;
            clickedButton.Content = "Batalkan setup";
            LoginOtherCloudflareButton.IsEnabled = false;
            CloudflareTunnelButton.IsEnabled = false;
            SaveCloudflareTunnelButton.IsEnabled = false;
            try
            {
                if (!forceLogin && _cloudflareSettings is { Mode: "account", TunnelId: not null, CredentialsPath: not null } existing &&
                    existing.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase) && File.Exists(existing.CredentialsPath))
                {
                    CloudflareHostnameBox.Text = hostname;
                    if (_cloudflaredProcess is { HasExited: false })
                        CloudflareTunnelStatusText.Text = $"Tunnel akun sudah berjalan: {hostname}";
                    else
                        await StartCloudflareTunnelAsync(existing);
                    return;
                }

                if (_cloudflaredProcess is { HasExited: false }) StopCloudflareTunnelRuntime();
                CloudflareTunnelStatusText.Text = forceLogin
                    ? "Menyiapkan login Cloudflare untuk memilih akun/domain..."
                    : "Menyiapkan cloudflared...";
                var cloudflaredPath = await EnsureCloudflaredPathAsync();
                var deviceKey = _deviceId.Length >= 8 ? _deviceId[..8] : _deviceId;
                var tunnel = await new CloudflareAccountSetup(cloudflaredPath, _cloudflareWorkDirectory).SetupAsync(
                    new Uri(hostname).IdnHost,
                    deviceKey,
                    message => Dispatcher.BeginInvoke(new Action(() => CloudflareTunnelStatusText.Text = message)),
                    (url, browserAlreadyOpened) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (browserAlreadyOpened) return;
                        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                        catch { CloudflareTunnelStatusText.Text = "Buka browser secara manual dan selesaikan login Cloudflare."; }
                    })),
                    _cloudflareSetupCts.Token,
                    forceLogin);

                var settings = new CloudflareTunnelSettings("account", hostname, null, true, null, tunnel.TunnelId, tunnel.CredentialsPath);
                PersistCloudflareTunnelSettings(settings);
                CloudflareHostnameBox.Text = hostname;
                await StartCloudflareTunnelAsync(settings);
            }
            catch (OperationCanceledException)
            {
                CloudflareTunnelStatusText.Text = "Setup Cloudflare dibatalkan.";
            }
            catch (Exception ex)
            {
                CloudflareTunnelStatusText.Text = $"Setup Cloudflare gagal: {ex.Message}";
                SetCloudflareStage("Setup Cloudflare gagal", busy: false, failed: true);
                if (_cloudflareSettings is { Enabled: true } current && current.Mode == "account")
                    _ = ScheduleCloudflareReconnectAsync(current);
            }
            finally
            {
                _cloudflareSetupRunning = false;
                _cloudflareSetupCts?.Dispose();
                _cloudflareSetupCts = null;
                clickedButton.Content = originalContent;
                LoginOtherCloudflareButton.IsEnabled = true;
                CloudflareTunnelButton.IsEnabled = true;
                SaveCloudflareTunnelButton.IsEnabled = true;
                RefreshCloudflareLoginStatus();
            }
        }
        private async Task StartCloudflareTunnelAsync(CloudflareTunnelSettings? settings)
        {
            if (settings is null) throw new InvalidOperationException("Pengaturan tunnel belum tersedia.");
            if (_cloudflaredProcess is { HasExited: false }) return;
            if (!_listener.Server.IsBound) throw new InvalidOperationException("Layanan laptop belum aktif.");

            CloudflareTunnelButton.IsEnabled = false;
            SetCloudflareStage("Membuka tunnel", busy: true);
            CloudflareTunnelStatusText.Text = "Menyiapkan koneksi aman Cloudflare...";
            var cloudflaredPath = await EnsureCloudflaredPathAsync();

            _cloudflareOriginListener ??= new TcpListener(IPAddress.Loopback, 8768);
            if (!_cloudflareOriginListener.Server.IsBound)
            {
                _cloudflareOriginListener.Start();
                _ = AcceptCloudflareOriginClientsAsync(_cloudflareOriginListener, _stop.Token);
            }

            var startInfo = new ProcessStartInfo(cloudflaredPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus")
            };
            startInfo.ArgumentList.Add("tunnel");
            startInfo.ArgumentList.Add("--no-autoupdate");
            if (settings.Mode == "account")
            {
                if (!TryNormalizePublicHostname(settings.Hostname, out var accountHostname)) throw new InvalidDataException("Hostname tunnel tersimpan tidak valid.");
                if (string.IsNullOrWhiteSpace(settings.TunnelId) || string.IsNullOrWhiteSpace(settings.CredentialsPath) || !File.Exists(settings.CredentialsPath))
                    throw new InvalidOperationException("Kredensial tunnel akun tidak ditemukan. Jalankan \"Login Cloudflare & buat tunnel otomatis\" lagi.");
                var configPath = new CloudflareAccountSetup(cloudflaredPath, _cloudflareWorkDirectory)
                    .WriteConfig(settings.TunnelId, settings.CredentialsPath, new Uri(accountHostname).IdnHost, 8768);
                _cloudflarePublicUrl = accountHostname;
                startInfo.ArgumentList.Add("--config");
                startInfo.ArgumentList.Add(configPath);
                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add(settings.TunnelId);
                CloudflareTunnelStatusText.Text = "Menghubungkan tunnel akun Cloudflare...";
            }
            else if (settings.Mode == "managed")
            {
                if (!TryNormalizePublicHostname(settings.Hostname, out _)) throw new InvalidDataException("Hostname tunnel tersimpan tidak valid.");
                var encrypted = Convert.FromBase64String(settings.ProtectedToken ?? "");
                var clear = UnprotectCloudflareToken(encrypted);
                try { await File.WriteAllBytesAsync(_cloudflareTokenRuntimePath, clear); }
                finally
                {
                    CryptographicOperations.ZeroMemory(encrypted);
                    CryptographicOperations.ZeroMemory(clear);
                }
                _cloudflarePublicUrl = settings.Hostname;
                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add("--token-file");
                startInfo.ArgumentList.Add(_cloudflareTokenRuntimePath);
                CloudflareTunnelStatusText.Text = "Menghubungkan tunnel akun Cloudflare. URL stabil akan tersedia setelah dashboard mengaktifkan tunnel.";
            }
            else
            {
                _cloudflarePublicUrl = "";
                startInfo.ArgumentList.Add("--url");
                startInfo.ArgumentList.Add("http://127.0.0.1:8768");
                CloudflareTunnelStatusText.Text = "Membuat tunnel cepat. Menunggu alamat sementara dari Cloudflare...";
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += CloudflaredOutputReceived;
            process.ErrorDataReceived += CloudflaredOutputReceived;
            process.Exited += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_stoppingCloudflareTunnel || _stop.IsCancellationRequested || !ReferenceEquals(_cloudflaredProcess, process)) return;
                _cloudflaredProcess = null;
                _cloudflarePublicUrl = "";
                CloudflareTunnelButton.Content = "Sambungkan lewat Cloudflare";
                SetCloudflareStage("", busy: false);
                TryDeleteCloudflareRuntimeToken();
                if (_cloudflareSettings is { Enabled: true } settings)
                    _ = ScheduleCloudflareReconnectAsync(settings);
                else
                    CloudflareTunnelStatusText.Text = "Tunnel mati. Aktifkan lagi dari aplikasi Windows kapan saja.";
            }));
            if (!process.Start()) throw new InvalidOperationException("cloudflared gagal dijalankan.");
            _cloudflaredProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            CloudflareTunnelButton.Content = "Matikan Cloudflare Tunnel";
            CloudflareTunnelButton.IsEnabled = true;
            _cloudflareRestartAttempt = 0;
            SetCloudflareStage("Mendaftarkan hostname", busy: true);
            if (settings.Mode is "managed" or "account") CloudflareTunnelStatusText.Text = $"Tunnel berjalan; menunggu konfirmasi dari Cloudflare. QR Android dapat memakai {settings.Hostname}.";
        }

        private void SetCloudflareStage(string stage, bool busy, bool failed = false)
        {
            CloudflareProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            CloudflareStageText.Visibility = string.IsNullOrWhiteSpace(stage) ? Visibility.Collapsed : Visibility.Visible;
            CloudflareStageText.Text = stage;
            CloudflareStageText.Foreground = failed
                ? (Brush)FindResource("DangerBrush")
                : (Brush)FindResource("AccentBrush");
        }

        private async Task VerifyCloudflareRouteAsync(string publicUrl)
        {
            if (_cloudflareRouteCheckInProgress || !TryNormalizePublicHostname(publicUrl, out var hostname)) return;
            _cloudflareRouteCheckInProgress = true;
            await Dispatcher.InvokeAsync(() =>
            {
                SetCloudflareStage("Memeriksa jalur ke laptop", busy: true);
                CloudflareTunnelStatusText.Text = "Tunnel terdaftar. Memastikan alamat publik mencapai layanan laptop...";
            });
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                using var response = await client.GetAsync($"{hostname.TrimEnd('/')}/api/status", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_cloudflaredProcess is not { HasExited: false } || !_cloudflarePublicUrl.Equals(publicUrl, StringComparison.OrdinalIgnoreCase)) return;
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized)
                    {
                        SetCloudflareStage("✓ Tunnel tersambung · layanan laptop merespons", busy: false);
                        CloudflareTunnelStatusText.Text = response.StatusCode == HttpStatusCode.Unauthorized
                            ? "Jalur publik sudah mencapai layanan. Android tetap harus dipasangkan untuk membaca data."
                            : "Jalur publik dan layanan laptop berhasil diperiksa.";
                    }
                    else
                    {
                        SetCloudflareStage("Tunnel hidup · jalur aplikasi perlu diperiksa", busy: false, failed: true);
                        CloudflareTunnelStatusText.Text = $"Alamat publik merespons HTTP {(int)response.StatusCode}; periksa hostname atau aturan akses Cloudflare.";
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_cloudflaredProcess is not { HasExited: false }) return;
                    SetCloudflareStage("Tunnel terdaftar · jalur belum terverifikasi", busy: false, failed: true);
                    CloudflareTunnelStatusText.Text = $"Cloudflare menerima tunnel, tetapi pemeriksaan layanan gagal: {ex.Message}";
                });
            }
            finally
            {
                _cloudflareRouteCheckInProgress = false;
            }
        }

        private void CloudflaredOutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (_cloudflaredProcess is null || string.IsNullOrWhiteSpace(e.Data)) return;
            var quickUrl = System.Text.RegularExpressions.Regex.Match(e.Data, @"https://[a-zA-Z0-9-]+\.trycloudflare\.com").Value;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!string.IsNullOrWhiteSpace(quickUrl))
                {
                    _cloudflarePublicUrl = quickUrl;
                    CloudflareTunnelStatusText.Text = $"Tunnel cepat aktif di {quickUrl}; alamat berubah saat tunnel dimulai ulang.";
                    _ = VerifyCloudflareRouteAsync(quickUrl);
                }
                else if (e.Data.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase))
                {
                    var publicUrl = _cloudflarePublicUrl;
                    CloudflareTunnelStatusText.Text = string.IsNullOrWhiteSpace(publicUrl)
                        ? "Tunnel Cloudflare tersambung."
                        : $"Cloudflare mendaftarkan tunnel di {publicUrl}.";
                    if (!string.IsNullOrWhiteSpace(publicUrl)) _ = VerifyCloudflareRouteAsync(publicUrl);
                }
            }));
        }

        private async Task AcceptCloudflareOriginClientsAsync(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(token);
                    _ = HandleCloudflareOriginClientAsync(client, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { await Task.Delay(500, token).ContinueWith(_ => { }); }
            }
        }

        private async Task HandleCloudflareOriginClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                if (remote is null || !IPAddress.IsLoopback(remote.Address)) return;
                await HandleRequestAsync(client.GetStream(), token, secure: true, clientKey: null, viaTunnel: true);
            }
        }

        private void StopCloudflareTunnelRuntime()
        {
            _stoppingCloudflareTunnel = true;
            try
            {
                var process = _cloudflaredProcess;
                _cloudflaredProcess = null;
                if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
                process?.Dispose();
            }
            catch { }
            _cloudflarePublicUrl = "";
            try { _cloudflareOriginListener?.Stop(); } catch { }
            _cloudflareOriginListener = null;
            TryDeleteCloudflareRuntimeToken();
            _stoppingCloudflareTunnel = false;
        }

        private void TryDeleteCloudflareRuntimeToken()
        {
            try { if (File.Exists(_cloudflareTokenRuntimePath)) File.Delete(_cloudflareTokenRuntimePath); }
            catch { }
        }

        private sealed record ScreenAccessSettings(bool Enabled, string Salt, string PasswordHash, int Iterations, string? ProtectedPassword = null);

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Length;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description, ref DataBlob entropy,
            IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static readonly byte[] ScreenPasswordEntropy = Encoding.UTF8.GetBytes("LaptopStatus.ScreenAccessPassword.v1");
        private static readonly byte[] CloudflareTokenEntropy = Encoding.UTF8.GetBytes("LaptopStatus.CloudflareTunnelToken.v1");

        private static byte[] ProtectForCurrentWindowsUser(byte[] plainBytes) => TransformWithDpapi(plainBytes, protect: true, ScreenPasswordEntropy);
        private static byte[] UnprotectForCurrentWindowsUser(byte[] protectedBytes) => TransformWithDpapi(protectedBytes, protect: false, ScreenPasswordEntropy);
        private static byte[] ProtectCloudflareToken(byte[] plainBytes) => TransformWithDpapi(plainBytes, protect: true, CloudflareTokenEntropy);
        private static byte[] UnprotectCloudflareToken(byte[] protectedBytes) => TransformWithDpapi(protectedBytes, protect: false, CloudflareTokenEntropy);

        private static byte[] TransformWithDpapi(byte[] inputBytes, bool protect, byte[] entropyBytes)
        {
            var input = new DataBlob { Length = inputBytes.Length, Data = Marshal.AllocHGlobal(inputBytes.Length) };
            var entropy = new DataBlob { Length = entropyBytes.Length, Data = Marshal.AllocHGlobal(entropyBytes.Length) };
            DataBlob output = default;
            IntPtr description = IntPtr.Zero;
            try
            {
                Marshal.Copy(inputBytes, 0, input.Data, inputBytes.Length);
                Marshal.Copy(entropyBytes, 0, entropy.Data, entropyBytes.Length);
                var succeeded = protect
                    ? CryptProtectData(ref input, "LaptopStatus screen access password", ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, out description, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!succeeded) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows tidak dapat mengamankan password untuk akun ini.");
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
                Marshal.FreeHGlobal(entropy.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
                if (description != IntPtr.Zero) LocalFree(description);
            }
        }

        private void LoadScreenAccessSettings()
        {
            if (!File.Exists(_screenAccessSettingsPath)) return;
            try
            {
                var settings = JsonSerializer.Deserialize<ScreenAccessSettings>(File.ReadAllText(_screenAccessSettingsPath));
                if (settings is null || !settings.Enabled) return;
                var salt = Convert.FromBase64String(settings.Salt);
                var hash = Convert.FromBase64String(settings.PasswordHash);
                if (salt.Length != 16 || hash.Length != 32 || settings.Iterations is < 100000 or > 1000000)
                    throw new InvalidDataException("Pengaturan akses password tidak valid.");
                _screenAccessSalt = salt;
                _screenAccessHash = hash;
                _loadedScreenPasswordIterations = settings.Iterations;
                _screenPasswordAccessEnabled = true;
                if (!string.IsNullOrWhiteSpace(settings.ProtectedPassword))
                {
                    try
                    {
                        var protectedBytes = Convert.FromBase64String(settings.ProtectedPassword);
                        var passwordBytes = UnprotectForCurrentWindowsUser(protectedBytes);
                        try { _screenAccessPasswordForDisplay = Encoding.UTF8.GetString(passwordBytes); }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(passwordBytes);
                            CryptographicOperations.ZeroMemory(protectedBytes);
                        }
                    }
                    catch
                    {
                        // Keep the password verifier active if the encrypted display copy cannot be opened.
                        _screenAccessPasswordForDisplay = null;
                    }
                }
            }
            catch
            {
                _screenPasswordAccessEnabled = false;
                ScreenAccessStatus.Text = "Pengaturan akses password tidak dapat dibaca. Set ulang password dari jendela Windows.";
            }
        }

        private void PersistScreenAccessSettings(ScreenAccessSettings settings)
        {
            var directory = Path.GetDirectoryName(_screenAccessSettingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _screenAccessSettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, _screenAccessSettingsPath, overwrite: true);
        }

        private static ScreenAccessSettings CreateScreenAccessSettings(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[]? protectedBytes = null;
            try
            {
                var hash = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, ScreenPasswordIterations, HashAlgorithmName.SHA256, 32);
                protectedBytes = ProtectForCurrentWindowsUser(passwordBytes);
                return new ScreenAccessSettings(true, Convert.ToBase64String(salt), Convert.ToBase64String(hash), ScreenPasswordIterations,
                    Convert.ToBase64String(protectedBytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
                CryptographicOperations.ZeroMemory(salt);
            }
        }

        private bool VerifyScreenAccessPassword(string password)
        {
            if (!_screenPasswordAccessEnabled || password.Length < 3 || password.Any(char.IsControl) || password != password.Trim()) return false;
            lock (_screenPasswordGate)
            {
                var now = DateTimeOffset.UtcNow;
                while (_screenPasswordFailures.TryPeek(out var failedAt) && now - failedAt >= TimeSpan.FromMinutes(1))
                    _screenPasswordFailures.Dequeue();
                if (_screenPasswordFailures.Count >= MaxScreenPasswordFailuresPerMinute) return false;

                var passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[]? attemptedHash = null;
                try
                {
                    attemptedHash = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, _screenAccessSalt, _loadedScreenPasswordIterations, HashAlgorithmName.SHA256, _screenAccessHash.Length);
                    var valid = CryptographicOperations.FixedTimeEquals(attemptedHash, _screenAccessHash);
                    if (valid) _screenPasswordFailures.Clear();
                    else _screenPasswordFailures.Enqueue(now);
                    return valid;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passwordBytes);
                    if (attemptedHash is not null) CryptographicOperations.ZeroMemory(attemptedHash);
                }
            }
        }

        private async void SaveScreenAccess_Click(object sender, RoutedEventArgs e)
        {
            if (UnattendedAccessToggle.IsChecked != true)
            {
                SaveScreenAccessButton.IsEnabled = false;
                try
                {
                    await Task.Run(() => PersistScreenAccessSettings(new ScreenAccessSettings(false, "", "", ScreenPasswordIterations, null)));
                    _screenPasswordAccessEnabled = false;
                    _screenAccessSalt = Array.Empty<byte>();
                    _screenAccessHash = Array.Empty<byte>();
                    _screenAccessPasswordForDisplay = null;
                    _screenAccessPasswordVisible = false;
                    SavedScreenAccessPasswordText.Clear();
                    SavedScreenAccessPasswordText.Visibility = Visibility.Collapsed;
                    ToggleSavedScreenPasswordButton.Visibility = Visibility.Collapsed;
                    ScreenAccessStatus.Text = "Akses dengan password dimatikan. Permintaan layar kembali memerlukan persetujuan di laptop.";
                }
                catch (Exception ex) { ScreenAccessStatus.Text = $"Pengaturan tidak tersimpan: {ex.Message}"; }
                finally { SaveScreenAccessButton.IsEnabled = true; }
                return;
            }

            var password = ScreenAccessPasswordBox.Password;
            if (password.Length < 3 || password.Any(char.IsControl) || password != password.Trim())
            {
                ScreenAccessStatus.Text = "Buat password khusus minimal 3 karakter tanpa karakter kontrol.";
                ScreenAccessPasswordBox.Clear();
                return;
            }

            SaveScreenAccessButton.IsEnabled = false;
            ScreenAccessStatus.Text = "Membuat hash password yang aman...";
            try
            {
                var settings = await Task.Run(() => CreateScreenAccessSettings(password));
                await Task.Run(() => PersistScreenAccessSettings(settings));
                _screenAccessSalt = Convert.FromBase64String(settings.Salt);
                _screenAccessHash = Convert.FromBase64String(settings.PasswordHash);
                _loadedScreenPasswordIterations = settings.Iterations;
                _screenPasswordAccessEnabled = true;
                _screenAccessPasswordForDisplay = password;
                _screenAccessPasswordVisible = false;
                SavedScreenAccessPasswordText.Clear();
                SavedScreenAccessPasswordText.Visibility = Visibility.Collapsed;
                ToggleSavedScreenPasswordButton.Content = "Tampilkan password";
                ToggleSavedScreenPasswordButton.Visibility = Visibility.Visible;
                lock (_screenPasswordGate) _screenPasswordFailures.Clear();
                ScreenAccessStatus.Text = "Password tersimpan terenkripsi di PC untuk akun Windows ini. Android perlu pemasangan; sesi maksimal 10 menit.";
                RecordWindowsHistory("Akses kendali layar dengan password diaktifkan dari Windows.");
            }
            catch (Exception ex) { ScreenAccessStatus.Text = $"Pengaturan tidak tersimpan: {ex.Message}"; }
            finally
            {
                ScreenAccessPasswordBox.Clear();
                SaveScreenAccessButton.IsEnabled = true;
            }
        }

        private void ToggleSavedScreenPassword_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_screenAccessPasswordForDisplay)) return;
            _screenAccessPasswordVisible = !_screenAccessPasswordVisible;
            if (_screenAccessPasswordVisible)
            {
                SavedScreenAccessPasswordText.Text = _screenAccessPasswordForDisplay;
                SavedScreenAccessPasswordText.Visibility = Visibility.Visible;
                ToggleSavedScreenPasswordButton.Content = "Sembunyikan password";
            }
            else
            {
                SavedScreenAccessPasswordText.Clear();
                SavedScreenAccessPasswordText.Visibility = Visibility.Collapsed;
                ToggleSavedScreenPasswordButton.Content = "Tampilkan password";
            }
        }

        private static X509Certificate2 LoadOrCreateScreenCertificate()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "screen-server.pfx");
            if (File.Exists(path))
                return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);

            using var privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={Environment.MachineName} Laptop Status", privateKey, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
            return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        }

        private WinForms.NotifyIcon CreateTrayIcon()
        {
            var menu = new WinForms.ContextMenuStrip();
            var open = menu.Items.Add("Buka Status Perangkat");
            open.Click += (_, _) => Dispatcher.BeginInvoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
            var exit = menu.Items.Add("Keluar");
            exit.Click += (_, _) => Dispatcher.BeginInvoke(() => { _allowExit = true; Close(); });
            var icon = new WinForms.NotifyIcon
            {
                Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
                Text = "Status Perangkat — layanan aktif",
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
            return icon;
        }

        private void RecordWindowsHistory(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(() => RecordWindowsHistory(message));
                return;
            }
            _windowsHistory.Enqueue($"{DateTime.Now:T} — {message}");
            while (_windowsHistory.Count > 20) _windowsHistory.Dequeue();
            WindowsHistoryText.Text = string.Join(Environment.NewLine + Environment.NewLine, _windowsHistory.Reverse());
        }

        private void ClearWindowsHistory_Click(object sender, RoutedEventArgs e)
        {
            if (_windowsHistory.Count == 0)
            {
                WindowsHistoryText.Text = "Belum ada aktivitas yang tercatat.";
                return;
            }
            var result = MessageBox.Show(this, "Hapus semua riwayat aktivitas pada aplikasi Windows?", "Hapus riwayat", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            _windowsHistory.Clear();
            WindowsHistoryText.Text = "Belum ada aktivitas yang tercatat.";
        }

        private void RefreshStats()
        {
            _cpu = Math.Clamp(_cpuCounter.NextValue(), 0, 100);
            AnimateMetricValue(CpuValue, _cpu, "%");
            AnimateProgress(CpuUsageBar, _cpu);
            try
            {
                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                _ram = GlobalMemoryStatusEx(ref memory) ? memory.MemoryLoad : 0;
            }
            catch { _ram = 0; }
            AnimateMetricValue(RamValue, _ram, "%");
            AnimateProgress(RamUsageBar, _ram);
            try { _hardwareSnapshot = _hardwareTelemetry.Read(); } catch { }
            UpdateHardwarePresentation();
            try
            {
                var charge = System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent;
                _battery = charge is >= 0 and <= 1 ? (int)(charge * 100) : null;
            }
            catch { _battery = null; }
            if (_battery.HasValue) AnimateMetricValue(BatteryValue, _battery.Value, "%");
            else
            {
                StopMetricValueAnimation(BatteryValue);
                _metricDisplayedValues.Remove(BatteryValue);
                BatteryValue.Text = "N/A";
            }
            AnimateProgress(BatteryUsageBar, _battery ?? 0);
            AddHistoryValue(_cpuHistory, _cpu);
            AddHistoryValue(_ramHistory, _ram);
            AddHistoryValue(_gpuHistory, _hardwareSnapshot.GpuUsagePercent is >= 0 and <= 100 ? _hardwareSnapshot.GpuUsagePercent.Value : double.NaN);
            if (_showingPerformanceView) UpdatePerformanceView();
        }

        private static void AddHistoryValue(Queue<double> history, double value)
        {
            history.Enqueue(value);
            while (history.Count > 60) history.Dequeue();
        }

        private void ShowProcessView_Click(object sender, RoutedEventArgs e) => SwitchAnalysisView(false);

        private void ShowPerformanceView_Click(object sender, RoutedEventArgs e) => SwitchAnalysisView(true);

        private void SwitchAnalysisView(bool performance)
        {
            _showingPerformanceView = performance;
            var target = performance ? PerformanceView : ProcessView;
            var previous = performance ? ProcessView : PerformanceView;
            previous.BeginAnimation(OpacityProperty, null);
            previous.Visibility = Visibility.Collapsed;
            target.Visibility = Visibility.Visible;
            target.BeginAnimation(OpacityProperty, null);
            target.Opacity = 1;
            if (MotionAnimationsEnabled)
            {
                target.Opacity = 0;
                target.RenderTransform = new TranslateTransform(0, 9);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
                ((TranslateTransform)target.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            }
            else target.RenderTransform = Transform.Identity;

            MemoryProcessesTab.Background = new SolidColorBrush(performance ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(38, 114, 228, 191));
            MemoryProcessesTab.BorderBrush = new SolidColorBrush(performance ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(61, 114, 228, 191));
            MemoryProcessesTab.Foreground = new SolidColorBrush(performance ? Color.FromRgb(167, 181, 197) : Color.FromRgb(241, 246, 252));
            MemoryPerformanceTab.Background = new SolidColorBrush(performance ? Color.FromArgb(38, 114, 228, 191) : Color.FromArgb(0, 0, 0, 0));
            MemoryPerformanceTab.BorderBrush = new SolidColorBrush(performance ? Color.FromArgb(61, 114, 228, 191) : Color.FromArgb(0, 0, 0, 0));
            MemoryPerformanceTab.Foreground = new SolidColorBrush(performance ? Color.FromRgb(241, 246, 252) : Color.FromRgb(167, 181, 197));
            if (performance) UpdatePerformanceView();
        }

        private void UpdatePerformanceView()
        {
            PerformanceCpuValue.Text = $"{_cpu:0}%";
            PerformanceRamValue.Text = $"{_ram:0}%";
            PerformanceGpuValue.Text = _hardwareSnapshot.GpuUsagePercent is double gpu && gpu is >= 0 and <= 100 ? $"{gpu:0}%" : "—";
            var cpuTemp = _hardwareSnapshot.CpuTemperatureC is double c && c is >= 5 and <= 115 ? $"{c:0}°C" : "—";
            var gpuTemp = _hardwareSnapshot.GpuTemperatureC is double g && g is >= 5 and <= 130 ? $"{g:0}°C" : "—";
            var fan = _hardwareSnapshot.FanRpm is double rpm && rpm is > 0 and < 100000 ? $"{rpm:0} rpm" : "—";
            PerformanceSensorText.Text = $"Suhu CPU {cpuTemp}   •   Suhu GPU {gpuTemp}   •   Kipas {fan}";
            DrawHistory(CpuHistoryCanvas, _cpuHistory, Color.FromRgb(94, 183, 255));
            DrawHistory(RamHistoryCanvas, _ramHistory, Color.FromRgb(114, 228, 191));
            DrawHistory(GpuHistoryCanvas, _gpuHistory, Color.FromRgb(192, 132, 252));
        }

        private void PerformanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_showingPerformanceView) UpdatePerformanceView();
        }

        private static void DrawHistory(Canvas canvas, IEnumerable<double> values, Color color)
        {
            canvas.Children.Clear();
            var width = canvas.ActualWidth;
            var height = canvas.ActualHeight;
            if (width < 2 || height < 2) return;
            var data = values.Where(double.IsFinite).ToArray();
            for (var i = 1; i < 4; i++)
            {
                var y = height * i / 4;
                canvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(28, 167, 181, 197)), StrokeThickness = 1 });
            }
            if (data.Length < 2) return;
            var linePoints = new PointCollection();
            for (var i = 0; i < data.Length; i++)
            {
                var x = i * (width - 2) / Math.Max(1, data.Length - 1) + 1;
                var y = height - 3 - Math.Clamp(data[i], 0, 100) * (height - 6) / 100;
                linePoints.Add(new Point(x, y));
            }
            canvas.Children.Add(new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection(linePoints.Concat(new[] { new Point(width - 1, height), new Point(1, height) })),
                Fill = new SolidColorBrush(Color.FromArgb(24, color.R, color.G, color.B)),
                StrokeThickness = 0
            });
            canvas.Children.Add(new System.Windows.Shapes.Polyline { Points = linePoints, Stroke = new SolidColorBrush(color), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
        }
        private void ProcessSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyProcessSearch();

        private void ApplyProcessSearch()
        {
            if (ProcessTable.ItemsSource is null) return;
            var query = ProcessSearchBox.Text.Trim();
            var view = CollectionViewSource.GetDefaultView(ProcessTable.ItemsSource);
            view.Filter = item => item is ProcessInfo process &&
                (query.Length == 0 || process.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || process.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
            view.Refresh();
        }

        private void LoadAppearanceSettings()
        {
            try
            {
                if (!File.Exists(_appearanceSettingsPath)) return;
                var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(_appearanceSettingsPath));
                if (settings is null) return;
                _motionAnimationsEnabled = settings.MotionAnimationsEnabled;
                _classicThemeEnabled = settings.ClassicThemeEnabled;
                _automaticDualRouteEnabled = settings.AutomaticDualRouteEnabled ?? true;
                _connectionChimesEnabled = settings.ConnectionChimesEnabled ?? true;
                _androidJokeNotificationEnabled = settings.AndroidJokeNotificationEnabled ?? false;
            }
            catch { }
        }

        private void SaveAppearanceSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_appearanceSettingsPath)!);
                File.WriteAllText(_appearanceSettingsPath, JsonSerializer.Serialize(new AppearanceSettings(
                    _motionAnimationsEnabled, _classicThemeEnabled, _automaticDualRouteEnabled, _connectionChimesEnabled,
                    _androidJokeNotificationEnabled)));
            }
            catch { }
        }

        private void AppAppearanceToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingAppearanceSettings) return;
            _motionAnimationsEnabled = MotionAnimationsToggle.IsChecked == true;
            _classicThemeEnabled = ClassicThemeToggle.IsChecked == true;
            SaveAppearanceSettings();
            ApplyClassicTheme();
        }

        private void ConnectionPreferencesToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingAppearanceSettings) return;
            _automaticDualRouteEnabled = AutomaticDualRouteToggle.IsChecked == true;
            _connectionChimesEnabled = ConnectionChimeToggle.IsChecked == true;
            _androidJokeNotificationEnabled = AndroidJokeNotificationToggle.IsChecked == true;
            SaveAppearanceSettings();
        }

        private void TriggerAndroidJoke_Click(object sender, RoutedEventArgs e)
        {
            if (!_androidJokeNotificationEnabled)
            {
                JokeSettingsStatusText.Text = "Nyalakan dulu toggle joke Android pada bagian Koneksi & pemberitahuan.";
                return;
            }
            _androidJokeTriggerUntilUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            JokeSettingsStatusText.Text = "Joke Android dikirim. Pastikan Android sedang membuka aplikasi dan terhubung; aktif 30 detik.";
        }

        private void StopAndroidJoke_Click(object sender, RoutedEventArgs e)
        {
            _androidJokeTriggerUntilUtc = DateTimeOffset.MinValue;
            JokeSettingsStatusText.Text = "Pemicu joke Android dihentikan.";
        }

        private void LoadJokeSettings()
        {
            try
            {
                if (File.Exists(_jokeSettingsPath))
                {
                    var loaded = JsonSerializer.Deserialize<JokeWindowSettings>(File.ReadAllText(_jokeSettingsPath));
                    if (loaded is not null)
                    {
                        var candidateColor = loaded.BackgroundHex;
                        if (!IsValidJokeColor(candidateColor)) candidateColor = JokeWindowSettings.Default.BackgroundHex;
                        var candidateAnimation = loaded.Animation;
                        if (!IsJokeAnimation(candidateAnimation)) candidateAnimation = JokeWindowSettings.Default.Animation;
                        _jokeSettings = new JokeWindowSettings(
                            string.IsNullOrWhiteSpace(loaded.Message) ? JokeWindowSettings.Default.Message : loaded.Message.Trim()[..Math.Min(200, loaded.Message.Trim().Length)],
                            candidateColor,
                            string.IsNullOrWhiteSpace(loaded.IconGlyph) ? JokeWindowSettings.Default.IconGlyph : loaded.IconGlyph.Trim()[..Math.Min(8, loaded.IconGlyph.Trim().Length)],
                            !string.IsNullOrWhiteSpace(loaded.IconImagePath) && File.Exists(loaded.IconImagePath) ? loaded.IconImagePath : null,
                            candidateAnimation!);
                    }
                }
            }
            catch { _jokeSettings = JokeWindowSettings.Default; }

            JokeMessageBox.Text = _jokeSettings.Message;
            JokeBackgroundHexBox.Text = _jokeSettings.BackgroundHex;
            JokeIconGlyphBox.Text = _jokeSettings.IconGlyph;
            JokeIconPathText.Text = string.IsNullOrWhiteSpace(_jokeSettings.IconImagePath) ? "Ikon bawaan" : _jokeSettings.IconImagePath;
            JokeAnimationCombo.SelectedValue = _jokeSettings.Animation;
            if (JokeAnimationCombo.SelectedItem is null) JokeAnimationCombo.SelectedIndex = 0;
            JokeSettingsStatusText.Text = "Pengaturan joke tersimpan di laptop ini.";
            UpdateJokeBackgroundPreview();
        }

        private static bool IsValidJokeColor(string? value)
        {
            if (value is null || value.Length != 7 || value[0] != '#') return false;
            for (var index = 1; index < value.Length; index++)
                if (!Uri.IsHexDigit(value[index])) return false;
            return true;
        }

        private static string? GetAndroidJokeIconBase64(JokeWindowSettings settings)
        {
            try
            {
                var path = settings.IconImagePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > 131072) return null;
                return Convert.ToBase64String(File.ReadAllBytes(path));
            }
            catch { return null; }
        }

        private static bool IsJokeAnimation(string? value) =>
            value is "pulse" or "spin" or "bounce" or "shake" or "swing" or "zoom" or "fade" or "slide" or "wobble" or "pop";

        private void JokeBackgroundHexBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateJokeBackgroundPreview();

        private void UpdateJokeBackgroundPreview()
        {
            if (JokeBackgroundPreview is null || !IsValidJokeColor(JokeBackgroundHexBox.Text)) return;
            try { JokeBackgroundPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(JokeBackgroundHexBox.Text)); }
            catch { }
        }

        private void ChooseJokeBackground_Click(object sender, RoutedEventArgs e)
        {
            var initial = System.Drawing.Color.FromArgb(174, 18, 24);
            if (IsValidJokeColor(JokeBackgroundHexBox.Text))
            {
                var text = JokeBackgroundHexBox.Text;
                initial = System.Drawing.Color.FromArgb(
                    Convert.ToInt32(text.Substring(1, 2), 16),
                    Convert.ToInt32(text.Substring(3, 2), 16),
                    Convert.ToInt32(text.Substring(5, 2), 16));
            }
            using var dialog = new WinForms.ColorDialog { Color = initial, FullOpen = true, AnyColor = true };
            if (dialog.ShowDialog() != WinForms.DialogResult.OK) return;
            JokeBackgroundHexBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        }

        private void ChooseJokeIcon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih gambar ikon untuk Mode Joke",
                Filter = "Gambar|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Semua file|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "JokeIcons");
                Directory.CreateDirectory(directory);
                var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                var savedPath = Path.Combine(directory, "custom-icon" + extension);
                File.Copy(dialog.FileName, savedPath, overwrite: true);
                JokeIconPathText.Text = savedPath;
                JokeSettingsStatusText.Text = "Ikon dipilih. Tekan Simpan untuk menerapkan kustomisasi.";
            }
            catch (Exception ex) { JokeSettingsStatusText.Text = "Ikon gagal dimuat: " + ex.Message; }
        }

        private void ClearJokeIcon_Click(object sender, RoutedEventArgs e)
        {
            JokeIconPathText.Text = "Ikon bawaan";
            JokeSettingsStatusText.Text = "Ikon bawaan akan digunakan setelah disimpan.";
        }

        private void SaveJokeSettings_Click(object sender, RoutedEventArgs e)
        {
            var message = JokeMessageBox.Text.Trim();
            if (message.Length == 0)
            {
                JokeSettingsStatusText.Text = "Isi pesan joke terlebih dahulu.";
                return;
            }
            if (!IsValidJokeColor(JokeBackgroundHexBox.Text))
            {
                JokeSettingsStatusText.Text = "Warna harus memakai format #RRGGBB, contohnya #AE1218.";
                return;
            }

            var iconPath = File.Exists(JokeIconPathText.Text) ? JokeIconPathText.Text : null;
            var settings = new JokeWindowSettings(
                message[..Math.Min(200, message.Length)],
                JokeBackgroundHexBox.Text.ToUpperInvariant(),
                string.IsNullOrWhiteSpace(JokeIconGlyphBox.Text) ? JokeWindowSettings.Default.IconGlyph : JokeIconGlyphBox.Text.Trim()[..Math.Min(8, JokeIconGlyphBox.Text.Trim().Length)],
                iconPath,
                JokeAnimationCombo.SelectedValue?.ToString() ?? JokeWindowSettings.Default.Animation);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_jokeSettingsPath)!);
                var temporaryPath = _jokeSettingsPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
                File.Move(temporaryPath, _jokeSettingsPath, overwrite: true);
                _jokeSettings = settings;
                JokeSettingsStatusText.Text = "Tersimpan. Joke berikutnya memakai pengaturan ini.";
            }
            catch (Exception ex) { JokeSettingsStatusText.Text = "Pengaturan joke gagal disimpan: " + ex.Message; }
        }

        private void ApplyClassicTheme()
        {
            var classic = _classicThemeEnabled;
            Background = new SolidColorBrush(classic ? Color.FromRgb(9, 15, 24) : Color.FromRgb(11, 17, 25));
            SetBrushColor("GlassSurfaceBrush", classic ? Color.FromArgb(230, 42, 57, 72) : Color.FromRgb(24, 34, 48));
            SetBrushColor("GlassPanelBrush", classic ? Color.FromArgb(216, 35, 48, 61) : Color.FromRgb(17, 25, 35));
            SetBrushColor("PanelBrush", classic ? Color.FromArgb(211, 21, 29, 39) : Color.FromRgb(17, 25, 35));
            SetBrushColor("Panel2Brush", classic ? Color.FromArgb(207, 30, 42, 56) : Color.FromRgb(24, 34, 48));
            SetBrushColor("LineBrush", classic ? Color.FromArgb(54, 255, 255, 255) : Color.FromArgb(20, 255, 255, 255));
            SetBrushColor("GlassLineBrush", classic ? Color.FromArgb(69, 255, 255, 255) : Color.FromArgb(20, 255, 255, 255));

            var panelGradient = new LinearGradientBrush(Color.FromArgb(216, 35, 48, 61), Color.FromArgb(200, 21, 30, 41), 90);
            var cardGradient = new LinearGradientBrush(Color.FromArgb(230, 42, 57, 72), Color.FromArgb(211, 19, 27, 36), 90);
            var line = new SolidColorBrush(classic ? Color.FromArgb(69, 255, 255, 255) : Color.FromArgb(20, 255, 255, 255));
            foreach (var border in FindVisualChildren<Border>(this))
            {
                if (ReferenceEquals(border.Style, Resources["GlassPanel"]))
                {
                    border.Background = classic ? panelGradient : (Brush)Resources["GlassPanelBrush"];
                    border.BorderBrush = line;
                    border.CornerRadius = new CornerRadius(classic ? 22 : 18);
                }
                else if (ReferenceEquals(border.Style, Resources["StatCard"]))
                {
                    border.Background = classic ? cardGradient : (Brush)Resources["GlassSurfaceBrush"];
                    border.BorderBrush = line;
                    border.BorderThickness = new Thickness(classic ? 1 : 0);
                    border.CornerRadius = new CornerRadius(classic ? 20 : 16);
                    border.Effect = classic ? new DropShadowEffect { Color = Color.FromArgb(32, 0, 0, 0), BlurRadius = 22, ShadowDepth = 8, Opacity = 0.45 } : null;
                }
            }
        }

        private void SetBrushColor(string key, Color color)
        {
            // SolidColorBrush values declared as StaticResource may be frozen by WPF.
            // Replace the resource instead of mutating the frozen instance so the
            // classic theme can be applied safely during startup and at runtime.
            Resources[key] = new SolidColorBrush(color);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match) yield return match;
                foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }

        private void UpdateHardwarePresentation()
        {
            var cpu = string.IsNullOrWhiteSpace(_hardwareSnapshot.CpuModel) ? LoadProcessorName() : _hardwareSnapshot.CpuModel;
            var gpus = _hardwareSnapshot.Gpus;
            var signature = cpu + "|" + string.Join("|", gpus.Select(gpu => gpu.Model));
            if (!signature.Equals(_hardwareCardsSignature, StringComparison.Ordinal))
            {
                _hardwareCardsSignature = signature;
                DeviceHardwareCards.Children.Clear();
                DeviceSystemSpecs.Children.Clear();
                _gpuHardwareMeters.Clear();

                var cpuCard = CreateHardwareCard(
                    "Prosesor", CpuFamilyFor(cpu), LoadBrandLogo(BrandFor(cpu)), CpuModelForCard(cpu), cpu,
                    HardwareAccent(cpu), RuntimeInformation.OSArchitecture.ToString(), "Penggunaan CPU", "Thread logis");
                DeviceHardwareCards.Children.Add(cpuCard.Card);
                _cpuHardwareMeters = cpuCard.Meters;

                for (var index = 0; index < gpus.Count; index++)
                {
                    var gpu = gpus[index];
                    var kind = IsDedicatedGpu(gpu.Model) ? "Khusus" : "Terintegrasi";
                    var card = CreateHardwareCard(
                        $"Kartu grafis {index + 1}", GpuSeriesFor(gpu.Model),
                        LoadGpuBrandLogo(gpu.Model) ?? LoadBrandLogo(BrandFor(gpu.Model)),
                        GpuModelForCard(gpu.Model), gpu.Model, HardwareAccent(gpu.Model), kind,
                        "Beban GPU", "Suhu GPU");
                    DeviceHardwareCards.Children.Add(card.Card);
                    _gpuHardwareMeters.Add(card.Meters);
                }

                var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                var totalMemory = GlobalMemoryStatusEx(ref memory) ? FormatBytes(memory.TotalPhysical) : "—";
                var fanText = _hardwareSnapshot.FanRpm is > 0 and < 100000 ? $"{_hardwareSnapshot.FanRpm:0} RPM" : "—";
                AddSystemSpec("RAM terpasang", totalMemory);
                _cpuTemperatureSystemSpecValue = AddSystemSpec("Suhu CPU", "—");
                _fanSystemSpecValue = AddSystemSpec("Kipas", fanText);
                AddSystemSpec("Arsitektur", RuntimeInformation.OSArchitecture.ToString());
                AddSystemSpec("Sistem operasi", Environment.OSVersion.VersionString, wide: true);
            }

            if (_cpuHardwareMeters is { } cpuMeters)
            {
                SetHardwareMeter(cpuMeters.FirstLabel, cpuMeters.FirstValue, cpuMeters.FirstBar, "Penggunaan CPU", _cpu, "%", 100);
                SetHardwareMeter(cpuMeters.SecondLabel, cpuMeters.SecondValue, cpuMeters.SecondBar, "Thread logis", Environment.ProcessorCount, "", Math.Max(Environment.ProcessorCount, 1));
            }

            for (var index = 0; index < _gpuHardwareMeters.Count; index++)
            {
                var meters = _gpuHardwareMeters[index];
                var gpu = index < gpus.Count ? gpus[index] : null;
                if (gpu is null)
                {
                    SetHardwareMeter(meters.FirstLabel, meters.FirstValue, meters.FirstBar, "Beban GPU", null, "%", 100);
                    SetHardwareMeter(meters.SecondLabel, meters.SecondValue, meters.SecondBar, "", null, "°C", 100);
                    continue;
                }
                SetHardwareMeter(meters.FirstLabel, meters.FirstValue, meters.FirstBar, "Beban GPU", gpu.UsagePercent, "%", 100);
                SetHardwareMeter(meters.SecondLabel, meters.SecondValue, meters.SecondBar, "Suhu GPU", gpu.TemperatureC, "°C", 100);
            }

            var cpuTemperature = _hardwareSnapshot.CpuTemperatureC is >= 5 and <= 115 ? $"{_hardwareSnapshot.CpuTemperatureC:0}°C" : "—";
            var fan = _hardwareSnapshot.FanRpm is > 0 and < 100000 ? $"{_hardwareSnapshot.FanRpm:0} RPM" : "—";
            if (_cpuTemperatureSystemSpecValue is not null) _cpuTemperatureSystemSpecValue.Text = cpuTemperature;
            if (_fanSystemSpecValue is not null) _fanSystemSpecValue.Text = fan;
            var gpuCount = gpus.Count;
            TroubleshootingStatusText.Text = $"Layanan Windows: {StatusText.Text}. Prosesor: {cpuTemperature}. Kartu grafis terdeteksi: {gpuCount}. Kipas: {fan}. Sensor yang tidak dilaporkan perangkat tetap ditampilkan sebagai —.";
        }

        private (Border Card, HardwareMeterControls Meters) CreateHardwareCard(
            string role, string badge, ImageSource? logo, string model, string rawModel,
            Color accent, string kind, string firstLabel, string secondLabel)
        {
            var accentBrush = new SolidColorBrush(accent);
            var cardBody = new StackPanel { Margin = new Thickness(18, 16, 16, 14) };
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 12), MinHeight = 40 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brandStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (logo is not null)
                brandStack.Children.Add(new Image { Source = logo, Width = 52, Height = 34, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 9, 0) });
            if (!string.IsNullOrWhiteSpace(badge))
                brandStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(115, accent.R, accent.G, accent.B)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9, 4, 9, 4), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = badge, Foreground = accentBrush, FontSize = 12, FontWeight = FontWeights.Bold }
                });
            Grid.SetColumn(brandStack, 0);
            heading.Children.Add(brandStack);
            var kindPill = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(22, 33, 55)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 48, 74)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(99), Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = kind, Foreground = new SolidColorBrush(Color.FromRgb(138, 153, 181)), FontSize = 10 }
            };
            Grid.SetColumn(kindPill, 1);
            heading.Children.Add(kindPill);
            cardBody.Children.Add(heading);
            cardBody.Children.Add(new TextBlock { Text = role, Foreground = new SolidColorBrush(Color.FromRgb(138, 153, 181)), FontSize = 12.5, Margin = new Thickness(0, 0, 0, 2) });
            cardBody.Children.Add(new TextBlock { Text = model, Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 249)), FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, LineHeight = 25 });
            cardBody.Children.Add(new TextBlock { Text = rawModel, Foreground = new SolidColorBrush(Color.FromRgb(138, 153, 181)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 12) });

            var metersGrid = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
            metersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            metersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var firstMeter = CreateHardwareMeter(firstLabel, accentBrush);
            var secondMeter = CreateHardwareMeter(secondLabel, accentBrush);
            Grid.SetColumn(secondMeter.Container, 2);
            metersGrid.Children.Add(firstMeter.Container);
            metersGrid.Children.Add(secondMeter.Container);
            cardBody.Children.Add(metersGrid);

            var glow = new System.Windows.Shapes.Ellipse
            {
                Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -60, -60, 0),
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new(Color.FromArgb(45, accent.R, accent.G, accent.B), 0),
                    new(Color.FromArgb(0, accent.R, accent.G, accent.B), 1)
                })
            };
            var cardGrid = new Grid { ClipToBounds = true };
            cardGrid.Children.Add(glow);
            cardGrid.Children.Add(cardBody);
            var stripe = new Border { Background = accentBrush, Width = 4, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(14, 0, 0, 14) };
            var outer = new Grid();
            outer.Children.Add(cardGrid);
            outer.Children.Add(stripe);
            var card = new Border
            {
                Width = 330, MinHeight = 190, Margin = new Thickness(0, 0, 14, 14),
                Background = new SolidColorBrush(Color.FromRgb(22, 33, 55)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 48, 74)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Child = outer
            };
            return (card, new HardwareMeterControls(firstMeter.Label, firstMeter.Value, firstMeter.Bar, secondMeter.Label, secondMeter.Value, secondMeter.Bar));
        }

        private static (StackPanel Container, TextBlock Label, TextBlock Value, ProgressBar Bar) CreateHardwareMeter(string label, Brush accent)
        {
            var caption = new Grid();
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labelText = new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(138, 153, 181)), FontSize = 10 };
            var valueText = new TextBlock { Text = "—", Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 249)), FontSize = 10, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(valueText, 1);
            caption.Children.Add(labelText);
            caption.Children.Add(valueText);
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, Margin = new Thickness(0, 5, 0, 0), Foreground = accent, Background = new SolidColorBrush(Color.FromRgb(13, 22, 38)), BorderThickness = new Thickness(0) };
            var container = new StackPanel();
            container.Children.Add(caption);
            container.Children.Add(bar);
            return (container, labelText, valueText, bar);
        }

        private static void SetHardwareMeter(TextBlock labelText, TextBlock valueText, ProgressBar bar, string label, double? value, string unit, double maximum)
        {
            var available = value is double v && double.IsFinite(v) && v >= 0;
            labelText.Text = label;
            valueText.Text = available ? $"{value:0}{unit}" : "—";
            bar.Value = available ? Math.Clamp(value!.Value / Math.Max(maximum, 1) * 100, 0, 100) : 0;
        }

        private TextBlock AddSystemSpec(string label, string value, bool wide = false)
        {
            var card = new Border
            {
                Width = wide ? 360 : 170, MinHeight = 62, Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 10, 14, 10), Background = new SolidColorBrush(Color.FromRgb(22, 33, 55)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 48, 74)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12)
            };
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(138, 153, 181)), FontSize = 11 });
            var valueText = new TextBlock { Text = value, Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 249)), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            content.Children.Add(valueText);
            card.Child = content;
            DeviceSystemSpecs.Children.Add(card);
            return valueText;
        }

        private static string GpuModelForCard(string model)
        {
            var normalized = System.Text.RegularExpressions.Regex.Replace(model ?? "", @"\((?:R|TM|r|tm)\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^NVIDIA\s+|^AMD\s+|^Intel\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return normalized.Length == 0 ? model ?? string.Empty : normalized;
        }

        private static string CpuModelForCard(string model)
        {
            var normalized = System.Text.RegularExpressions.Regex.Replace(model ?? "", @"\((?:R|TM|r|tm)\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\bRyzen\s+(?:AI\s+)?[3579]\b", options))
                normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^.*?\bRyzen\s+(?:AI\s+)?[3579]\s*[-]?\s*", "", options);
            else if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\bCore\s+Ultra\s+[579]\b", options))
                normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^.*?\bCore\s+Ultra\s+[579]\s*", "", options);
            else if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\bCore\s+i[3579]\b", options))
                normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^.*?\bCore\s+i[3579][-\s]*", "", options);
            else if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\bSnapdragon\s+", options))
            {
                var snapdragonModel = System.Text.RegularExpressions.Regex.Match(normalized, @"\bX1[\w-]+", options);
                normalized = snapdragonModel.Success
                    ? snapdragonModel.Value
                    : System.Text.RegularExpressions.Regex.Replace(normalized, @"^.*?\bSnapdragon\s+", "", options);
            }
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+with\s+Radeon.*$|\s+CPU\s*@.*$|\s+CPU$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? model ?? string.Empty : normalized;
        }

        private static bool IsDedicatedGpu(string model)
        {
            var name = model ?? "";
            return name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\bArc\s+A\d")
                || System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\bRX\s?\d");
        }

        private static Color HardwareAccent(string hardwareName)
        {
            if (hardwareName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) return Color.FromRgb(118, 185, 0);
            if (hardwareName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return Color.FromRgb(237, 28, 36);
            if (hardwareName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Adreno", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase)) return Color.FromRgb(91, 124, 255);
            if (hardwareName.Contains("Intel", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Iris", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("UHD", StringComparison.OrdinalIgnoreCase)) return Color.FromRgb(10, 143, 232);
            return Color.FromRgb(94, 234, 212);
        }

        private static string CpuFamilyFor(string model)
        {
            var match = System.Text.RegularExpressions.Regex.Match(model ?? "", @"(?i)\bRyzen\s+(AI\s+)?[3579]\b|\bCore\s+Ultra\s+[579]\b|\bCore\s+i[3579]\b|\bSnapdragon\s+X\s+\w+\b");
            return match.Success ? match.Value.Trim() : BrandWordmark(model ?? "", false);
        }

        private static string GpuSeriesFor(string model)
        {
            var name = model ?? "";
            if (name.Contains("RTX", StringComparison.OrdinalIgnoreCase)) return "GeForce RTX";
            if (name.Contains("GTX", StringComparison.OrdinalIgnoreCase)) return "GeForce GTX";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\bMX\s?\d")) return "GeForce MX";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\bGT\s?\d")) return "GeForce GT";
            if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return "Intel Arc";
            if (name.Contains("Iris", StringComparison.OrdinalIgnoreCase)) return "Intel Iris Xe";
            if (name.Contains("UHD", StringComparison.OrdinalIgnoreCase)) return "Intel UHD";
            if (name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)) return "Intel HD";
            if (name.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) || name.Contains("Adreno", StringComparison.OrdinalIgnoreCase)) return "Qualcomm Adreno";
            if (name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)) return "AMD Radeon RX";
            if (name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return "AMD Radeon";
            return "Grafis terintegrasi";
        }

        private ImageSource? LoadGpuBrandLogo(string hardwareName)
        {
            var name = hardwareName ?? "";
            var key = name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ? "rtx"
                : name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ? "gtx"
                : System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\bMX\s?\d") ? "mx"
                : System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\bGT\s?\d") ? "gt"
                : name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? "arc"
                : name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ? "uhd"
                : name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ? "hd"
                : name.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase) || name.Contains("Adreno", StringComparison.OrdinalIgnoreCase) ? "snapdragon"
                : name.Contains("Mobility", StringComparison.OrdinalIgnoreCase) || name.Contains("ATI", StringComparison.OrdinalIgnoreCase) ? "radeon-ati"
                : name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "radeon"
                : null;
            if (key is null) return null;
            var cacheKey = "series:" + key;
            if (_brandLogoCache.TryGetValue(cacheKey, out var cached)) return cached;
            try
            {
                var resourceName = GetType().Assembly.GetManifestResourceNames()
                    .FirstOrDefault(resource => resource.EndsWith($".Assets.Brand.Series.{key}.png", StringComparison.OrdinalIgnoreCase));
                if (resourceName is null) return null;
                using var stream = GetType().Assembly.GetManifestResourceStream(resourceName);
                if (stream is null) return null;
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                _brandLogoCache[cacheKey] = bitmap;
                return bitmap;
            }
            catch { return null; }
        }

        private static string LoadProcessorName()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private static string BrandWordmark(string hardwareName, bool isGpu)
        {
            if (string.IsNullOrWhiteSpace(hardwareName)) return isGpu ? "GPU" : "CPU";
            if (hardwareName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
            if (hardwareName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return isGpu ? "RADEON" : "AMD RYZEN";
            if (hardwareName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                if (hardwareName.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return "intel ARC";
                if (hardwareName.Contains("Iris", StringComparison.OrdinalIgnoreCase)) return "intel IRIS";
                if (hardwareName.Contains("UHD", StringComparison.OrdinalIgnoreCase)) return "intel UHD";
                if (hardwareName.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)) return "intel HD";
                return "intel";
            }
            return isGpu ? "GPU" : "CPU";
        }

        private static string? BrandFor(string hardwareName)
        {
            if (string.IsNullOrWhiteSpace(hardwareName)) return null;
            if (hardwareName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Quadro", StringComparison.OrdinalIgnoreCase)) return "nvidia";
            if (hardwareName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return "amd";
            if (hardwareName.Contains("Intel", StringComparison.OrdinalIgnoreCase) || hardwareName.Contains("Iris", StringComparison.OrdinalIgnoreCase)) return "intel";
            return null;
        }

        private ImageSource? LoadBrandLogo(string? brand)
        {
            if (string.IsNullOrWhiteSpace(brand)) return null;
            if (_brandLogoCache.TryGetValue(brand, out var cached)) return cached;
            try
            {
                var resourceName = GetType().Assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith($".Assets.Brand.{brand}.svg", StringComparison.OrdinalIgnoreCase));
                if (resourceName is null) return null;
                using var stream = GetType().Assembly.GetManifestResourceStream(resourceName);
                if (stream is null) return null;
                var svg = XDocument.Load(stream);
                var pathData = svg.Descendants().FirstOrDefault(element => element.Name.LocalName == "path")?.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(pathData)) return null;
                var color = brand switch
                {
                    "amd" => Color.FromRgb(237, 28, 36),
                    "intel" => Color.FromRgb(0, 113, 197),
                    "nvidia" => Color.FromRgb(118, 185, 0),
                    _ => Color.FromRgb(114, 228, 191)
                };
                var drawing = new GeometryDrawing(new SolidColorBrush(color), null, Geometry.Parse(pathData));
                var image = new DrawingImage(drawing);
                image.Freeze();
                _brandLogoCache[brand] = image;
                return image;
            }
            catch { return null; }
        }

        private sealed record AppearanceSettings(
            bool MotionAnimationsEnabled,
            bool ClassicThemeEnabled,
            bool? AutomaticDualRouteEnabled = null,
            bool? ConnectionChimesEnabled = null,
            bool? AndroidJokeNotificationEnabled = null);

        private const int MaximumSharedFileBytes = 25 * 1024 * 1024;

        private void ShareDropZone_DragEnter(object sender, DragEventArgs e)
        {
            _shareDropHighlighted = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = _shareDropHighlighted ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            UpdateShareDropAppearance();
        }

        private void ShareDropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ShareDropZone_DragLeave(object sender, DragEventArgs e)
        {
            _shareDropHighlighted = false;
            UpdateShareDropAppearance();
        }

        private void ShareDropZone_Drop(object sender, DragEventArgs e)
        {
            _shareDropHighlighted = false;
            UpdateShareDropAppearance();
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
            QueueShareFile(paths[0]);
            e.Handled = true;
        }

        private void UpdateShareDropAppearance()
        {
            ShareDropZone.BorderBrush = new SolidColorBrush(_shareDropHighlighted ? Color.FromRgb(114, 228, 191) : Color.FromArgb(61, 114, 228, 191));
            ShareDropZone.Background = new SolidColorBrush(_shareDropHighlighted ? Color.FromRgb(35, 68, 73) : Color.FromRgb(32, 46, 60));
            ShareDropHint.Text = _shareDropHighlighted ? "Lepaskan untuk mengirim file ke Android" : "Jatuhkan file untuk membagikannya";
        }

        private void ChooseShareFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "Pilih file untuk dikirim ke Android", CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(this) == true) QueueShareFile(dialog.FileName);
        }

        private void QueueShareFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) throw new FileNotFoundException("File tidak ditemukan.");
                if (info.Length > MaximumSharedFileBytes) throw new InvalidDataException("Ukuran file maksimal 25 MB.");
                var bytes = File.ReadAllBytes(path);
                lock (_shareGate) _pendingShareFile = new SharedFile(Guid.NewGuid().ToString("N"), Path.GetFileName(path), bytes);
                ShareStatusText.Text = $"{Path.GetFileName(path)} siap diambil dari Android ({FormatBytes((ulong)bytes.Length)}).";
            }
            catch (Exception ex) { ShareStatusText.Text = "File tidak dapat dibagikan: " + ex.Message; }
        }

        private void ShareClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
                if (string.IsNullOrWhiteSpace(text))
                {
                    ShareStatusText.Text = "Clipboard Windows kosong atau tidak berisi teks.";
                    return;
                }
                if (Encoding.UTF8.GetByteCount(text) > 128 * 1024) throw new InvalidDataException("Teks clipboard maksimal 128 KB.");
                lock (_shareGate) _pendingShareClipboard = text;
                ShareStatusText.Text = "Teks clipboard siap diambil dari Android.";
            }
            catch (Exception ex) { ShareStatusText.Text = "Clipboard tidak dapat dibagikan: " + ex.Message; }
        }

        private void OpenSharedFilesFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_sharedFilesPath);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_sharedFilesPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { ShareStatusText.Text = "Folder file berbagi tidak dapat dibuka: " + ex.Message; }
        }

        private void AnimateMetricValue(TextBlock target, double value, string suffix)
        {
            value = Math.Clamp(value, 0, 100);
            var from = _metricDisplayedValues.GetValueOrDefault(target, 0);
            StopMetricValueAnimation(target);
            if (!MotionAnimationsEnabled || Math.Abs(from - value) < 0.5)
            {
                _metricDisplayedValues[target] = value;
                target.Text = $"{value:F0}{suffix}";
                return;
            }

            var started = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _metricAnimationTimers[target] = timer;
            timer.Tick += (_, _) =>
            {
                var progress = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / 900.0, 0, 1);
                var eased = 1 - Math.Pow(1 - progress, 4);
                var displayed = from + (value - from) * eased;
                _metricDisplayedValues[target] = displayed;
                target.Text = $"{displayed:F0}{suffix}";
                if (progress < 1) return;
                timer.Stop();
                _metricAnimationTimers.Remove(target);
                _metricDisplayedValues[target] = value;
                target.Text = $"{value:F0}{suffix}";
            };
            timer.Start();
        }

        private void StopMetricValueAnimation(TextBlock target)
        {
            if (!_metricAnimationTimers.Remove(target, out var timer)) return;
            timer.Stop();
        }

        private void AnimateProgress(ProgressBar bar, double target)
        {
            target = Math.Clamp(target, bar.Minimum, bar.Maximum);
            if (!MotionAnimationsEnabled)
            {
                bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
                bar.Value = target;
                return;
            }
            var current = bar.Value;
            if (Math.Abs(current - target) < 0.5) return;
            bar.Value = target;
            bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(900))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void AnimateCloudflareStatus()
        {
            AnimateCloudflareText(CloudflareTunnelStatusText);
        }

        private void AnimateCloudflareStage()
        {
            AnimateCloudflareText(CloudflareStageText);
        }

        private void AnimateCloudflareText(TextBlock target)
        {
            if (!MotionAnimationsEnabled || !IsLoaded || target.Text.Length == 0)
            {
                target.Opacity = 1;
                return;
            }

            var transition = _cloudflareTextTransitionIds.GetValueOrDefault(target) + 1;
            _cloudflareTextTransitionIds[target] = transition;
            target.BeginAnimation(OpacityProperty, null);
            var fadeOut = new DoubleAnimation(target.Opacity, 0.25, TimeSpan.FromMilliseconds(130));
            fadeOut.Completed += (_, _) =>
            {
                if (_cloudflareTextTransitionIds.GetValueOrDefault(target) != transition) return;
                target.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(400))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            };
            target.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void AnimateConnectionInfo()
        {
            if (!MotionAnimationsEnabled)
            {
                ConnectionInfo.BeginAnimation(OpacityProperty, null);
                ConnectionInfo.Opacity = 1;
                return;
            }
            ConnectionInfo.Opacity = 0.65;
            ConnectionInfo.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private sealed record ScheduledPower(string Action, long AtUtc, string Message);

        private static readonly ConcurrentDictionary<string, string> ProcessIconBase64Cache = new(StringComparer.OrdinalIgnoreCase);

        private sealed record ProcessInfo(int Pid, string Name, ulong WorkingSetBytes, bool Responding, bool CanTerminate, string TerminationNote, string IconBase64, string ExecutablePath)
        {
            public string MemoryText => $"{WorkingSetBytes / 1024d / 1024d:N1} MB";
        }

        private static string ReadProcessIconBase64(string executablePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return string.Empty;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
                if (icon is null) return string.Empty;
                using var source = icon.ToBitmap();
                using var bitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, 32, 32);
                }
                using var stream = new MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return Convert.ToBase64String(stream.ToArray());
            }
            catch { return string.Empty; }
        }

        private static IReadOnlyList<ProcessInfo> GetProcessSnapshot()
        {
            var result = new List<ProcessInfo>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id <= 4) continue;
                        var name = process.ProcessName;
                        var bytes = process.WorkingSet64;
                        var responding = true;
                        try { responding = process.Responding; } catch { }
                        var canTerminate = ProcessKillHelper.CanTerminate(process, out var terminationNote);
                        var executablePath = string.Empty;
                        try { executablePath = process.MainModule?.FileName ?? string.Empty; } catch { }
                        result.Add(new ProcessInfo(process.Id, name, (ulong)Math.Max(0, bytes), responding, canTerminate, terminationNote, string.Empty, executablePath));
                    }
                    catch { }
                }
            }
            return result.OrderByDescending(item => item.WorkingSetBytes).Take(100)
                .Select(item => item with
                {
                    IconBase64 = ProcessIconBase64Cache.GetOrAdd(item.Name, _ => ReadProcessIconBase64(item.ExecutablePath))
                })
                .ToArray();
        }

        private static bool IsOnAcPower()
        {
            try
            {
                return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            }
            catch { return false; }
        }

        private static ulong GetTotalPhysicalMemoryBytes()
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            return GlobalMemoryStatusEx(ref memory) ? memory.TotalPhysical : 0;
        }

        private static object GetMemorySnapshot()
        {
            var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
            if (GetPerformanceInfo(ref info, info.Size))
            {
                static ulong Bytes(nuint pages, nuint pageSize) => checked((ulong)pages * (ulong)pageSize);
                var total = Bytes(info.PhysicalTotal, info.PageSize);
                var available = Bytes(info.PhysicalAvailable, info.PageSize);
                var processes = Process.GetProcesses();
                ulong processWorkingSet = 0;
                foreach (var process in processes)
                {
                    using (process)
                    {
                        try { processWorkingSet += (ulong)Math.Max(0, process.WorkingSet64); } catch { }
                    }
                }
                return new
                {
                    totalBytes = total,
                    usedBytes = total > available ? total - available : 0,
                    availableBytes = available,
                    systemCacheBytes = Bytes(info.SystemCache, info.PageSize),
                    committedBytes = Bytes(info.CommitTotal, info.PageSize),
                    commitLimitBytes = Bytes(info.CommitLimit, info.PageSize),
                    pagedPoolBytes = Bytes(info.KernelPaged, info.PageSize),
                    nonPagedPoolBytes = Bytes(info.KernelNonpaged, info.PageSize),
                    processWorkingSetBytes = processWorkingSet,
                    processCount = info.ProcessCount
                };
            }

            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            GlobalMemoryStatusEx(ref memory);
            return new
            {
                totalBytes = memory.TotalPhysical,
                usedBytes = memory.TotalPhysical > memory.AvailablePhysical ? memory.TotalPhysical - memory.AvailablePhysical : 0,
                availableBytes = memory.AvailablePhysical,
                systemCacheBytes = 0UL,
                committedBytes = memory.TotalPageFile > memory.AvailablePageFile ? memory.TotalPageFile - memory.AvailablePageFile : 0,
                commitLimitBytes = memory.TotalPageFile,
                pagedPoolBytes = 0UL,
                nonPagedPoolBytes = 0UL,
                processWorkingSetBytes = 0UL,
                processCount = Process.GetProcesses().Length
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PerformanceInformation
        {
            public uint Size;
            public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable;
            public nuint SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);

        private async Task<bool> RunProcessTerminationAsync(int pid)
        {
            string message;
            var terminated = false;
            try
            {
                using var target = Process.GetProcessById(pid);
                if (!ProcessKillHelper.CanTerminate(target, out var reason))
                {
                    message = $"Proses {target.ProcessName} ({pid}) tidak dihentikan: {reason}";
                    await Dispatcher.InvokeAsync(() => RecordWindowsHistory(message));
                    return false;
                }
                var processName = target.ProcessName;
                try
                {
                    await PrivilegedActionRunner.TerminateProcessAsync(pid);
                    message = $"Proses {processName} ({pid}) dihentikan.";
                    terminated = true;
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    message = $"Windows menolak penghentian {processName} ({pid}). Proses tersebut dilindungi Windows atau membutuhkan hak administrator.";
                }
            }
            catch (OperationCanceledException ex) { message = ex.Message; }
            catch (Exception ex) { message = $"Gagal menghentikan proses {pid}: {ex.Message}"; }
            await Dispatcher.InvokeAsync(() => RecordWindowsHistory(message));
            return terminated;
        }

        private async Task RefreshMemoryDetailsAsync()
        {
            try
            {
                var json = await Task.Run(() => JsonSerializer.SerializeToElement(GetMemorySnapshot()));
                var total = json.GetProperty("totalBytes").GetUInt64();
                var used = json.GetProperty("usedBytes").GetUInt64();
                var available = json.GetProperty("availableBytes").GetUInt64();
                var percent = total == 0 ? 0 : Math.Clamp(used * 100d / total, 0, 100);
                MemoryUsageBar.Value = percent;
                MemorySummaryText.Text = $"RAM fisik: {FormatBytes(used)} digunakan dari {FormatBytes(total)} ({percent:F0}%). Tersedia {FormatBytes(available)}.";
                MemoryBreakdownText.Text = $"Cache sistem: {FormatBytes(json.GetProperty("systemCacheBytes").GetUInt64())}   •   Commit: {FormatBytes(json.GetProperty("committedBytes").GetUInt64())} / {FormatBytes(json.GetProperty("commitLimitBytes").GetUInt64())}\nPool kernel paged: {FormatBytes(json.GetProperty("pagedPoolBytes").GetUInt64())}   •   non-paged: {FormatBytes(json.GetProperty("nonPagedPoolBytes").GetUInt64())}\nTotal working set proses: {FormatBytes(json.GetProperty("processWorkingSetBytes").GetUInt64())} (dapat menghitung halaman bersama lebih dari sekali).";
            }
            catch (Exception ex) { MemorySummaryText.Text = $"Data memori tidak dapat dibaca: {ex.Message}"; }
        }

        private async Task RefreshProcessesAsync()
        {
            if (_processRefreshInProgress) return;
            ResetProcessKillArm();
            _processRefreshInProgress = true;
            try
            {
                ProcessTable.ItemsSource = await Task.Run(GetProcessSnapshot);
                ApplyProcessSearch();
            }
            catch (Exception ex) { MemoryBreakdownText.Text = $"Daftar proses gagal dimuat: {ex.Message}"; }
            finally { _processRefreshInProgress = false; }
        }

        private async void RunRamMapAction_Click(object sender, RoutedEventArgs e)
        {
            if (RamMapActionSelector.SelectedItem is not ComboBoxItem selected || selected.Tag is not string action) return;
            var label = selected.Content?.ToString() ?? "Aksi memori";
            var confirm = MessageBox.Show(
                $"Jalankan {label}?\n\nWindows dapat memuat ulang halaman memori. Aplikasi lain mungkin terasa lambat untuk sementara. Pastikan pekerjaan penting sudah disimpan.",
                "Konfirmasi aksi RAMMap", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            RunRamMapActionButton.IsEnabled = false;
            RamMapActionStatus.Text = "Menjalankan aksi memori...";
            try
            {
                var message = await PrivilegedActionRunner.RunRamMapActionAsync(action);
                RamMapActionStatus.Text = message;
                await RefreshMemoryDetailsAsync();
                RecordWindowsHistory("Aksi RAMMap: " + action);
            }
            catch (Exception ex)
            {
                RamMapActionStatus.Text = ex.Message;
            }
            finally { RunRamMapActionButton.IsEnabled = true; }
        }

        private async void RefreshProcessList_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button) button.IsEnabled = false;
            await RefreshMemoryDetailsAsync();
            await RefreshProcessesAsync();
            if (sender is Button refreshButton) refreshButton.IsEnabled = true;
        }

        private async void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: int pid } button || button.DataContext is not ProcessInfo process) return;
            if (_armedProcessKillPid != pid)
            {
                ResetProcessKillArm();
                _armedProcessKillPid = pid;
                _armedProcessKillButton = button;
                button.Content = "Yakin?";
                button.Background = new SolidColorBrush(Color.FromRgb(229, 91, 106));
                button.Foreground = Brushes.White;
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(114, 228, 191));
                _processKillArmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
                _processKillArmTimer.Tick += (_, _) => ResetProcessKillArm();
                _processKillArmTimer.Start();
                return;
            }

            _processKillArmTimer?.Stop();
            _processKillArmTimer = null;
            _armedProcessKillPid = null;
            _armedProcessKillButton = null;
            button.IsEnabled = false;
            button.Content = "Menghentikan...";
            var terminated = await RunProcessTerminationAsync(pid);
            if (terminated && FindVisualAncestor<DataGridRow>(button) is { } row)
                await AnimateProcessRowCollapseAsync(row);
            await RefreshProcessesAsync();
        }

        private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent is not null && parent is not T) parent = VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        private async Task AnimateProcessRowCollapseAsync(DataGridRow row)
        {
            if (!MotionAnimationsEnabled) return;
            var height = Math.Max(1, row.ActualHeight);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            row.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = ease });
            row.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(height, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = ease });
            await Task.Delay(500);
        }

        private void ResetProcessKillArm()
        {
            _processKillArmTimer?.Stop();
            _processKillArmTimer = null;
            if (_armedProcessKillButton is { } button)
            {
                button.Content = "Hentikan";
                button.ClearValue(BackgroundProperty);
                button.ClearValue(ForegroundProperty);
                button.ClearValue(BorderBrushProperty);
            }
            _armedProcessKillButton = null;
            _armedProcessKillPid = null;
        }

        private static string FormatBytes(ulong bytes)
        {
            var value = (double)bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:N1} {units[unit]}";
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        private void StatCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement card) return;
            e.Handled = true;
            OpenStatCard(card.Tag?.ToString() ?? "");
        }

        private void StatCard_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Space) || sender is not FrameworkElement card) return;
            e.Handled = true;
            OpenStatCard(card.Tag?.ToString() ?? "");
        }

        private void OpenStatCard(string metric)
        {
            switch (metric)
            {
                case "cpu":
                    NavigateToPageByKey("analysis");
                    SwitchAnalysisView(true);
                    MemoryPerformanceTab.Focus();
                    break;
                case "ram":
                    NavigateToPageByKey("analysis");
                    SwitchAnalysisView(false);
                    MemoryProcessesTab.Focus();
                    break;
                case "battery":
                    ShowBatteryDetails();
                    break;
            }
        }

        private void ShowBatteryDetails()
        {
            var powerStatus = WinForms.SystemInformation.PowerStatus;
            var chargeStatus = powerStatus.BatteryChargeStatus;
            var noBattery = chargeStatus == WinForms.BatteryChargeStatus.NoSystemBattery;
            var statusUnknown = chargeStatus == WinForms.BatteryChargeStatus.Unknown;
            var onAcPower = powerStatus.PowerLineStatus == WinForms.PowerLineStatus.Online;
            var onBatteryPower = powerStatus.PowerLineStatus == WinForms.PowerLineStatus.Offline;
            var isCharging = !statusUnknown && chargeStatus.HasFlag(WinForms.BatteryChargeStatus.Charging);
            var stateText = noBattery
                ? "Windows tidak mendeteksi baterai."
                : statusUnknown
                    ? "Windows belum melaporkan status pengisian baterai."
                : isCharging
                    ? "Sedang mengisi daya."
                    : onAcPower
                        ? "Adaptor daya tersambung; status pengisian tidak dilaporkan Windows."
                        : onBatteryPower
                            ? "Sedang menggunakan daya baterai."
                            : "Status sumber daya belum dilaporkan Windows.";
            var remainingSeconds = powerStatus.BatteryLifeRemaining;
            var remaining = remainingSeconds >= 0 ? TimeSpan.FromSeconds(remainingSeconds) : TimeSpan.Zero;
            var remainingText = noBattery
                ? "—"
                : onAcPower
                    ? "Perkiraan waktu sampai penuh tidak dilaporkan Windows."
                    : onBatteryPower && remainingSeconds >= 0
                        ? $"Perkiraan sisa daya: {(int)remaining.TotalHours} jam {remaining.Minutes} menit."
                        : "Perkiraan waktu pemakaian tidak dilaporkan Windows.";

            var valueText = _battery.HasValue ? $"{_battery.Value}%" : noBattery ? "Tidak ada baterai" : "Tidak tersedia";
            var accent = (Brush)FindResource(_battery is < 20 ? "DangerBrush" : "AccentBrush");
            var workArea = SystemParameters.WorkArea;
            var dialogWidth = Math.Max(300, Math.Min(420, workArea.Width - 32));
            var dialogHeight = Math.Max(240, Math.Min(410, workArea.Height - 32));
            var dialog = new Window
            {
                Title = "Status baterai",
                Owner = this,
                Width = dialogWidth,
                MinWidth = Math.Min(320, dialogWidth),
                MaxWidth = dialogWidth,
                Height = dialogHeight,
                MinHeight = Math.Min(260, dialogHeight),
                MaxHeight = dialogHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(11, 17, 25)),
                Foreground = (Brush)FindResource("TextBrush"),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            var details = new StackPanel();
            details.Children.Add(new TextBlock { Text = "Status baterai", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
            details.Children.Add(new TextBlock { Text = valueText, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = accent, Margin = new Thickness(0, 12, 0, 5) });
            details.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = _battery ?? 0, Height = 8, Foreground = accent, Background = new SolidColorBrush(Color.FromRgb(42, 53, 66)) });
            details.Children.Add(new TextBlock { Text = stateText, FontSize = 12, Foreground = (Brush)FindResource("TextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 5) });
            details.Children.Add(new TextBlock { Text = onAcPower ? "Sumber daya: adaptor AC" : "Sumber daya: baterai", FontSize = 11, Foreground = (Brush)FindResource("TextDimBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            details.Children.Add(new TextBlock { Text = remainingText, FontSize = 11, Foreground = (Brush)FindResource("TextDimBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12) });
            var closeButton = new Button { Content = "Tutup", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 7, 16, 7) };
            closeButton.Click += (_, _) => dialog.Close();
            var body = new Grid { Margin = new Thickness(22) };
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var detailsScroll = new ScrollViewer
            {
                Content = details,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Margin = new Thickness(0, 0, 0, 8)
            };
            body.Children.Add(detailsScroll);
            Grid.SetRow(closeButton, 1);
            body.Children.Add(closeButton);
            dialog.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 25, 35)),
                BorderBrush = (Brush)FindResource("GlassLineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(8),
                Child = body
            };
            dialog.ShowDialog();
        }

        private void NavigateToPage(object sender, MouseButtonEventArgs e)
        {
            var page = (sender as FrameworkElement)?.Tag?.ToString() ?? "summary";
            NavigateToPageByKey(page);
        }

        private void NavigateToPageByKey(string page)
        {
            var target = page switch { "history" => HistoryPanel, "settings" => SettingsPanel, "analysis" => MemoryPanel, "device-details" => DeviceDetailsPanel, _ => SummaryPanel };
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            foreach (var panel in new[] { SummaryPanel, HistoryPanel, SettingsPanel, MemoryPanel, DeviceDetailsPanel }) panel.Visibility = panel == target ? Visibility.Visible : Visibility.Collapsed;
            if (MotionAnimationsEnabled)
            {
                target.Opacity = 0;
                target.RenderTransform = new TranslateTransform(0, 22);
                target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(430)) { EasingFunction = ease });
                ((TranslateTransform)target.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(560)) { EasingFunction = ease });
            }
            else
            {
                target.BeginAnimation(OpacityProperty, null);
                target.Opacity = 1;
                target.RenderTransform = Transform.Identity;
            }

            HeaderTitle.Text = page switch { "history" => "Riwayat Perangkat", "settings" => "Pengaturan", "analysis" => "Statistik & Troubleshooting", "device-details" => "Rincian perangkat", _ => "Ringkasan Sistem" };
            HeaderTitle.Opacity = 0.6;
            HeaderTitle.Margin = new Thickness(8, 0, 0, 0);
            if (MotionAnimationsEnabled)
            {
                var titleSlide = new Duration(TimeSpan.FromMilliseconds(280));
                HeaderTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, 1, titleSlide) { EasingFunction = ease });
                HeaderTitle.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(8, 0, 0, 0), new Thickness(0), titleSlide) { EasingFunction = ease });
            }
            else
            {
                HeaderTitle.BeginAnimation(OpacityProperty, null);
                HeaderTitle.BeginAnimation(MarginProperty, null);
                HeaderTitle.Opacity = 1;
                HeaderTitle.Margin = new Thickness(0);
            }

            SetNavState(SummaryNav, page == "summary");
            SetNavState(MemoryNav, page == "analysis");
            SetNavState(DeviceDetailsNav, page == "device-details");
            SetNavState(HistoryNav, page == "history");
            SetNavState(SettingsNav, page == "settings");
            MoveNavIndicator(page switch { "analysis" => MemoryNav, "device-details" => DeviceDetailsNav, "history" => HistoryNav, "settings" => SettingsNav, _ => SummaryNav });
            if (page == "settings") SettingsScroll.ScrollToTop();
            if (page == "analysis")
            {
                _ = RefreshMemoryDetailsAsync();
                _ = RefreshProcessesAsync();
            }
            if (page == "device-details") UpdateHardwarePresentation();
        }

        private void OpenGuide_Click(object sender, RoutedEventArgs e)
        {
            new OnboardingGuideWindow { Owner = this }.ShowDialog();
        }

        private void SetNavState(Border item, bool selected)
        {
            item.Background = Brushes.Transparent;
            item.BorderBrush = Brushes.Transparent;
            foreach (var child in (item.Child as System.Windows.Controls.StackPanel)?.Children.OfType<FrameworkElement>() ?? Enumerable.Empty<FrameworkElement>())
            {
                if (child is System.Windows.Shapes.Path path) path.Stroke = selected ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("TextDimBrush");
                else if (child is System.Windows.Controls.TextBlock text) text.Foreground = selected ? (Brush)FindResource("TextBrush") : (Brush)FindResource("TextDimBrush");
            }
            item.RenderTransform = new TranslateTransform();
            if (MotionAnimationsEnabled)
                item.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(selected ? 4 : 0, TimeSpan.FromMilliseconds(230))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
        }

        private void MoveNavIndicator(Border target, bool animate = true)
        {
            if (!SidebarNavRoot.IsLoaded || !target.IsLoaded) return;
            var targetY = target.TransformToAncestor(SidebarNavRoot).Transform(new Point(0, 0)).Y;
            NavIndicator.Height = target.ActualHeight;
            NavIndicator.Opacity = 1;
            if (NavIndicator.RenderTransform is not TranslateTransform transform)
                NavIndicator.RenderTransform = transform = new TranslateTransform();
            if (!animate || !MotionAnimationsEnabled)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = targetY;
                return;
            }

            var startY = transform.Y;
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = startY;
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                Value = targetY,
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)),
                KeySpline = new KeySpline(0.32, 0.72, 0, 1)
            });
            transform.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        private void MainContent_Loaded(object sender, RoutedEventArgs e)
        {
            MoveNavIndicator(SummaryNav, animate: false);
            if (!MotionAnimationsEnabled)
            {
                MainContent.Opacity = 1;
                MainContent.RenderTransform = Transform.Identity;
                return;
            }
            MainContent.Opacity = 0;
            MainContent.RenderTransform = new TranslateTransform(0, 14);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            MainContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
            ((TranslateTransform)MainContent.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(620)) { EasingFunction = ease });
        }

        private void GlassCard_MouseEnter(object sender, MouseEventArgs e) => AnimateGlassCard(sender, true);
        private void GlassCard_MouseLeave(object sender, MouseEventArgs e) => AnimateGlassCard(sender, false);

        private void AnimateGlassCard(object sender, bool hovered)
        {
            if (sender is not Border card) return;
            if (!MotionAnimationsEnabled)
            {
                card.RenderTransform = Transform.Identity;
                return;
            }
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            if (card.RenderTransform is not ScaleTransform transform || transform.IsFrozen)
                card.RenderTransform = transform = new ScaleTransform(1, 1);
            var scale = hovered ? 1.012 : 1.0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            card.BorderBrush = hovered ? new SolidColorBrush(Color.FromArgb(0x77, 0x72, 0xE4, 0xBF)) : (Brush)FindResource("GlassLineBrush");
            card.Effect = hovered
                ? new DropShadowEffect { Color = Color.FromArgb(0x22, 0x72, 0xE4, 0xBF), BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35 }
                : null;
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = HandleClientAsync(client, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { await Task.Delay(500, token).ContinueWith(_ => { }); }
            }
        }

        private async Task AcceptScreenClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _screenListener.AcceptTcpClientAsync(token);
                    _ = HandleScreenClientAsync(client, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { await Task.Delay(500, token).ContinueWith(_ => { }); }
            }
        }

        private Task HandleScreenClientAsync(TcpClient client, CancellationToken token)
            => HandleTlsClientAsync(client, token);

        private async Task HandleTlsClientAsync(TcpClient client, CancellationToken token)
        {
            var remoteKey = NormalizeClientKey((client.Client.RemoteEndPoint as IPEndPoint)?.Address);
            using (client)
            using (var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
            {
                using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _screenCertificate,
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }, handshakeTimeout.Token);
                    await HandleRequestAsync(tls, token, secure: true, clientKey: remoteKey);
                }
                catch { }
            }
        }

        private async Task BroadcastDiscoveryAsync(CancellationToken token)
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var announcement = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        service = "LaptopStatus",
                        deviceId = _deviceId,
                        port = Port,
                        securePort = ScreenPort,
                        deviceName = _deviceName,
                        manufacturer = _manufacturer,
                        model = _model,
                        macAddress = _macAddress,
                        fingerprint = Convert.ToHexString(SHA256.HashData(_screenCertificate.RawData))
                    });
                    foreach (var endpoint in GetDiscoveryBroadcastEndpoints())
                        await udp.SendAsync(announcement, endpoint, token);
                }
                catch (OperationCanceledException) { break; }
                catch { }
                try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
                catch (OperationCanceledException) { break; }
            }
        }

        private static IReadOnlyList<IPEndPoint> GetDiscoveryBroadcastEndpoints()
        {
            var endpoints = new HashSet<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(item =>
                    item.OperationalStatus == OperationalStatus.Up &&
                    item.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet))
                {
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses.Where(item =>
                        item.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(item.Address) &&
                        !item.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)))
                    {
                        var subnetMask = unicast.IPv4Mask;
                        if (subnetMask is null) continue;
                        var address = unicast.Address.GetAddressBytes();
                        var mask = subnetMask.GetAddressBytes();
                        if (address.Length != 4 || mask.Length != 4) continue;
                        var broadcast = new byte[4];
                        for (var index = 0; index < broadcast.Length; index++)
                            broadcast[index] = (byte)(address[index] | (byte)~mask[index]);
                        endpoints.Add(new IPAddress(broadcast));
                    }
                }
            }
            catch (NetworkInformationException) { }

            return endpoints.Select(address => new IPEndPoint(address, 8766)).ToArray();
        }

        private static bool IsCurrentProcessElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private bool TryGetRemoteShellSession(string path, string suppliedKey, out RemoteShellSession? session)
        {
            session = null;
            var id = QueryValue(path, "sessionId");
            if (string.IsNullOrWhiteSpace(id) || !_remoteShellSessions.TryGetValue(id, out var candidate)) return false;
            if (candidate.IsExpired)
            {
                RemoveRemoteShellSession(id);
                return false;
            }

            var ownerHash = RemoteShellSession.HashOwnerKey(suppliedKey);
            if (!candidate.OwnerKeyHash.Equals(ownerHash, StringComparison.Ordinal)) return false;
            session = candidate;
            return true;
        }

        private void RemoveRemoteShellSessionsForOwner(string ownerHash)
        {
            foreach (var pair in _remoteShellSessions)
                if (pair.Value.OwnerKeyHash.Equals(ownerHash, StringComparison.Ordinal))
                    RemoveRemoteShellSession(pair.Key);
        }

        private void CleanupExpiredRemoteShellSessions()
        {
            foreach (var pair in _remoteShellSessions)
                if (pair.Value.IsExpired) RemoveRemoteShellSession(pair.Key);
        }

        private void RemoveRemoteShellSession(string id)
        {
            if (_remoteShellSessions.TryRemove(id, out var session)) session.Dispose();
        }

        private Task HandleClientAsync(TcpClient client, CancellationToken token)
            => HandleTlsClientAsync(client, token);

        private static string NormalizeClientKey(IPAddress? address)
        {
            if (address is null) return "unknown";
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }

        private async Task HandleRequestAsync(Stream stream, CancellationToken token, bool secure, string? clientKey = null, bool viaTunnel = false)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var buffer = new byte[4096];
                using var requestBytes = new MemoryStream();
                var headerEnd = -1;
                while (headerEnd < 0)
                {
                    var count = await stream.ReadAsync(buffer, requestTimeout.Token);
                    if (count == 0 || requestBytes.Length + count > 16384) return;
                    requestBytes.Write(buffer, 0, count);
                    headerEnd = FindHttpHeaderEnd(requestBytes.GetBuffer(), (int)requestBytes.Length);
                }
                var text = Encoding.ASCII.GetString(requestBytes.GetBuffer(), 0, headerEnd);
                var firstLine = text.Split("\r\n", 2)[0];
                var requestParts = firstLine.Split(' ');
                var method = requestParts.ElementAtOrDefault(0) ?? "";
                var path = requestParts.ElementAtOrDefault(1) ?? "";
                var lengthHeader = text.Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                var contentLength = lengthHeader is not null && int.TryParse(lengthHeader[(lengthHeader.IndexOf(':') + 1)..].Trim(), out var parsedLength) ? parsedLength : 0;
                var maximumBodyBytes = method == "POST" && path.StartsWith("/api/share/file?", StringComparison.Ordinal)
                    ? MaximumSharedFileBytes
                    : method == "POST" && path == "/api/share/clipboard" ? 128 * 1024
                    : method == "POST" && (path == "/api/shell/start"
                        || path.StartsWith("/api/shell/input?", StringComparison.Ordinal)
                        || path.StartsWith("/api/shell/stop?", StringComparison.Ordinal)) ? 16 * 1024
                    : 0;
                if (contentLength < 0 || contentLength > maximumBodyBytes)
                {
                    var message = Encoding.UTF8.GetBytes("{\"error\":\"request_body_too_large\"}");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 413 Payload Too Large\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {message.Length}\r\nConnection: close\r\n\r\n"), token);
                    await stream.WriteAsync(message, token);
                    return;
                }
                if (contentLength > 0) requestTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                var bodyOffset = headerEnd + 4;
                var alreadyRead = Math.Clamp((int)requestBytes.Length - bodyOffset, 0, contentLength);
                using var bodyStream = new MemoryStream(contentLength);
                if (alreadyRead > 0) bodyStream.Write(requestBytes.GetBuffer(), bodyOffset, alreadyRead);
                while (bodyStream.Length < contentLength)
                {
                    var remaining = (int)(contentLength - bodyStream.Length);
                    var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), requestTimeout.Token);
                    if (count == 0) return;
                    bodyStream.Write(buffer, 0, count);
                }
                var requestBody = bodyStream.ToArray();
                var isPairingRequest = secure && method == "GET" && path.StartsWith("/api/pair?", StringComparison.Ordinal);
                var rateKey = clientKey ?? "unknown";
                if (viaTunnel)
                {
                    // Lewat tunnel semua koneksi tampak berasal dari loopback; alamat klien asli ada di header Cloudflare.
                    var cfLine = text.Split("\r\n").FirstOrDefault(line => line.StartsWith("CF-Connecting-IP:", StringComparison.OrdinalIgnoreCase));
                    var cfValue = cfLine is null ? "" : cfLine[(cfLine.IndexOf(':') + 1)..].Trim();
                    rateKey = "cf:" + (IPAddress.TryParse(cfValue, out var cfAddress) ? NormalizeClientKey(cfAddress) : "unknown");
                }
                var auth = text.Split("\r\n").FirstOrDefault(line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));
                var screenPasswordHeader = text.Split("\r\n").FirstOrDefault(line => line.StartsWith("X-LaptopStatus-Screen-Password:", StringComparison.OrdinalIgnoreCase));
                var encodedScreenPassword = screenPasswordHeader is null ? "" : screenPasswordHeader[(screenPasswordHeader.IndexOf(':') + 1)..].Trim();
                string screenPassword;
                try { screenPassword = Encoding.UTF8.GetString(Convert.FromBase64String(encodedScreenPassword)); }
                catch { screenPassword = ""; }
                int status;
                int retryAfterSeconds = 0;
                byte[] responseBytes;
                string contentType = "application/json; charset=utf-8";
                string? screenSource = null;
                string? control = null;
                string controlMessage = "";
                int? processKillPid = null;
                var suppliedKey = auth is null ? "" : auth[(auth.IndexOf(':') + 1)..].Trim();
                var authenticatedClient = secure && IsAuthorized(suppliedKey);
                if (isPairingRequest)
                {
                    var decision = _pairingLimiter.TryBegin(rateKey, out var retryWait);
                    if (decision != PairingRateLimiter.Decision.Allowed)
                    {
                        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryWait.TotalSeconds));
                        status = 429;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            error = decision == PairingRateLimiter.Decision.Busy ? "pairing_in_progress" : "pairing_rate_limited",
                            retryAfterSeconds
                        });
                    }
                    else
                    {
                        string? issuedToken = null;
                        try
                        {
                            requestTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                            var clientId = QueryValue(path, "clientId");
                            var clientName = QueryValue(path, "deviceName");
                            var code = QueryValue(path, "code");
                            issuedToken = await IssuePairingTokenAsync(clientId, clientName, code);
                        }
                        finally
                        {
                            _pairingLimiter.End(rateKey, issuedToken is not null);
                        }

                        if (issuedToken is null)
                        {
                            status = 403;
                            responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"pairing_denied_or_invalid\"}");
                        }
                        else
                        {
                            status = 200;
                            responseBytes = JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                deviceId = _deviceId,
                                deviceName = _deviceName,
                                manufacturer = _manufacturer,
                                model = _model,
                                macAddress = _macAddress,
                                token = issuedToken,
                                fingerprint = Convert.ToHexString(SHA256.HashData(_screenCertificate.RawData))
                            });
                        }
                    }
                }
                else if (!authenticatedClient)
                {
                    status = 401; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"unauthorized\"}");
                }
                else if (secure && method == "POST" && path == "/api/shell/start")
                {
                    try
                    {
                        using var shellRequest = JsonDocument.Parse(requestBody);
                        var shellType = shellRequest.RootElement.TryGetProperty("type", out var typeValue)
                            ? typeValue.GetString() ?? "" : "";
                        var shellScreenPassword = shellRequest.RootElement.TryGetProperty("screenPassword", out var passwordValue)
                            ? passwordValue.GetString() ?? "" : "";
                        if (!_screenPasswordAccessEnabled || !VerifyScreenAccessPassword(shellScreenPassword))
                        {
                            status = 403;
                            responseBytes = JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                error = "screen_password_required",
                                message = "Password kendali layar tidak valid atau belum diaktifkan di Windows."
                            });
                        }
                        else if (IsCurrentProcessElevated())
                        {
                            status = 403;
                            responseBytes = JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                error = "windows_app_is_elevated",
                                message = "Aplikasi Windows sedang berjalan sebagai Administrator. Tutup lalu buka LaptopStatus secara biasa agar terminal Android memakai hak akun standar dan tidak melewati UAC."
                            });
                        }
                        else if (shellType.Trim().ToLowerInvariant() is not ("cmd" or "powershell"))
                        {
                            status = 400;
                            responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_shell_type\"}");
                        }
                        else
                        {
                            var ownerHash = RemoteShellSession.HashOwnerKey(suppliedKey);
                            RemoveRemoteShellSessionsForOwner(ownerHash);
                            CleanupExpiredRemoteShellSessions();
                            if (_remoteShellSessions.Count >= 4)
                            {
                                status = 429;
                                responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"too_many_shell_sessions\",\"message\":\"Tutup terminal Android lain lalu coba lagi.\"}");
                            }
                            else
                            {
                                var shellSession = RemoteShellSession.Start(shellType, ownerHash);
                                _remoteShellSessions[shellSession.Id] = shellSession;
                                status = 200;
                                responseBytes = JsonSerializer.SerializeToUtf8Bytes(new
                                {
                                    sessionId = shellSession.Id,
                                    type = shellSession.Type,
                                    workingDirectory = shellSession.WorkingDirectory,
                                    elevated = false
                                });
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        status = 400;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_shell_request\"}");
                    }
                    catch (Exception)
                    {
                        status = 501;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"shell_start_failed\",\"message\":\"Terminal Windows gagal dimulai.\"}");
                    }
                }
                else if (secure && method == "GET" && path.StartsWith("/api/shell/read?", StringComparison.Ordinal))
                {
                    if (!TryGetRemoteShellSession(path, suppliedKey, out var shellSession))
                    {
                        status = 404;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"shell_session_not_found\",\"message\":\"Sesi terminal berakhir. Hubungkan ulang terminal.\"}");
                    }
                    else
                    {
                        var cursor = long.TryParse(QueryValue(path, "after"), out var parsedCursor) ? parsedCursor : 0;
                        var output = shellSession!.ReadAfter(cursor);
                        status = 200;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            sessionId = shellSession.Id,
                            output = output.Text,
                            nextCursor = output.NextCursor,
                            exited = output.Exited,
                            truncated = output.Truncated
                        });
                    }
                }
                else if (secure && method == "POST" && path.StartsWith("/api/shell/input?", StringComparison.Ordinal))
                {
                    if (!TryGetRemoteShellSession(path, suppliedKey, out var shellSession))
                    {
                        status = 404;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"shell_session_not_found\",\"message\":\"Sesi terminal berakhir. Hubungkan ulang terminal.\"}");
                    }
                    else
                    {
                        try
                        {
                            using var inputRequest = JsonDocument.Parse(requestBody);
                            var input = inputRequest.RootElement.TryGetProperty("text", out var textValue)
                                ? textValue.GetString() ?? "" : "";
                            if (input.Length == 0 || input.Length > 4096)
                            {
                                status = 400;
                                responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_shell_input\",\"message\":\"Input harus berisi 1–4096 karakter.\"}");
                            }
                            else
                            {
                                shellSession!.WriteInput(input);
                                status = 200;
                                responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                            }
                        }
                        catch (JsonException)
                        {
                            status = 400;
                            responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_shell_input\"}");
                        }
                        catch (InvalidOperationException ex)
                        {
                            status = 409;
                            responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { error = "shell_session_ended", message = ex.Message });
                        }
                    }
                }
                else if (secure && method == "POST" && path.StartsWith("/api/shell/stop?", StringComparison.Ordinal))
                {
                    if (!TryGetRemoteShellSession(path, suppliedKey, out var shellSession))
                    {
                        status = 404;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"shell_session_not_found\"}");
                    }
                    else
                    {
                        RemoveRemoteShellSession(shellSession!.Id);
                        status = 200;
                        responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    }
                }
                else if (method == "GET" && path == "/api/share/pending")
                {
                    lock (_shareGate)
                    {
                        status = 200;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            fileAvailable = _pendingShareFile is not null,
                            fileId = _pendingShareFile?.Id ?? "",
                            fileName = _pendingShareFile?.Name ?? "",
                            fileLength = _pendingShareFile?.Bytes.Length ?? 0,
                            clipboardAvailable = !string.IsNullOrEmpty(_pendingShareClipboard)
                        });
                    }
                }
                else if (method == "GET" && path.StartsWith("/api/share/file?", StringComparison.Ordinal))
                {
                    var id = QueryValue(path, "id");
                    lock (_shareGate)
                    {
                        if (_pendingShareFile is { } file && file.Id == id)
                        {
                            status = 200;
                            responseBytes = file.Bytes;
                            contentType = "application/octet-stream";
                        }
                        else { status = 404; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"shared_file_not_found\"}"); }
                    }
                }
                else if (method == "GET" && path == "/api/share/clipboard")
                {
                    lock (_shareGate)
                    {
                        if (!string.IsNullOrEmpty(_pendingShareClipboard))
                        {
                            status = 200;
                            responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { text = _pendingShareClipboard });
                        }
                        else { status = 404; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"shared_clipboard_empty\"}"); }
                    }
                }
                else if (method == "POST" && path.StartsWith("/api/share/ack?", StringComparison.Ordinal))
                {
                    var fileId = QueryValue(path, "fileId");
                    var clipboardAck = QueryValue(path, "clipboard").Equals("true", StringComparison.OrdinalIgnoreCase);
                    lock (_shareGate)
                    {
                        if (_pendingShareFile?.Id == fileId) _pendingShareFile = null;
                        if (clipboardAck) _pendingShareClipboard = "";
                    }
                    status = 200;
                    responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                }
                else if (method == "POST" && path.StartsWith("/api/share/file?", StringComparison.Ordinal))
                {
                    if (requestBody.Length == 0)
                    {
                        status = 400;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"empty_shared_file\"}");
                    }
                    else
                    {
                        var suppliedName = Path.GetFileName(QueryValue(path, "name"));
                        var safeName = string.IsNullOrWhiteSpace(suppliedName) ? "file.bin" : string.Concat(suppliedName.Where(character => !Path.GetInvalidFileNameChars().Contains(character)));
                        if (safeName.Length > 140) safeName = safeName[^140..];
                        if (string.IsNullOrWhiteSpace(safeName)) safeName = "file.bin";
                        Directory.CreateDirectory(_sharedFilesPath);
                        var savedPath = Path.Combine(_sharedFilesPath, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{safeName}");
                        await File.WriteAllBytesAsync(savedPath, requestBody, requestTimeout.Token);
                        status = 200;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, fileName = safeName, path = savedPath, length = requestBody.Length });
                        _ = Dispatcher.BeginInvoke(() => ShareStatusText.Text = $"Diterima dari Android: {safeName} ({FormatBytes((ulong)requestBody.Length)}). Tersimpan di folder berbagi LaptopStatus.");
                    }
                }
                else if (method == "POST" && path == "/api/share/clipboard")
                {
                    var sharedText = Encoding.UTF8.GetString(requestBody);
                    if (string.IsNullOrWhiteSpace(sharedText))
                    {
                        status = 400;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"empty_shared_clipboard\"}");
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() => System.Windows.Clipboard.SetText(sharedText));
                        status = 200;
                        responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                        _ = Dispatcher.BeginInvoke(() => ShareStatusText.Text = "Teks clipboard dari Android sudah disalin ke clipboard Windows.");
                    }
                }
                else if (method == "GET" && path == "/api/status")
                {
                    status = 200;
                    var windowsJokeActive = _jokeWindow?.IsVisible == true;
                    var androidJokeTriggerActive = DateTimeOffset.UtcNow < _androidJokeTriggerUntilUtc;
                    var data = new { deviceId = _deviceId, deviceName = _deviceName, manufacturer = _manufacturer, model = _model, macAddress = _macAddress,
                        online = true, cpuPercent = Math.Round(_cpu), ramPercent = Math.Round(_ram), batteryPercent = _battery, onAcPower = IsOnAcPower(),
                        cpuTemperatureC = _hardwareSnapshot.CpuTemperatureC, gpuUsagePercent = _hardwareSnapshot.GpuUsagePercent,
                        gpuTemperatureC = _hardwareSnapshot.GpuTemperatureC, fanRpm = _hardwareSnapshot.FanRpm,
                        cpuModel = string.IsNullOrWhiteSpace(_hardwareSnapshot.CpuModel) ? LoadProcessorName() : _hardwareSnapshot.CpuModel,
                        gpuModel = _hardwareSnapshot.GpuModel,
                        gpus = _hardwareSnapshot.Gpus.Select(gpu => new { model = gpu.Model, usagePercent = gpu.UsagePercent, temperatureC = gpu.TemperatureC }).ToArray(),
                        logicalProcessors = Environment.ProcessorCount,
                        totalPhysicalMemoryBytes = GetTotalPhysicalMemoryBytes(), osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                        osVersion = Environment.OSVersion.VersionString,
                        jokeActive = windowsJokeActive,
                        androidJokeTriggerActive,
                        androidJokeNotificationEnabled = _androidJokeNotificationEnabled,
                        androidJokeNotificationMessage = _jokeSettings.Message,
                        androidJokeBackgroundHex = _jokeSettings.BackgroundHex,
                        androidJokeIconGlyph = _jokeSettings.IconGlyph,
                        androidJokeIconBase64 = _androidJokeNotificationEnabled && androidJokeTriggerActive
                            ? GetAndroidJokeIconBase64(_jokeSettings) : null,
                        androidJokeAnimation = _jokeSettings.Animation,
                        screenControlActive = _screenControlActive, screenControlPending = _screenControlPending,
                        screenPasswordAccessEnabled = _screenPasswordAccessEnabled, screenPort = ScreenPort, updatedAt = DateTimeOffset.Now };
                    responseBytes = JsonSerializer.SerializeToUtf8Bytes(data);
                }
                else if (secure && method == "GET" && path == "/api/memory")
                {
                    status = 200;
                    responseBytes = JsonSerializer.SerializeToUtf8Bytes(GetMemorySnapshot());
                }
                else if (secure && method == "POST" && path.StartsWith("/api/memory/action?", StringComparison.Ordinal))
                {
                    try
                    {
                        var result = await PrivilegedActionRunner.RunRamMapActionAsync(QueryValue(path, "action"));
                        status = 200;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, message = result });
                        await Dispatcher.InvokeAsync(() => RecordWindowsHistory("Aksi memori: " + QueryValue(path, "action")));
                    }
                    catch (Exception ex)
                    {
                        status = ex is ArgumentException ? 400 : 501;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { error = "memory_action_failed", message = ex.Message });
                    }
                }
                else if (secure && method == "GET" && path == "/api/schedule")
                {
                    status = 200;
                    responseBytes = JsonSerializer.SerializeToUtf8Bytes(GetScheduledPowerSnapshot());
                }
                else if (secure && method == "GET" && path == "/api/routines")
                {
                    status = 200;
                    responseBytes = JsonSerializer.SerializeToUtf8Bytes(GetScheduledRoutineSnapshot());
                }
                else if (secure && method == "POST" && path.StartsWith("/api/routines?", StringComparison.Ordinal))
                {
                    if (!int.TryParse(QueryValue(path, "index"), out var routineIndex) || routineIndex is < 0 or > 2
                        || !bool.TryParse(QueryValue(path, "enabled"), out var enabled))
                    {
                        status = 400;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_routine\"}");
                    }
                    else
                    {
                        var updated = SetScheduledRoutine(routineIndex, enabled);
                        status = 200;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, index = updated.Index, enabled = updated.Enabled, routines = GetScheduledRoutineSnapshot() });
                        await Dispatcher.InvokeAsync(() => RecordWindowsHistory($"Rutinitas {updated.Name} {(enabled ? "diaktifkan" : "dinonaktifkan")} dari Android."));
                    }
                }
                else if (secure && method == "POST" && path.StartsWith("/api/schedule?", StringComparison.Ordinal))
                {
                    if (QueryValue(path, "cancel").Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        CancelScheduledPower();
                        status = 200;
                        responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, message = "Jadwal dibatalkan." });
                    }
                    else if (!long.TryParse(QueryValue(path, "atUtc"), out var scheduledAt) || scheduledAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        || QueryValue(path, "action") is not ("shutdown" or "restart" or "sleep"))
                    {
                        status = 400;
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_schedule\"}");
                    }
                    else
                    {
                        var schedule = new ScheduledPower(QueryValue(path, "action"), scheduledAt, QueryValue(path, "message").Trim());
                        try
                        {
                            var recoveryMode = SaveScheduledPower(schedule);
                            status = 200;
                            responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, action = schedule.Action, atUtc = schedule.AtUtc, message = schedule.Message, recoveryMode });
                            await Dispatcher.InvokeAsync(() =>
                            {
                                RecordWindowsHistory($"Jadwal {schedule.Action} dibuat.");
                                ShowScheduledPowerNotice(schedule);
                                if (recoveryMode == "login_recovery")
                                    ConnectionInfo.Text = "Jadwal tersimpan. Windows membatasi Task Scheduler, jadi Laptop Status akan memulihkannya saat akun ini masuk kembali.";
                                else if (recoveryMode == "active_session")
                                    ConnectionInfo.Text = "Jadwal tersimpan dan aktif selama Laptop Status berjalan. Pemulihan otomatis saat login tidak tersedia di akun Windows ini.";
                            });
                        }
                        catch (Exception ex)
                        {
                            status = 501;
                            responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { error = "schedule_persistence_failed", message = ex.Message });
                        }
                    }
                }
                else if (secure && method == "GET" && path == "/api/processes")
                {
                    status = 200;
                    var processes = GetProcessSnapshot().Select(item => new
                    {
                        pid = item.Pid,
                        name = item.Name,
                        workingSetBytes = item.WorkingSetBytes,
                        responding = item.Responding,
                        canTerminate = item.CanTerminate,
                        terminationNote = item.TerminationNote,
                        iconBase64 = item.IconBase64
                    });
                    responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { processes });
                }
                else if (secure && method == "POST" && path.StartsWith("/api/process/kill?", StringComparison.Ordinal))
                {
                        if (!int.TryParse(QueryValue(path, "pid"), out var pid) || pid <= 4)
                        {
                            status = 400;
                            responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_process_id\"}");
                        }
                        else
                        {
                            try
                            {
                                using var target = Process.GetProcessById(pid);
                                if (!ProcessKillHelper.CanTerminate(target, out var reason))
                                {
                                    status = 403;
                                    responseBytes = JsonSerializer.SerializeToUtf8Bytes(new { error = "protected_process", message = reason });
                                }
                                else
                                {
                                    processKillPid = pid;
                                    status = 202;
                                    responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true,\"status\":\"approval_pending\"}");
                                }
                            }
                            catch (ArgumentException)
                            {
                                status = 404;
                                responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"process_not_found\"}");
                            }
                        }
                }
                else if (secure && method == "GET" && path == "/api/screen")
                {
                    if (!_screenControlActive) { status = 403; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"screen_session_not_active\"}"); }
                    else
                    {
                        var capture = DesktopControl.CaptureControlScreen();
                        status = 200; responseBytes = capture.Image; contentType = "image/jpeg";
                        screenSource = capture.Source;
                    }
                }
                else if (secure && method == "POST" && path.StartsWith("/api/screen/input?", StringComparison.Ordinal))
                {
                    if (!_screenControlActive) { status = 403; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"screen_session_not_active\"}"); }
                    else if (!int.TryParse(QueryValue(path, "x"), out var x) || !int.TryParse(QueryValue(path, "y"), out var y))
                    { status = 400; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_coordinates\"}"); }
                    else
                    {
                        var action = QueryValue(path, "action");
                        if (action is not ("move" or "down" or "up" or "click")) { status = 400; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_action\"}"); }
                        else { DesktopControl.MoveAndClick(x, y, action); status = 200; responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}"); }
                    }
                }
                else if (secure && method == "POST" && path.StartsWith("/api/screen/type?", StringComparison.Ordinal))
                {
                    var typed = QueryValue(path, "text");
                    if (!_screenControlActive) { status = 403; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"screen_session_not_active\"}"); }
                    else if (typed.Length > 500) { status = 400; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"text_too_long\"}"); }
                    else { DesktopControl.TypeText(typed); status = 200; responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}"); }
                }
                else if (method == "POST" && path.StartsWith("/api/control?command=", StringComparison.Ordinal))
                {
                    control = QueryValue(path, "command");
                    controlMessage = QueryValue(path, "message").Trim();
                    var ordinaryCommand = control is "shutdown" or "restart" or "sleep" or "joke-on" or "joke-off"
                        or "admin-cmd" or "admin-powershell"
                        or "open-app-notepad" or "open-app-calculator" or "open-app-explorer"
                        or "open-app-task-manager" or "open-app-settings";
                    var screenCommand = secure && (control is "screen-request" or "screen-stop");
                    var passwordScreenCommand = secure && control == "screen-auth";
                    if (passwordScreenCommand)
                    {
                        if (VerifyScreenAccessPassword(screenPassword)) { status = 200; responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}"); }
                        else { status = 403; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_screen_password\"}"); control = null; }
                    }
                    else if (ordinaryCommand || screenCommand) { status = 200; responseBytes = Encoding.UTF8.GetBytes("{\"ok\":true}"); }
                    else { status = 400; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_command\"}"); control = null; }
                }
                else { status = 404; responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"not_found\"}"); }

                if (authenticatedClient && status == 200 && method == "GET" && path == "/api/status")
                    await Dispatcher.InvokeAsync(() => RegisterConnectionSound(success: true, rateKey));
                else if ((isPairingRequest && status == 403) ||
                         (secure && !authenticatedClient && !string.IsNullOrWhiteSpace(suppliedKey) && path == "/api/status"))
                    await Dispatcher.InvokeAsync(() => RegisterConnectionSound(success: false, rateKey));

                var statusText = status switch { 200 => "OK", 202 => "Accepted", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 409 => "Conflict", 429 => "Too Many Requests", 500 => "Internal Server Error", 501 => "Not Implemented", _ => "Not Found" };
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {statusText}\r\nContent-Type: {contentType}\r\nContent-Length: {responseBytes.Length}\r\nCache-Control: no-store\r\n{(screenSource is null ? "" : $"X-LaptopStatus-Screen-Source: {screenSource}\r\n")}{(retryAfterSeconds > 0 ? $"Retry-After: {retryAfterSeconds}\r\n" : "")}Connection: close\r\n\r\n");
                await stream.WriteAsync(header, token);
                await stream.WriteAsync(responseBytes, token);
                if (control is not null) _ = RunControlAsync(control, controlMessage);
                if (processKillPid.HasValue) _ = RunProcessTerminationAsync(processKillPid.Value);
            }
            catch (OperationCanceledException) { }
            catch
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes("{\"error\":\"server_error\"}");
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 500 Internal Server Error\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, token);
                    await stream.WriteAsync(bytes, token);
                }
                catch { }
            }
        }

        private static string QueryValue(string path, string key)
        {
            var queryStart = path.IndexOf('?');
            if (queryStart < 0) return "";
            foreach (var part in path[(queryStart + 1)..].Split('&'))
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == key) return Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            }
            return "";
        }

        private static int FindHttpHeaderEnd(byte[] bytes, int count)
        {
            for (var index = 0; index <= count - 4; index++)
                if (bytes[index] == 13 && bytes[index + 1] == 10 && bytes[index + 2] == 13 && bytes[index + 3] == 10)
                    return index;
            return -1;
        }

        private async Task RunControlAsync(string command, string customMessage = "")
        {
            try
            {
                await Dispatcher.InvokeAsync(() => RecordWindowsHistory($"Perintah Android: {command}"));
                if (command == "screen-request")
                {
                    await Dispatcher.InvokeAsync(() => RequestScreenSession(passwordAuthenticated: false));
                    return;
                }
                if (command == "screen-auth")
                {
                    await Dispatcher.InvokeAsync(() => RequestScreenSession(passwordAuthenticated: true));
                    return;
                }
                if (command == "screen-stop")
                {
                    await Dispatcher.InvokeAsync(StopScreenSession);
                    return;
                }
                if (command.StartsWith("open-app-", StringComparison.Ordinal))
                {
                    await Dispatcher.InvokeAsync(() => LaunchWindowsApp(command));
                    return;
                }
                if (command is "admin-cmd" or "admin-powershell")
                {
                    await Dispatcher.InvokeAsync(() => LaunchAdminShell(command));
                    return;
                }
                if (command == "joke-on")
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_jokeWindow is { IsVisible: true }) return;
                        _jokeWindow = new JokeWindow(_jokeSettings, MotionAnimationsEnabled);
                        _jokeWindow.Show();
                    });
                    return;
                }
                if (command == "joke-off")
                {
                    await Dispatcher.InvokeAsync(() => _jokeWindow?.Close());
                    return;
                }
                await Task.Delay(250);
                if (command is "shutdown" or "restart" or "sleep")
                    await Dispatcher.InvokeAsync(() => ShowPowerCountdown(command, customMessage));
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => ConnectionInfo.Text = $"Perintah gagal dijalankan: {ex.Message}");
            }
        }

        private void LaunchWindowsApp(string command)
        {
            var (executable, arguments, label) = command switch
            {
                "open-app-notepad" => ("notepad.exe", "", "Notepad"),
                "open-app-calculator" => ("calc.exe", "", "Kalkulator"),
                "open-app-explorer" => ("explorer.exe", "", "File Explorer"),
                "open-app-task-manager" => ("taskmgr.exe", "", "Task Manager"),
                "open-app-settings" => ("ms-settings:", "", "Pengaturan Windows"),
                _ => throw new ArgumentException("Pilihan aplikasi Windows tidak dikenal.")
            };

            Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true });
            ConnectionInfo.Text = $"Membuka {label} di Windows.";
            RecordWindowsHistory($"Aplikasi Windows dibuka dari Android: {label}.");
        }

        private void LaunchAdminShell(string command)
        {
            var isPowerShell = command == "admin-powershell";
            var label = isPowerShell ? "PowerShell Administrator" : "Command Prompt Administrator";
            var approved = MessageBox.Show(this,
                $"Android meminta membuka {label}. Jendela shell akan terlihat di Windows. Windows akan menampilkan UAC; setujui hanya jika kamu yang meminta tindakan ini.\n\nHak Administrator hanya berlaku selama jendela shell ini terbuka.",
                "Buka shell Administrator",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!approved)
            {
                ConnectionInfo.Text = $"Permintaan {label} dibatalkan di Windows.";
                RecordWindowsHistory($"Permintaan {label} dari Android dibatalkan.");
                return;
            }

            var executable = isPowerShell
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
                : Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                WindowStyle = ProcessWindowStyle.Normal,
                Arguments = isPowerShell ? "-NoLogo -NoExit" : "/k title Laptop Status - Administrator CMD"
            };
            try
            {
                Process.Start(startInfo);
                ConnectionInfo.Text = $"{label} dibuka. Selesaikan prompt UAC di Windows.";
                RecordWindowsHistory($"{label} dibuka dari Android setelah konfirmasi lokal.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                ConnectionInfo.Text = $"UAC membatalkan pembukaan {label}.";
                RecordWindowsHistory($"UAC membatalkan permintaan {label} dari Android.");
            }
        }

        private void RequestScreenSession(bool passwordAuthenticated)
        {
            if (passwordAuthenticated && !_screenPasswordAccessEnabled)
            {
                ConnectionInfo.Text = "Akses password sudah dimatikan. Aktifkan kembali dari Pengaturan Windows.";
                return;
            }
            if (_screenControlActive)
            {
                ConnectionInfo.Text = "Sesi kendali layar sudah aktif.";
                return;
            }
            _screenControlPending = true;
            ConnectionInfo.Text = passwordAuthenticated
                ? "Password khusus LaptopStatus diterima; membuka sesi dengan indikator lokal."
                : "Menunggu persetujuan lokal untuk kendali layar Android.";
            try
            {
                if (!passwordAuthenticated)
                {
                    var approval = new ScreenApprovalWindow();
                    if (approval.ShowDialog() != true)
                    {
                        ConnectionInfo.Text = "Permintaan kendali layar ditolak atau ditutup.";
                        return;
                    }
                }

                _screenControlExpires = DateTimeOffset.UtcNow.AddMinutes(10);
                _screenControlActive = true;
                _screenIndicator = new ScreenControlIndicatorWindow(StopScreenSession, passwordAuthenticated);
                _screenIndicator.Show();
                ConnectionInfo.Text = passwordAuthenticated
                    ? "Kendali layar dengan password aktif selama maksimal 10 menit. Hentikan dari indikator merah atau Android."
                    : "Kendali layar aktif selama maksimal 10 menit. Hentikan dari indikator merah atau Android.";
                RecordWindowsHistory(passwordAuthenticated
                    ? "Sesi kendali layar dimulai dengan password khusus (berakhir otomatis dalam 10 menit)."
                    : "Sesi kendali layar disetujui (berakhir otomatis dalam 10 menit).");
            }
            finally { _screenControlPending = false; }
        }

        private void StopScreenSession()
        {
            var wasActive = _screenControlActive || _screenControlPending;
            _screenControlActive = false;
            _screenControlPending = false;
            DesktopControl.ReleaseMouse();
            if (_screenIndicator is { IsVisible: true }) _screenIndicator.Close();
            _screenIndicator = null;
            if (wasActive && !_allowExit && IsLoaded) RecordWindowsHistory("Sesi kendali layar dihentikan.");
            if (!_allowExit && IsLoaded) ConnectionInfo.Text = "Sesi kendali layar berhenti.";
        }

        private static void StartPowerCommand(string arguments)
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true });
        }

        private static List<ScheduledRoutine> CreateDefaultScheduledRoutines() => new()
        {
            new(0, "Matikan malam", "shutdown", "22:00", "Setiap hari", false, ""),
            new(1, "Tidur siang", "sleep", "13:00", "Senin–Jumat", false, ""),
            new(2, "Mulai ulang", "restart", "03:00", "Setiap Minggu", false, "")
        };

        private object GetScheduledRoutineSnapshot()
        {
            lock (_scheduledRoutinesGate)
            {
                var now = DateTime.Now;
                return new
                {
                    routines = _scheduledRoutines.Select(routine => new
                    {
                        index = routine.Index, name = routine.Name, action = routine.Action, time = routine.Time,
                        days = routine.Days, enabled = routine.Enabled, nextAt = routine.Enabled ? GetNextRoutineTime(routine, now).ToUnixTimeMilliseconds() : 0
                    }).ToArray()
                };
            }
        }

        private static DateTimeOffset GetNextRoutineTime(ScheduledRoutine routine, DateTime now)
        {
            var parts = routine.Time.Split(':');
            var hour = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            var minute = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            for (var offset = 0; offset <= 7; offset++)
            {
                var date = now.Date.AddDays(offset);
                var matches = routine.Index switch
                {
                    0 => true,
                    1 => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                    2 => date.DayOfWeek == DayOfWeek.Sunday,
                    _ => false
                };
                var candidate = date.AddHours(hour).AddMinutes(minute);
                if (matches && candidate > now) return new DateTimeOffset(candidate);
            }
            return new DateTimeOffset(now.Date.AddDays(7).AddHours(hour).AddMinutes(minute));
        }

        private ScheduledRoutine SetScheduledRoutine(int index, bool enabled)
        {
            lock (_scheduledRoutinesGate)
            {
                var position = _scheduledRoutines.FindIndex(item => item.Index == index);
                if (position < 0) throw new ArgumentOutOfRangeException(nameof(index));
                var current = _scheduledRoutines[position];
                var updated = current with { Enabled = enabled };
                _scheduledRoutines[position] = updated;
                SaveScheduledRoutinesLocked();
                return updated;
            }
        }

        private void LoadScheduledRoutines()
        {
            try
            {
                if (!File.Exists(_scheduledRoutinesPath)) return;
                var saved = JsonSerializer.Deserialize<List<ScheduledRoutine>>(File.ReadAllText(_scheduledRoutinesPath));
                if (saved is null) return;
                lock (_scheduledRoutinesGate)
                {
                    var defaults = CreateDefaultScheduledRoutines();
                    _scheduledRoutines = defaults.Select(item => saved.FirstOrDefault(candidate => candidate.Index == item.Index) is { } candidate
                        ? item with { Enabled = candidate.Enabled, LastRunDate = candidate.LastRunDate ?? "" }
                        : item).ToList();
                }
            }
            catch { _scheduledRoutines = CreateDefaultScheduledRoutines(); }
        }

        private void SaveScheduledRoutinesLocked()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_scheduledRoutinesPath)!);
            File.WriteAllText(_scheduledRoutinesPath, JsonSerializer.Serialize(_scheduledRoutines));
        }

        private void CheckScheduledRoutines()
        {
            var now = DateTime.Now;
            ScheduledRoutine? due = null;
            lock (_scheduledRoutinesGate)
            {
                var today = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var position = _scheduledRoutines.FindIndex(routine =>
                {
                    if (!routine.Enabled || routine.LastRunDate == today) return false;
                    var time = routine.Time.Split(':');
                    if (time.Length != 2 || !int.TryParse(time[0], out var hour) || !int.TryParse(time[1], out var minute)
                        || now.Hour != hour || now.Minute != minute) return false;
                    return routine.Index switch
                    {
                        0 => true,
                        1 => now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                        2 => now.DayOfWeek == DayOfWeek.Sunday,
                        _ => false
                    };
                });
                if (position < 0) return;
                due = _scheduledRoutines[position] with { LastRunDate = today };
                _scheduledRoutines[position] = due;
                try { SaveScheduledRoutinesLocked(); } catch { }
            }
            if (due is null) return;
            RecordWindowsHistory($"Rutinitas {due.Name} dimulai.");
            ShowPowerCountdown(due.Action, due.Name);
        }

        private object GetScheduledPowerSnapshot()
        {
            lock (_scheduledPowerGate)
            {
                return _scheduledPower is null
                    ? new { action = "", atUtc = 0L, message = "" }
                    : new { action = _scheduledPower.Action, atUtc = _scheduledPower.AtUtc, message = _scheduledPower.Message };
            }
        }

        private string SaveScheduledPower(ScheduledPower schedule)
        {
            lock (_scheduledPowerGate)
            {
                var directory = Path.GetDirectoryName(_scheduledPowerPath)!;
                var temporaryPath = _scheduledPowerPath + ".tmp";
                Directory.CreateDirectory(directory);
                try
                {
                    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(schedule));
                    File.Move(temporaryPath, _scheduledPowerPath, overwrite: true);
                    _scheduledPower = schedule;
                }
                catch
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                    throw;
                }

                try
                {
                    EnsureScheduledPowerTask(schedule);
                    DeleteScheduledPowerResumeStartup();
                    return "task_scheduler";
                }
                catch
                {
                    try
                    {
                        EnsureScheduledPowerResumeStartup();
                        return "login_recovery";
                    }
                    catch
                    {
                        return "active_session";
                    }
                }
            }
        }

        private void LoadScheduledPower()
        {
            var validRecordLoaded = false;
            try
            {
                if (!File.Exists(_scheduledPowerPath))
                {
                    DeleteScheduledPowerTask();
                    DeleteScheduledPowerResumeStartup();
                    return;
                }
                var schedule = JsonSerializer.Deserialize<ScheduledPower>(File.ReadAllText(_scheduledPowerPath));
                if (schedule is null || schedule.AtUtc <= 0
                    || schedule.Action is not ("shutdown" or "restart" or "sleep")
                    || schedule.AtUtc > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
                {
                    File.Delete(_scheduledPowerPath);
                    DeleteScheduledPowerTask();
                    DeleteScheduledPowerResumeStartup();
                    return;
                }
                var scheduledAt = DateTimeOffset.FromUnixTimeMilliseconds(schedule.AtUtc);
                var estimatedBootTime = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
                if (estimatedBootTime > scheduledAt)
                {
                    CancelScheduledPower();
                    _expiredScheduledPowerOnStartup = schedule;
                    return;
                }
                lock (_scheduledPowerGate) _scheduledPower = schedule;
                validRecordLoaded = true;
                try
                {
                    EnsureScheduledPowerTask(schedule);
                    DeleteScheduledPowerResumeStartup();
                }
                catch
                {
                    EnsureScheduledPowerResumeStartup();
                    ConnectionInfo.Text = "Jadwal tersimpan. Windows membatasi Task Scheduler, jadi Laptop Status akan memulihkannya saat akun ini masuk kembali.";
                }
            }
            catch (Exception ex)
            {
                if (!validRecordLoaded)
                {
                    try { if (File.Exists(_scheduledPowerPath)) File.Delete(_scheduledPowerPath); } catch { }
                    DeleteScheduledPowerTask();
                    DeleteScheduledPowerResumeStartup();
                }
                else
                    ConnectionInfo.Text = "Jadwal tersimpan, tetapi pemulihan saat login gagal: " + ex.Message;
            }
        }

        private void CancelScheduledPower()
        {
            lock (_scheduledPowerGate)
            {
                _scheduledPower = null;
                try { if (File.Exists(_scheduledPowerPath)) File.Delete(_scheduledPowerPath); } catch { }
            }
            DeleteScheduledPowerTask();
            DeleteScheduledPowerResumeStartup();
        }

        private bool CheckScheduledPower()
        {
            ScheduledPower? due = null;
            lock (_scheduledPowerGate)
            {
                if (_scheduledPower is not null && _scheduledPower.AtUtc <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                {
                    due = _scheduledPower;
                    _scheduledPower = null;
                    try { if (File.Exists(_scheduledPowerPath)) File.Delete(_scheduledPowerPath); } catch { }
                }
            }
            if (due is null) return false;
            DeleteScheduledPowerTask();
            DeleteScheduledPowerResumeStartup();
            RecordWindowsHistory($"Jadwal {due.Action} dimulai.");
            ShowPowerCountdown(due.Action, due.Message);
            return true;
        }

        private static void EnsureScheduledPowerTask(ScheduledPower schedule)
        {
            if (ScheduledPowerTaskMatches(schedule)) return;

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                executable = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                throw new InvalidOperationException("Windows tidak dapat menemukan aplikasi untuk memulihkan jadwal saat laptop menyala.");

            var userSid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Windows tidak dapat menentukan akun untuk pemulihan jadwal.");
            var taskNamespace = (XNamespace)"http://schemas.microsoft.com/windows/2004/02/mit/task";
            var boundary = DateTimeOffset.FromUnixTimeMilliseconds(schedule.AtUtc).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            var task = new XDocument(
                new XElement(taskNamespace + "Task",
                    new XAttribute("version", "1.2"),
                    new XElement(taskNamespace + "RegistrationInfo",
                        new XElement(taskNamespace + "Description", "Memulihkan jadwal daya Laptop Status saat Windows login atau waktu jadwal tiba.")),
                    new XElement(taskNamespace + "Triggers",
                        new XElement(taskNamespace + "LogonTrigger",
                            new XElement(taskNamespace + "Enabled", "true")),
                        new XElement(taskNamespace + "TimeTrigger",
                            new XElement(taskNamespace + "StartBoundary", boundary),
                            new XElement(taskNamespace + "Enabled", "true"))),
                    new XElement(taskNamespace + "Principals",
                        new XElement(taskNamespace + "Principal",
                            new XAttribute("id", "LaptopStatusUser"),
                            new XElement(taskNamespace + "UserId", userSid),
                            new XElement(taskNamespace + "LogonType", "InteractiveToken"),
                            new XElement(taskNamespace + "RunLevel", "LeastPrivilege"))),
                    new XElement(taskNamespace + "Settings",
                        new XElement(taskNamespace + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(taskNamespace + "DisallowStartIfOnBatteries", "false"),
                        new XElement(taskNamespace + "StopIfGoingOnBatteries", "false"),
                        new XElement(taskNamespace + "AllowHardTerminate", "true"),
                        new XElement(taskNamespace + "StartWhenAvailable", "true"),
                        new XElement(taskNamespace + "RunOnlyIfNetworkAvailable", "false"),
                        new XElement(taskNamespace + "AllowStartOnDemand", "true"),
                        new XElement(taskNamespace + "Enabled", "true"),
                        new XElement(taskNamespace + "Hidden", "false"),
                        new XElement(taskNamespace + "RunOnlyIfIdle", "false"),
                        new XElement(taskNamespace + "WakeToRun", "false"),
                        new XElement(taskNamespace + "ExecutionTimeLimit", "PT0S"),
                        new XElement(taskNamespace + "Priority", "7")),
                    new XElement(taskNamespace + "Actions",
                        new XAttribute("Context", "LaptopStatusUser"),
                        new XElement(taskNamespace + "Exec",
                            new XElement(taskNamespace + "Command", Path.GetFullPath(executable)),
                            new XElement(taskNamespace + "Arguments", "--resume-scheduled-power"),
                            new XElement(taskNamespace + "WorkingDirectory", Path.GetDirectoryName(Path.GetFullPath(executable)) ?? "")))));

            var taskPath = Path.Combine(Path.GetTempPath(), "LaptopStatus-ScheduledPower-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var xmlSettings = new XmlWriterSettings
                {
                    Encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
                    Indent = true,
                    OmitXmlDeclaration = false
                };
                using (var writer = XmlWriter.Create(taskPath, xmlSettings)) task.Save(writer);
                var validatedTask = XDocument.Load(taskPath);
                if (validatedTask.Root?.Name != taskNamespace + "Task")
                    throw new InvalidDataException("Definisi pemulihan jadwal tidak valid.");
                RunScheduledPowerTask(new[] { "/Create", "/TN", ScheduledPowerTaskName, "/XML", taskPath, "/F" });
            }
            finally
            {
                if (File.Exists(taskPath)) File.Delete(taskPath);
            }
        }

        private static void DeleteScheduledPowerTask()
        {
            try { RunScheduledPowerTask(new[] { "/Delete", "/TN", ScheduledPowerTaskName, "/F" }, allowFailure: true); }
            catch { }
        }

        private static void EnsureScheduledPowerResumeStartup()
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                executable = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                throw new InvalidOperationException("Windows tidak dapat menemukan aplikasi untuk memulihkan jadwal saat login.");

            using var runKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                ?? throw new InvalidOperationException("Windows tidak mengizinkan pemulihan jadwal melalui akun ini.");
            runKey.SetValue("LaptopStatusScheduledPowerRecovery", $"\"{Path.GetFullPath(executable)}\" --resume-scheduled-power", Microsoft.Win32.RegistryValueKind.String);
        }

        private static void DeleteScheduledPowerResumeStartup()
        {
            try
            {
                using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                runKey?.DeleteValue("LaptopStatusScheduledPowerRecovery", throwOnMissingValue: false);
            }
            catch { }
        }

        private static bool ScheduledPowerTaskMatches(ScheduledPower schedule)
        {
            try
            {
                var xml = RunScheduledPowerTask(new[] { "/Query", "/TN", ScheduledPowerTaskName, "/XML" }, allowFailure: true);
                var root = XDocument.Parse(xml).Root;
                if (root is null) return false;
                var ns = root.Name.Namespace;
                var enabled = !string.Equals(root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase);
                var hasLogon = root.Element(ns + "Triggers")?.Elements(ns + "LogonTrigger")
                    .Any(trigger => !string.Equals(trigger.Element(ns + "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase)) == true;
                var timeTrigger = root.Element(ns + "Triggers")?.Elements(ns + "TimeTrigger")
                    .FirstOrDefault(trigger => !string.Equals(trigger.Element(ns + "Enabled")?.Value, "false", StringComparison.OrdinalIgnoreCase));
                if (!enabled || !hasLogon || timeTrigger is null) return false;
                var boundaryText = timeTrigger.Element(ns + "StartBoundary")?.Value;
                return DateTimeOffset.TryParse(boundaryText, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var boundary)
                    && Math.Abs(boundary.ToUnixTimeMilliseconds() - schedule.AtUtc) < 1000;
            }
            catch { return false; }
        }

        private static string RunScheduledPowerTask(IEnumerable<string> arguments, bool allowFailure = false)
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows Task Scheduler tidak dapat dijalankan.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("Windows Task Scheduler tidak merespons.");
            }
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 && !allowFailure)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            return stdout;
        }

        private void ShowScheduledPowerNotice(ScheduledPower schedule)
        {
            var actionName = schedule.Action switch
            {
                "shutdown" => "mematikan Windows",
                "restart" => "merestart Windows",
                "sleep" => "menidurkan Windows",
                _ => "mengubah daya Windows"
            };
            var localTime = "waktu yang dijadwalkan";
            try { localTime = DateTimeOffset.FromUnixTimeMilliseconds(schedule.AtUtc).ToLocalTime().ToString("dd MMM yyyy, HH:mm"); }
            catch (ArgumentOutOfRangeException) { }
            var notice = $"Jadwal untuk {actionName} aktif pada {localTime}.";
            if (!string.IsNullOrWhiteSpace(schedule.Message))
            {
                var customMessage = schedule.Message.Trim();
                if (customMessage.Length > 150) customMessage = customMessage[..150] + "…";
                notice += $" Pesan: {customMessage}";
            }
            var title = schedule.Action == "shutdown" ? "Shutdown terjadwal aktif" : "Jadwal daya Windows aktif";
            _trayIcon.ShowBalloonTip(6000, title, notice, WinForms.ToolTipIcon.Info);
            if (_powerScheduleNotificationWindow is { IsVisible: true })
                _powerScheduleNotificationWindow.Close();
            _powerScheduleNotificationWindow = new PowerScheduleNotificationWindow(title, notice);
            _powerScheduleNotificationWindow.Closed += (_, _) => _powerScheduleNotificationWindow = null;
            _powerScheduleNotificationWindow.Show();
            _powerScheduleNotificationWindow.StartDismissTimer();
        }

        private void ShowExpiredScheduledPowerNotice(ScheduledPower schedule)
        {
            var actionName = schedule.Action switch
            {
                "shutdown" => "Shutdown",
                "restart" => "Restart",
                "sleep" => "Sleep",
                _ => "Jadwal daya"
            };
            var localTime = DateTimeOffset.FromUnixTimeMilliseconds(schedule.AtUtc).ToLocalTime().ToString("dd MMM yyyy, HH:mm");
            _trayIcon.ShowBalloonTip(
                7000,
                "Jadwal terlewat",
                $"{actionName} pukul {localTime} tidak dijalankan karena laptop sedang mati. Jadwal telah dibatalkan.",
                WinForms.ToolTipIcon.Warning);
        }

        private void RegisterConnectionSound(bool success, string connectionKey)
        {
            if (!_connectionChimesEnabled) return;
            var key = (success ? "success:" : "failure:") + (string.IsNullOrWhiteSpace(connectionKey) ? "unknown" : connectionKey);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_connectionChimeLastPlayed.TryGetValue(key, out var lastPlayed) && now - lastPlayed < 20_000) return;
            _connectionChimeLastPlayed[key] = now;
            _trayIcon.ShowBalloonTip(
                3500,
                success ? "Android terhubung" : "Koneksi Android ditolak",
                success ? "Laptop Status menerima koneksi aman dari Android." : "Windows menerima permintaan koneksi, tetapi pairing atau tokennya ditolak.",
                success ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning);
            if (_connectionChimeLastPlayed.Count > 48)
            {
                foreach (var entry in _connectionChimeLastPlayed.Where(item => now - item.Value > 300_000).ToArray())
                    _connectionChimeLastPlayed.TryRemove(entry.Key, out _);
            }

            var fileName = success ? "connection-success.mp3" : "connection-failed.mp3";
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", fileName);
            if (!File.Exists(path)) return;
            var player = new MediaPlayer { Volume = 0.72 };
            void Cleanup()
            {
                try { player.Close(); } catch { }
                if (Dispatcher.CheckAccess()) _activeConnectionChimes.Remove(player);
                else Dispatcher.BeginInvoke(new Action(() => _activeConnectionChimes.Remove(player)));
            }
            player.MediaOpened += (_, _) => player.Play();
            player.MediaEnded += (_, _) => Cleanup();
            player.MediaFailed += (_, _) => Cleanup();
            _activeConnectionChimes.Add(player);
            player.Open(new Uri(path, UriKind.Absolute));
        }

        private void ShowPowerCountdown(string action, string customMessage)
        {
            var countdown = new PowerCountdownWindow(action, customMessage, VerifyScreenAccessPassword, _screenPasswordAccessEnabled, MotionAnimationsEnabled);
            if (countdown.ShowDialog() != true)
            {
                RecordWindowsHistory($"Perintah {action} dibatalkan di laptop.");
                return;
            }
            if (action == "sleep") SetSuspendState(false, false, false);
            else StartPowerCommand(action == "restart" ? "/r /t 0" : "/s /t 0");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowExit)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(1800, "Status Perangkat", "Layanan tetap berjalan di system tray. Pilih Keluar dari menu ikon tray untuk menutupnya.", WinForms.ToolTipIcon.Info);
                return;
            }
            StopScreenSession();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            foreach (var sessionId in _remoteShellSessions.Keys.ToArray()) RemoveRemoteShellSession(sessionId);
            _screenAccessPasswordForDisplay = null;
            SavedScreenAccessPasswordText.Clear();
            foreach (var animationTimer in _metricAnimationTimers.Values.ToArray()) animationTimer.Stop();
            _metricAnimationTimers.Clear();
            _processKillArmTimer?.Stop();
            _stop.Cancel();
            StopCloudflareTunnelRuntime();
            _listener.Stop(); _screenListener.Stop(); _timer.Stop(); _screenSessionTimer.Stop(); _cpuCounter.Dispose(); _hardwareTelemetry.Dispose(); _stop.Dispose();
            _screenCertificate.Dispose();
            foreach (var chime in _activeConnectionChimes.ToArray()) { try { chime.Close(); } catch { } }
            _activeConnectionChimes.Clear();
            _trayIcon.Visible = false; _trayIcon.Dispose();
            _applicationIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}
