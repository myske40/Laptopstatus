using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace LaptopStatus.DebugApp;

public partial class MainWindow : Window
{
    private sealed record ApiPreset(string Name, string Method, int Port, string Route, string Body = "");

    private readonly IReadOnlyList<ApiPreset> _presets =
    [
        new("Status dan telemetry", "GET", 8765, "/api/status"),
        new("Memori", "GET", 8765, "/api/memory"),
        new("RAMMap · working sets", "POST", 8765, "/api/memory/action?action=working-sets"),
        new("RAMMap · system working set", "POST", 8765, "/api/memory/action?action=system-working-set"),
        new("RAMMap · modified page list", "POST", 8765, "/api/memory/action?action=modified-page-list"),
        new("RAMMap · standby list", "POST", 8765, "/api/memory/action?action=standby-list"),
        new("RAMMap · standby priority 0", "POST", 8765, "/api/memory/action?action=priority0-standby-list"),
        new("Proses", "GET", 8765, "/api/processes"),
        new("Jadwal daya", "GET", 8765, "/api/schedule"),
        new("Rutinitas daya", "GET", 8765, "/api/routines"),
        new("File masuk", "GET", 8765, "/api/share/pending"),
        new("Clipboard masuk", "GET", 8765, "/api/share/clipboard"),
        new("Tampilan layar", "GET", 8767, "/api/screen"),
        new("Kontrol layar · minta izin", "POST", 8767, "/api/control?command=screen-request"),
        new("Kontrol layar · hentikan", "POST", 8767, "/api/control?command=screen-stop"),
        new("Joke · mulai", "POST", 8765, "/api/control?command=joke-on"),
        new("Joke · berhenti", "POST", 8765, "/api/control?command=joke-off"),
        new("Power · shutdown", "POST", 8765, "/api/control?command=shutdown"),
        new("Power · restart", "POST", 8765, "/api/control?command=restart"),
        new("Power · sleep", "POST", 8765, "/api/control?command=sleep"),
        new("Aplikasi Windows · Notepad", "POST", 8765, "/api/control?command=open-app-notepad"),
        new("Aplikasi Windows · kalkulator", "POST", 8765, "/api/control?command=open-app-calculator"),
        new("Paksa tutup LaptopStatus", "POST", 8765, "/api/app/force-exit"),
        new("Mulai shell PowerShell", "POST", 8765, "/api/shell/start", "{\"type\":\"powershell\",\"screenPassword\":\"\"}")
    ];

    public MainWindow()
    {
        InitializeComponent();
        PresetList.ItemsSource = _presets;
        PresetList.SelectedIndex = 0;
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is not ApiPreset preset) return;
        MethodInput.SelectedIndex = preset.Method == "POST" ? 1 : 0;
        PortInput.Text = preset.Port.ToString();
        RouteInput.Text = preset.Route;
        BodyInput.Text = preset.Body;
        RequestInfoText.Text = preset.Name;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            var response = await SendRequestAsync("GET", 8765, "/api/status", "");
            ConnectionStatusText.Text = response.StatusCode is >= 200 and < 300
                ? $"Terhubung · HTTP {response.StatusCode}"
                : $"LaptopStatus menjawab · HTTP {response.StatusCode}";
            ConnectionStatusText.Foreground = response.StatusCode is >= 200 and < 300
                ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Orange;
            ResponseOutput.Text = response.Body;
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "Koneksi gagal";
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            ResponseOutput.Text = ex.Message;
        }
        finally { ConnectButton.IsEnabled = true; }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var method = (MethodInput.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
        var route = RouteInput.Text.Trim();
        if (!route.StartsWith("/api/", StringComparison.Ordinal) || route.Any(char.IsControl) || route.Contains(' '))
        {
            MessageBox.Show("Masukkan path endpoint yang diawali /api/.", "Endpoint tidak valid", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!int.TryParse(PortInput.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show("Port harus berupa angka antara 1 dan 65535.", "Port tidak valid", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (method == "POST")
        {
            var warning = route.Contains("shutdown", StringComparison.OrdinalIgnoreCase)
                || route.Contains("restart", StringComparison.OrdinalIgnoreCase)
                || route.Contains("sleep", StringComparison.OrdinalIgnoreCase)
                    ? "Permintaan ini bisa mematikan, memulai ulang, atau menidurkan Windows."
                    : route.Contains("force-exit", StringComparison.OrdinalIgnoreCase)
                        ? "Permintaan ini akan menutup LaptopStatus setelah respons dikirim."
                        : "Permintaan ini dapat mengubah keadaan LaptopStatus atau Windows.";
            if (MessageBox.Show($"{warning}\n\nKirim {method} {route}?", "Konfirmasi aksi debug", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
        }

        SendButton.IsEnabled = false;
        RequestInfoText.Text = $"Mengirim {method} {route}…";
        try
        {
            var response = await SendRequestAsync(method, port, route, BodyInput.Text);
            RequestInfoText.Text = $"HTTP {response.StatusCode} · {response.ElapsedMilliseconds} ms";
            ResponseOutput.Text = response.Body;
            ConnectionStatusText.Text = $"Respons diterima · HTTP {response.StatusCode}";
        }
        catch (Exception ex)
        {
            RequestInfoText.Text = "Permintaan gagal";
            ResponseOutput.Text = ex.ToString();
            ConnectionStatusText.Text = "Koneksi gagal";
        }
        finally { SendButton.IsEnabled = true; }
    }

    private async Task<(int StatusCode, string Body, long ElapsedMilliseconds)> SendRequestAsync(string method, int port, string route, string body)
    {
        var host = HostInput.Text.Trim();
        if (host.Length == 0 || host.Contains("/") || host.Contains(' ') || host.Contains('@'))
            throw new InvalidOperationException("Masukkan alamat IP atau nama host tanpa skema URL.");
        if (host.Contains(':') && !host.StartsWith('[')) host = $"[{host}]";
        var key = PairingKeyInput.Password.Trim();
        if (key.Length == 0) throw new InvalidOperationException("Masukkan kunci pairing LaptopStatus.");
        var expectedFingerprint = new string(FingerprintInput.Text.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (expectedFingerprint.Length != 64)
            throw new InvalidOperationException("Masukkan fingerprint TLS SHA-256 64 karakter dari LaptopStatus.");

        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            using var cert = new X509Certificate2(certificate);
            var actualFingerprint = Convert.ToHexString(SHA256.HashData(cert.RawData));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualFingerprint), Encoding.ASCII.GetBytes(expectedFingerprint));
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(new HttpMethod(method), $"https://{host}:{port}{route}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        if (method == "POST") request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await client.SendAsync(request);
        stopwatch.Stop();
        var responseBody = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, responseBody, stopwatch.ElapsedMilliseconds);
    }
}
