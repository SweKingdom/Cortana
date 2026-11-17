using System.IO;
using System.Text.RegularExpressions;

namespace Companion.Core;

/// <summary>
/// Event log: names, timings and error kinds only. Never message text and never headers. Anything that looks
/// like a key is replaced by *** (architecture, Säkerhet).
/// </summary>
internal static class Log
{
    private static readonly Regex KeyLike = new(@"(sk-[A-Za-z0-9_\-]{10,}|[A-Za-z0-9_\-]{32,})", RegexOptions.Compiled);
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    /// <summary>Tests switch the log off so they never write into the real logs folder.</summary>
    public static bool Enabled { get; set; } = true;

    public static string Redact(string s) => KeyLike.Replace(s, "***");

    public static void Event(string name, params (string key, object? value)[] fields)
    {
        if (!Enabled) return;
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {name}" +
                string.Concat(fields.Select(f => $" {f.key}={Redact(Convert.ToString(f.value, System.Globalization.CultureInfo.InvariantCulture) ?? "")}"));
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var path = Path.Combine(AppPaths.Logs, "companion.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Move(path, Path.Combine(AppPaths.Logs, "companion.old.log"), overwrite: true);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (IOException) { /* logging must never break the app */ }
        catch (UnauthorizedAccessException) { }
    }
}
