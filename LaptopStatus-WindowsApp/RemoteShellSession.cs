using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LaptopStatus;

internal sealed class RemoteShellSession : IDisposable
{
    private const int MaximumBufferedCharacters = 96 * 1024;
    private readonly object _gate = new();
    private readonly StringBuilder _output = new();
    private readonly Process _process;
    private readonly Timer _idleTimer;
    private long _bufferStart;
    private long _bufferEnd;
    private long _lastActivityTicks = DateTime.UtcNow.Ticks;
    private int _disposed;

    private RemoteShellSession(string ownerKeyHash, string type, Process process, string workingDirectory)
    {
        OwnerKeyHash = ownerKeyHash;
        Type = type;
        WorkingDirectory = workingDirectory;
        Id = Guid.NewGuid().ToString("N");
        _process = process;
        _process.OutputDataReceived += (_, eventArgs) => AppendLine(eventArgs.Data);
        _process.ErrorDataReceived += (_, eventArgs) => AppendLine(eventArgs.Data);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _idleTimer = new Timer(_ =>
        {
            if (IsExpired) Stop();
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public string Id { get; }
    public string Type { get; }
    public string WorkingDirectory { get; }
    public string OwnerKeyHash { get; }
    public bool IsExited
    {
        get { try { return _process.HasExited; } catch { return true; } }
    }
    public bool IsExpired => DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastActivityTicks) > TimeSpan.FromMinutes(15).Ticks;

    public static string HashOwnerKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static RemoteShellSession Start(string type, string ownerKeyHash)
    {
        var normalized = type.Trim().ToLowerInvariant();
        if (normalized is not ("cmd" or "powershell"))
            throw new ArgumentException("Pilih CMD atau PowerShell.", nameof(type));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var workingDirectory = Directory.Exists(userProfile) ? userProfile : Environment.CurrentDirectory;
        var executable = normalized == "cmd"
            ? Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = workingDirectory
        };

        if (normalized == "cmd")
        {
            startInfo.ArgumentList.Add("/Q");
            startInfo.Environment["PROMPT"] = "$P$G";
        }
        else
        {
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NoExit");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); [Console]::InputEncoding = [System.Text.UTF8Encoding]::new()");
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            return new RemoteShellSession(ownerKeyHash, normalized, process, workingDirectory);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public void WriteInput(string input)
    {
        if (Volatile.Read(ref _disposed) != 0 || IsExited)
            throw new InvalidOperationException("Sesi terminal sudah berakhir.");
        if (input.Length > 4096)
            throw new ArgumentException("Input terminal terlalu panjang.", nameof(input));

        lock (_gate)
        {
            _process.StandardInput.Write(input);
            _process.StandardInput.Flush();
            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        }
    }

    public RemoteShellOutput ReadAfter(long cursor, int maximumCharacters = 24000)
    {
        lock (_gate)
        {
            Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
            var wasTruncated = cursor < _bufferStart;
            var effectiveStart = Math.Clamp(cursor, _bufferStart, _bufferEnd);
            var offset = (int)(effectiveStart - _bufferStart);
            var count = Math.Min(Math.Max(0, maximumCharacters), _output.Length - offset);
            var text = count > 0 ? _output.ToString(offset, count) : string.Empty;
            return new RemoteShellOutput(text, effectiveStart + count, IsExited, wasTruncated);
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _idleTimer.Dispose();
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process.Dispose();
    }

    public void Dispose() => Stop();

    private void AppendLine(string? line)
    {
        if (line is null) return;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var text = line + Environment.NewLine;
            _output.Append(text);
            _bufferEnd += text.Length;
            var excess = _output.Length - MaximumBufferedCharacters;
            if (excess > 0)
            {
                _output.Remove(0, excess);
                _bufferStart += excess;
            }
        }
    }
}

internal sealed record RemoteShellOutput(string Text, long NextCursor, bool Exited, bool Truncated);
