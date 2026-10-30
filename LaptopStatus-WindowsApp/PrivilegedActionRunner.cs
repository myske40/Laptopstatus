using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LaptopStatus
{
    internal static class PrivilegedActionRunner
    {
        private const string HelperSwitch = "--elevated-action";
        private sealed record HelperResult(bool Success, string Message);

        public static async Task<string> RunRamMapActionAsync(string action)
        {
            if (ProcessKillHelper.IsElevated())
                return await Task.Run(() => RamMapActions.Execute(action));

            var result = await InvokeElevatedAsync("rammap", action);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            return result.Message;
        }

        public static async Task TerminateProcessAsync(int processId)
        {
            try
            {
                TerminateProcess(processId);
                return;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5 && !ProcessKillHelper.IsElevated())
            {
                var result = await InvokeElevatedAsync("kill", processId.ToString());
                if (!result.Success) throw new InvalidOperationException(result.Message);
            }
        }

        public static bool TryHandleHelperArguments(string[] arguments, out int exitCode)
        {
            exitCode = 0;
            if (arguments.Length == 0 || !string.Equals(arguments[0], HelperSwitch, StringComparison.Ordinal)) return false;
            if (arguments.Length < 4)
            {
                exitCode = 2;
                return true;
            }
            if (!Guid.TryParseExact(arguments[3], "N", out var requestId))
            {
                exitCode = 2;
                return true;
            }

            HelperResult result;
            try
            {
                if (!ProcessKillHelper.IsElevated())
                    throw new InvalidOperationException("Windows tidak memberikan izin administrator untuk aksi ini.");

                result = arguments[1] switch
                {
                    "rammap" => new HelperResult(true, RamMapActions.Execute(arguments[2])),
                    "kill" when int.TryParse(arguments[2], out var pid) && pid > 4 => ExecuteKill(pid),
                    _ => throw new ArgumentException("Perintah administrator tidak dikenali.")
                };
            }
            catch (Exception ex)
            {
                result = new HelperResult(false, ex.Message);
                exitCode = 1;
            }

            try
            {
                var path = GetResultPath(requestId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(result));
            }
            catch
            {
                exitCode = 1;
            }
            return true;
        }

        private static HelperResult ExecuteKill(int processId)
        {
            TerminateProcess(processId);
            return new HelperResult(true, "Proses berhasil dihentikan.");
        }

        private static void TerminateProcess(int processId)
        {
            using var process = Process.GetProcessById(processId);
            if (!ProcessKillHelper.CanTerminate(process, out var reason))
                throw new InvalidOperationException($"Proses tidak dihentikan: {reason}");
            var processName = process.ProcessName;
            process.Kill();
            process.WaitForExit(5000);
            if (!process.HasExited)
                throw new InvalidOperationException($"Windows belum menghentikan proses {processName} ({processId}).");
        }

        private static async Task<HelperResult> InvokeElevatedAsync(string action, string value)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                throw new InvalidOperationException("Lokasi aplikasi tidak dapat ditemukan.");

            var requestId = Guid.NewGuid();
            var resultPath = GetResultPath(requestId);
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add(HelperSwitch);
            startInfo.ArgumentList.Add(action);
            startInfo.ArgumentList.Add(value);
            startInfo.ArgumentList.Add(requestId.ToString("N"));

            try
            {
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Windows tidak dapat membuka dialog izin untuk aksi ini.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                if (!File.Exists(resultPath))
                    throw new InvalidOperationException("Windows tidak mengembalikan hasil aksi administrator.");
                var result = JsonSerializer.Deserialize<HelperResult>(await File.ReadAllTextAsync(resultPath));
                return result ?? new HelperResult(false, "Hasil aksi administrator tidak dapat dibaca.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Permintaan izin administrator dibatalkan; aksi tidak dijalankan.", ex);
            }
            finally
            {
                try { if (File.Exists(resultPath)) File.Delete(resultPath); }
                catch { }
            }
        }

        private static string GetResultPath(Guid requestId)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "AdminActions");
            return Path.Combine(directory, requestId.ToString("N") + ".json");
        }
    }
}
