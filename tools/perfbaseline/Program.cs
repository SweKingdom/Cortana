using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Companion.Character;

namespace PerfBaseline;

/// <summary>
/// CPU baseline for perfcheck. Same window setup as Companion's CharacterWindow (168x168, layered,
/// transparent, topmost, no taskbar entry, software rendering, Per Monitor V2 DPI) and nothing else:
/// no tray icon, no hotkeys, no chat controller, no animation.
///   --bare    an empty window (window cost only)
///   --static  the orb is drawn once by the real OrbVisual at rest scale, with no Storyboard (window + drawing)
/// Writes start-to-first-frame in ms to COMPANION_PERF_FILE, like the app, so perfcheck can reuse its wait.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool drawOrb = args.Contains("--static");
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var app = new Application();
        var window = new Window
        {
            Title = "PerfBaseline", Width = 168, Height = 168,
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            Topmost = true, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
            UseLayoutRounding = false, SnapsToDevicePixels = false,
        };
        var host = new Grid { Background = null };
        if (drawOrb) host.Children.Add(OrbVisual.Build(1.0));
        window.Content = host;

        window.ContentRendered += (_, _) =>
        {
            var perfFile = Environment.GetEnvironmentVariable("COMPANION_PERF_FILE");
            if (string.IsNullOrEmpty(perfFile)) return;
            try
            {
                double ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                File.WriteAllText(perfFile, ms.ToString("F0", CultureInfo.InvariantCulture));
            }
            catch (IOException) { }
        };
        app.Run(window);
    }
}
