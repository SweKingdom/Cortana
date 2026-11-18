using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Companion.Brain;
using Companion.Character;
using Companion.Core;
using Companion.Native;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Win32;
using Activity = Companion.Character.Activity;
using Expression = Companion.Character.Expression;

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
        _chat = new ChatController(_window, _bus, _settings, ephemeral: PerfProbe.Enabled);
        // The daily Sonnet count in the tray tooltip (D-076) until the settings counter of step 4. The quota is recorded before ReplyDone is published.
        _bus.Subscribe<ReplyDone>(_ => { if (_tray != null) _tray.ToolTipText = TooltipText(_chat!.Session.Quota.CountToday(ClaudeCodeBrain.ChatModel)); });

        _hotkeys = new GlobalHotkey();
        // D-030: panic hotkey, suggested Ctrl+Alt+H (hard-coded until step 4 settings).
        if (!_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x48 /* H */, TogglePanic))
            ShowHotkeyFailure("Ctrl+Alt+H", "Use Show / Hide in the tray menu.");
        // Chat hotkey (hard-coded until step 4 settings, like the panic key).
        if (!_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_SPACE, () => _chat!.ToggleInput()))
            ShowHotkeyFailure("Ctrl+Alt+Space", "Click the orb to chat.");

        if (PerfProbe.Enabled) _ = PerfProbe.RunAsync(_chat);
        if (Enum.TryParse<Expression>(Environment.GetEnvironmentVariable("COMPANION_PERF_PINNED"), out var pinned) && pinned != Expression.None)
            _chat.ProbePinned(pinned);   // dev hook for perfcheck -Pinned: a pinned bubble with this expression held

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
        menu.Items.Add(CreateFlirtMenu());
        menu.Items.Add(clickThrough);
        menu.Items.Add(noFocus);
        if (Environment.GetEnvironmentVariable("COMPANION_DEV") == "1") menu.Items.Add(CreatePreviewMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);

        _tray = new TaskbarIcon
        {
            ToolTipText = TooltipText(PerfProbe.Enabled ? 0 : new Quota(AppPaths.QuotaFile).CountToday(ClaudeCodeBrain.ChatModel)),
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

    private static string TooltipText(int sonnetToday) => $"Companion: {sonnetToday} Sonnet message{(sonnetToday == 1 ? "" : "s")} today";

    /// <summary>Flirt level 0-4 (D-017). Applies from the next message; the settings slider comes in step 4.</summary>
    private MenuItem CreateFlirtMenu()
    {
        var names = new[] { "0  Off", "1  Warm", "2  Teasing", "3  Flirty", "4  Max" };
        var root = new MenuItem { Header = "Flirt level" };
        var items = new List<MenuItem>();
        for (int i = 0; i < names.Length; i++)
        {
            int level = i;
            var item = new MenuItem { Header = names[i], IsCheckable = true, IsChecked = Math.Clamp(_settings.FlirtLevel, 0, 4) == i };
            item.Click += (_, _) =>
            {
                _settings.FlirtLevel = level;
                _settings.Save();
                foreach (var other in items) other.IsChecked = ReferenceEquals(other, item);
            };
            items.Add(item);
            root.Items.Add(item);
        }
        return root;
    }

    /// <summary>Dev only (COMPANION_DEV=1): shows each activity and expression without a chat, so Philip can judge the look.</summary>
    private MenuItem CreatePreviewMenu()
    {
        var root = new MenuItem { Header = "Preview (dev)" };
        void Add(string name, Action action) { var m = new MenuItem { Header = name }; m.Click += (_, _) => action(); root.Items.Add(m); }
        Add("Thinking (particles)", () => { _window!.SetExpression(Expression.None); _window.SetActivity(Activity.Thinking); });
        Add("Talking", () => _window!.SetActivity(Activity.Talking));
        Add("Idle", () => _window!.SetActivity(Activity.Idle));
        root.Items.Add(new Separator());
        foreach (var e in new[] { Expression.Happy, Expression.Teasing, Expression.Flirty, Expression.Concerned })
        {
            var expression = e;
            Add(expression.ToString(), () => { _window!.SetActivity(Activity.Idle); _window.SetExpression(Expression.None); _window.SetExpression(expression); });
        }
        Add("Clear expression", () => _window!.SetExpression(Expression.None));
        return root;
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
