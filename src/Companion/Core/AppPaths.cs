using System.IO;

namespace Companion.Core;

/// <summary>Folders under %APPDATA%\Companion\ (architecture, "Projektstruktur").</summary>
internal static class AppPaths
{
    public static string Root => Settings.Dir;
    /// <summary>Working directory of the brain's claude process: empty and app-owned, never the repo (D-048).</summary>
    public static string Brain => Path.Combine(Root, "brain");
    public static string MemoryNormal => Path.Combine(Root, "memory", "normal");
    public static string Logs => Path.Combine(Root, "logs");
    public static string QuotaFile => Path.Combine(Root, "quota.json");
}
