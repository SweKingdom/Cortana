using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Companion.Core;

namespace Companion.Brain;

/// <summary>
/// One `claude` process (stream-json in and out). Flags are those of D-055 / the architecture doc. It runs in the
/// app-owned brain folder, never in the repo (D-048).
/// </summary>
internal sealed class ClaudeProcess
{
    private readonly Process _p;
    private readonly StringBuilder _err = new();
    private int _killed;

    private ClaudeProcess(Process p) => _p = p;

    public bool HasExited { get { try { return _p.HasExited; } catch (InvalidOperationException) { return true; } } }
    public string Stderr { get { lock (_err) return _err.ToString(); } }

    /// <summary>Starts claude. <paramref name="effort"/> adds <c>--effort</c> when set; null keeps the default effort.</summary>
    public static ClaudeProcess Start(string model, string systemPromptFile, string? effort = null)
    {
        var exe = FindClaude() ?? throw new BrainException(FailKind.NotInstalled, "claude.exe not found on PATH");
        Directory.CreateDirectory(AppPaths.Brain);
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = AppPaths.Brain,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in new[]
        {
            "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--include-partial-messages", "--tools", "", "--strict-mcp-config", "--no-session-persistence",
            "--setting-sources", "project", "--safe-mode", "--disable-slash-commands",
            "--model", model, "--system-prompt-file", systemPromptFile,
        }) psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(effort)) { psi.ArgumentList.Add("--effort"); psi.ArgumentList.Add(effort); }

        Process p;
        try { p = Process.Start(psi)!; }
        catch (Win32Exception) { throw new BrainException(FailKind.NotInstalled, "claude could not be started"); }
        var cp = new ClaudeProcess(p);
        _ = Task.Run(async () =>
        {
            try { string? l; while ((l = await p.StandardError.ReadLineAsync()) != null) lock (cp._err) { if (cp._err.Length < 8000) cp._err.AppendLine(l); } }
            catch (IOException) { }
            catch (InvalidOperationException) { }
        });
        return cp;
    }

    private static string? _claudePath;
    private static string? FindClaude()
    {
        if (_claudePath != null && File.Exists(_claudePath)) return _claudePath;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var f = Path.Combine(dir.Trim(), "claude.exe");
                if (File.Exists(f)) return _claudePath = f;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public void WriteUser(string text)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = text } });
            _p.StandardInput.Write(json + "\n");
            _p.StandardInput.Flush();
        }
        catch (IOException) { throw new BrainException(FailKind.Offline, "claude process is gone"); }
        catch (InvalidOperationException) { throw new BrainException(FailKind.Offline, "claude process is gone"); }
    }

    public Task<string?> ReadLineAsync(CancellationToken ct) => _p.StandardOutput.ReadLineAsync(ct).AsTask();

    /// <summary>Interrupt = kill the process tree (D-055: no interrupt command needed).</summary>
    public void Kill()
    {
        if (Interlocked.Exchange(ref _killed, 1) == 1) return;
        try { if (!_p.HasExited) _p.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    /// <summary>Normal end: close stdin so claude exits by itself; kill it if it hangs. Not awaited by the caller.</summary>
    public async Task ShutdownAsync()
    {
        try { _p.StandardInput.Close(); } catch (IOException) { }
        try { await _p.WaitForExitAsync(new CancellationTokenSource(5000).Token); }
        catch (OperationCanceledException) { Kill(); }
        catch (InvalidOperationException) { }
    }
}
