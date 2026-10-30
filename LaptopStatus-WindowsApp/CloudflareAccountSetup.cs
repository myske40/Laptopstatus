using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LaptopStatus
{
    internal sealed record CloudflareAccountTunnel(string TunnelId, string CredentialsPath, string TunnelName, bool CreatedNew);

    /// <summary>
    /// Menyiapkan tunnel Cloudflare atas nama pemilik akun tanpa salin-tempel token:
    /// mendeteksi cloudflared yang sudah ada, mendeteksi login yang sudah ada (cert.pem),
    /// login lewat browser bila perlu, membuat tunnel khusus laptop ini, mengarahkan DNS,
    /// lalu menulis config.yml. Semua lewat perintah resmi cloudflared.
    /// </summary>
    internal sealed class CloudflareAccountSetup
    {
        private static readonly Regex HostnamePattern = new(
            @"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z0-9-]{2,63}$", RegexOptions.Compiled);
        private static readonly Regex LoginUrlPattern = new(@"https://dash\.cloudflare\.com/argotunnel\?\S+", RegexOptions.Compiled);
        private static readonly Regex AddedCnamePattern = new(@"Added CNAME\s+(\S+)\s+which", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly string _cloudflaredPath;
        private readonly string _workDirectory;
        private readonly string _userProfile;

        public CloudflareAccountSetup(string cloudflaredPath, string workDirectory, string? userProfileDirectory = null)
        {
            _cloudflaredPath = cloudflaredPath;
            _workDirectory = workDirectory;
            _userProfile = userProfileDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        // ---------- Deteksi ----------

        /// <summary>Cari cloudflared yang sudah terpasang (PATH atau lokasi instalasi umum).</summary>
        public static string? FindInstalledCloudflared(long minBytes = 1_000_000)
        {
            var candidates = new List<string>();
            var names = new[] { "cloudflared.exe", "cloudflared" };
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                foreach (var name in names)
                    candidates.Add(Path.Combine(directory.Trim('"'), name));

            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            })
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                candidates.Add(Path.Combine(root, "cloudflared", "cloudflared.exe"));
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate) && new FileInfo(candidate).Length >= minBytes) return candidate;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Cari cert.pem hasil "cloudflared tunnel login" (tanda bahwa pemilik sudah login).</summary>
        public string? FindOriginCert()
        {
            foreach (var directory in new[] { ".cloudflared", ".cloudflare-warp", "cloudflare-warp" })
            {
                var path = Path.Combine(_userProfile, directory, "cert.pem");
                try { if (File.Exists(path) && new FileInfo(path).Length > 0) return path; }
                catch { }
            }
            return null;
        }

        // ---------- Alur utama ----------

        public async Task<CloudflareAccountTunnel> SetupAsync(
            string hostname,
            string deviceKey,
            Action<string> status,
            Action<string, bool> onLoginUrl,
            CancellationToken ct,
            bool forceLogin = false)
        {
            if (!HostnamePattern.IsMatch(hostname))
                throw new ArgumentException("Hostname tidak valid.", nameof(hostname));
            Directory.CreateDirectory(_workDirectory);

            string? transientProfileDirectory = null;
            try
            {
                var certPath = forceLogin ? null : FindOriginCert();
                if (certPath is null)
                {
                    // cloudflared tunnel login selalu memakai profil pengguna aktif. Arahkan proses anak
                    // ke profil sementara agar login baru tidak menimpa atau memakai cert.pem lama.
                    transientProfileDirectory = Path.Combine(_workDirectory, "account-login-" + Guid.NewGuid().ToString("N"));
                    var transientCertPath = Path.Combine(transientProfileDirectory, ".cloudflared", "cert.pem");
                    TryDelete(transientCertPath);
                    status(forceLogin
                        ? "Login ulang Cloudflare: pilih akun dan domain yang akan dipakai laptop ini..."
                        : "Login Cloudflare: selesaikan login di browser, lalu pilih domain yang akan dipakai laptop ini...");
                    certPath = await LoginAsync(onLoginUrl, TimeSpan.FromMinutes(5), ct, transientProfileDirectory).ConfigureAwait(false);
                }
                else
                {
                    status("Akun Cloudflare terdeteksi di komputer ini. Melanjutkan tanpa login ulang...");
                }

                var baseName = BuildTunnelName(deviceKey);
                if (forceLogin)
                    baseName += "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
                status("Membuat tunnel khusus laptop ini...");
                var tunnel = await CreateTunnelAsync(certPath, baseName, ct).ConfigureAwait(false);
                try
                {
                    status($"Mengarahkan {hostname} ke tunnel...");
                    await RouteDnsAsync(certPath, tunnel.TunnelId, hostname, ct).ConfigureAwait(false);
                }
                catch
                {
                    if (tunnel.CreatedNew)
                        await TryDeleteTunnelAsync(certPath, tunnel, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                return tunnel;
            }
            finally
            {
                if (transientProfileDirectory is not null)
                {
                    TryDelete(Path.Combine(transientProfileDirectory, ".cloudflared", "cert.pem"));
                    TryDeleteEmptyDirectory(Path.Combine(transientProfileDirectory, ".cloudflared"));
                    TryDeleteEmptyDirectory(transientProfileDirectory);
                }
            }
        }
        public async Task<string> LoginAsync(Action<string, bool> onLoginUrl, TimeSpan timeout, CancellationToken ct, string? isolatedProfileDirectory = null)
        {
            var browserOpenedByCloudflared = false;
            var notified = false;
            IDictionary<string, string>? environment = null;
            string? isolatedCertPath = null;
            if (isolatedProfileDirectory is not null)
            {
                Directory.CreateDirectory(isolatedProfileDirectory);
                isolatedCertPath = Path.Combine(isolatedProfileDirectory, ".cloudflared", "cert.pem");
                environment = new Dictionary<string, string>
                {
                    ["USERPROFILE"] = isolatedProfileDirectory,
                    ["HOME"] = isolatedProfileDirectory,
                };
            }
            var result = await RunAsync(new[] { "tunnel", "login" }, environment, timeout, line =>
            {
                if (line.Contains("browser window should have opened", StringComparison.OrdinalIgnoreCase))
                    browserOpenedByCloudflared = true;
                if (!notified && TryGetLoginUrl(line, out var url))
                {
                    notified = true;
                    onLoginUrl(url, browserOpenedByCloudflared);
                }
            }, ct).ConfigureAwait(false);

            if (isolatedCertPath is not null)
            {
                if (File.Exists(isolatedCertPath)) return isolatedCertPath;
                throw new InvalidOperationException("Login Cloudflare belum selesai atau dibatalkan. " + Tail(result.Output));
            }
            var cert = FindOriginCert();
            if (cert is null)
                throw new InvalidOperationException("Login Cloudflare belum selesai atau dibatalkan. " + Tail(result.Output));
            return cert;
        }
        public async Task<CloudflareAccountTunnel> CreateTunnelAsync(string certPath, string baseName, CancellationToken ct)
        {
            Directory.CreateDirectory(_workDirectory);
            var env = new Dictionary<string, string> { ["TUNNEL_ORIGIN_CERT"] = certPath };
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var name = attempt == 0 ? baseName : $"{baseName}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()}";
                var credentialsPath = Path.Combine(_workDirectory, name + ".json");
                if (File.Exists(credentialsPath))
                {
                    try
                    {
                        var existingId = ReadTunnelId(credentialsPath);
                        return new CloudflareAccountTunnel(existingId, credentialsPath, name, CreatedNew: false);
                    }
                    catch
                    {
                        // Jangan hapus kredensial yang ada; coba nama tunnel terpisah.
                        continue;
                    }
                }

                var result = await RunAsync(new[] { "tunnel", "create", "--credentials-file", credentialsPath, name },
                    env, TimeSpan.FromSeconds(60), null, ct).ConfigureAwait(false);

                if (result.ExitCode == 0 && File.Exists(credentialsPath))
                {
                    var tunnelId = ReadTunnelId(credentialsPath);
                    return new CloudflareAccountTunnel(tunnelId, credentialsPath, name, CreatedNew: true);
                }
                if (result.Output.Contains("already exists", StringComparison.OrdinalIgnoreCase)) continue;
                throw new InvalidOperationException("Gagal membuat tunnel: " + Tail(result.Output));
            }
            throw new InvalidOperationException("Gagal membuat tunnel: nama tunnel bentrok berulang kali.");
        }
        public async Task RouteDnsAsync(string certPath, string tunnelId, string hostname, CancellationToken ct)
        {
            var env = new Dictionary<string, string> { ["TUNNEL_ORIGIN_CERT"] = certPath };
            var result = await RunAsync(new[] { "tunnel", "route", "dns", tunnelId, hostname },
                env, TimeSpan.FromSeconds(60), null, ct).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                if (result.Output.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Sudah ada record DNS untuk {hostname}. Hapus record itu di dashboard Cloudflare (menu DNS), atau pilih hostname lain, lalu coba lagi.");
                throw new InvalidOperationException("Gagal mengarahkan DNS: " + Tail(result.Output));
            }

            // Jika hostname tidak berada di domain yang dipilih saat login, cloudflared menempelkan nama domain
            // pilihan itu di belakangnya (mis. laptop.a.com.b.com). Deteksi supaya QR tidak menunjuk ke alamat yang salah.
            var match = AddedCnamePattern.Match(result.Output);
            if (match.Success && !match.Groups[1].Value.TrimEnd('.').Equals(hostname, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Cloudflare membuat record '{match.Groups[1].Value}', bukan '{hostname}'. " +
                    "Hostname harus berada di domain yang kamu pilih saat login. Hapus record yang salah itu di dashboard DNS.");
        }

        /// <summary>Tulis config.yml yang hanya meneruskan hostname ini ke origin lokal; permintaan lain dijawab 404.</summary>
        public string WriteConfig(string tunnelId, string credentialsPath, string hostname, int originPort)
        {
            if (!Guid.TryParse(tunnelId, out _)) throw new ArgumentException("ID tunnel tidak valid.", nameof(tunnelId));
            if (!HostnamePattern.IsMatch(hostname)) throw new ArgumentException("Hostname tidak valid.", nameof(hostname));
            Directory.CreateDirectory(_workDirectory);

            var yaml = new StringBuilder()
                .Append("tunnel: ").Append(tunnelId).Append('\n')
                .Append("credentials-file: '").Append(credentialsPath.Replace("'", "''")).Append("'\n")
                .Append("ingress:\n")
                .Append("  - hostname: ").Append(hostname).Append('\n')
                .Append("    service: http://127.0.0.1:").Append(originPort).Append('\n')
                .Append("  - service: http_status:404\n")
                .ToString();

            var configPath = Path.Combine(_workDirectory, "config.yml");
            var temporary = configPath + ".tmp";
            File.WriteAllText(temporary, yaml, new UTF8Encoding(false));
            File.Move(temporary, configPath, overwrite: true);
            return configPath;
        }

        // ---------- Helper ----------

        public static bool TryGetLoginUrl(string line, out string url)
        {
            url = "";
            var match = LoginUrlPattern.Match(line);
            if (!match.Success) return false;
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("dash.cloudflare.com", StringComparison.OrdinalIgnoreCase))
                return false;
            url = uri.AbsoluteUri;
            return true;
        }

        public static string BuildTunnelName(string deviceKey)
        {
            var cleaned = new string((deviceKey ?? "").ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c)).Take(12).ToArray());
            if (cleaned.Length == 0) cleaned = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            return "laptopstatus-" + cleaned;
        }

        private static string ReadTunnelId(string credentialsPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(credentialsPath));
            if (document.RootElement.TryGetProperty("TunnelID", out var id) && id.ValueKind == JsonValueKind.String &&
                Guid.TryParse(id.GetString(), out var guid))
                return guid.ToString();
            throw new InvalidDataException("File kredensial tunnel tidak berisi TunnelID yang valid.");
        }

        private async Task TryDeleteTunnelAsync(string certPath, CloudflareAccountTunnel tunnel, CancellationToken ct)
        {
            try
            {
                var env = new Dictionary<string, string> { ["TUNNEL_ORIGIN_CERT"] = certPath };
                await RunAsync(new[] { "tunnel", "delete", tunnel.TunnelId }, env, TimeSpan.FromSeconds(30), null, ct).ConfigureAwait(false);
            }
            catch { }
            TryDelete(tunnel.CredentialsPath);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
            catch { }
        }

        private static string Tail(string output)
        {
            var text = Regex.Replace(output, @"https://dash\.cloudflare\.com/argotunnel\?\S+", "[tautan login Cloudflare]").Trim();
            return text.Length <= 400 ? text : "..." + text[^400..];
        }

        private async Task<(int ExitCode, string Output)> RunAsync(
            IEnumerable<string> arguments,
            IDictionary<string, string>? environment,
            TimeSpan timeout,
            Action<string>? onLine,
            CancellationToken ct)
        {
            Directory.CreateDirectory(_workDirectory);
            var startInfo = new ProcessStartInfo(_cloudflaredPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _workDirectory
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            if (environment is not null)
                foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;

            using var process = new Process { StartInfo = startInfo };
            var output = new StringBuilder();
            var gate = new object();
            void Handle(object sender, DataReceivedEventArgs e)
            {
                if (e.Data is null) return;
                lock (gate) output.AppendLine(e.Data);
                try { onLine?.Invoke(e.Data); } catch { }
            }
            process.OutputDataReceived += Handle;
            process.ErrorDataReceived += Handle;

            if (!process.Start()) throw new InvalidOperationException("cloudflared gagal dijalankan.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException("cloudflared tidak selesai dalam batas waktu.");
            }
            process.WaitForExit(); // pastikan seluruh output sudah terbaca
            lock (gate) return (process.ExitCode, output.ToString());
        }
    }
}
