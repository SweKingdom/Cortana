using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Companion.Character;
using Companion.Core;
using Companion.Native;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Win32;

namespace Companion;

public partial class App : Application
{
    public App()
    {
        // Software rendering is the default: hardware rendering costs ~330 MB private bytes with the GPU
        // driver on this PC (P3). Dev hook: COMPANION_RENDER=hardware turns it off.
        if (Environment.GetEnvironmentVariable("COMPANION_RENDER") != "hardware")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
    }

    private Mutex? _singleInstance;
    private Settings _settings = new();
    private CharacterWindow? _window;
    private TaskbarIcon? _tray;
    private GlobalHotkey? _hotkeys;
    private EventBus? _bus;
    private ChatController? _chat;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\Companion.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        _settings = Settings.Load();
        _window = new CharacterWindow(_settings);
        _window.ContentRendered += OnFirstRender;
        _window.Show();
    }

    private void OnFirstRender(object? sender, EventArgs e)
    {
        _window!.ContentRendered -= OnFirstRender;

        // Start-to-orb measurement for tools/perfcheck.ps1 (only when asked; D-040).
        var perfFile = Environment.GetEnvironmentVariable("COMPANION_PERF_FILE");
        if (!string.IsNullOrEmpty(perfFile))
        {
            try
            {
                double ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                File.WriteAllText(perfFile, ms.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (IOException) { }
        }

        // Tray and hotkey come after the orb is on screen so they never delay it.
        CreateTray();
        // Chat (step 2): nothing starts here; the session and the windows are created on first use.
        _bus = new EventBus();
        _chat = new ChatController(_window, _bus, ephemeral: PerfProbe.Enabled);

        _hotkeys = new GlobalHotkey();
        // D-030: panic hotkey, suggested Ctrl+Alt+H (hard-coded until step 4 settings).
        if (!_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x48 /* H */, TogglePanic))
            ShowHotkeyFailure("Ctrl+Alt+H", "Use Show / Hide in the tray menu.");
        // Chat hotkey (hard-coded until step 4 settings, like the panic key).
        if (!_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_SPACE, () => _chat!.OpenInput()))
            ShowHotkeyFailure("Ctrl+Alt+Space", "Click the orb to chat.");

        if (PerfProbe.Enabled) _ = PerfProbe.RunAsync(_chat);

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void CreateTray()
    {
        var showHide = new MenuItem { Header = "Show / Hide" };
        showHide.Click += (_, _) => TogglePanic();

        var clickThrough = new MenuItem { Header = "Click-through", IsCheckable = true };
        clickThrough.Click += (_, _) => _window!.SetClickThrough(clickThrough.IsChecked);

        // Temporary, for prototype P4. Step 4's GameMode will own this (D-014).
        var noFocus = new MenuItem { Header = "No-focus (game test)", IsCheckable = true };
        noFocus.Click += (_, _) => _window!.SetNoActivate(noFocus.IsChecked);

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(showHide);
        menu.Items.Add(clickThrough);
        menu.Items.Add(noFocus);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);

        _tray = new TaskbarIcon
        {
            ToolTipText = "Companion",
            ContextMenu = menu,
            IconSource = new GeneratedIconSource
            {
                Text = "●",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x7D, 0xFF)),
                BackgroundType = BackgroundType.Ellipse,
                FontSize = 36,
            },
        };
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Hide or show the orb. Hiding is also the panic action (D-030): it cancels a running reply and closes the bubble and input.</summary>
    private void TogglePanic()
    {
        bool wasVisible = _window!.IsVisible;
        _window.ToggleVisible();
        if (wasVisible) _bus?.Publish(new PanicHide());
    }

    private void ShowHotkeyFailure(string combo, string hint)
        => _tray?.ShowNotification("Companion", $"{combo} is already taken. {hint}");

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Dispatcher.BeginInvoke(() => _window?.SetSessionPaused(true));
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Dispatcher.BeginInvoke(() => _window?.SetSessionPaused(false));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => _window?.PlaceOnScreen());

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _hotkeys?.Dispose();
        _chat?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
