using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Companion.Core;
using Companion.Native;

namespace Companion.Character;

public partial class CharacterWindow : Window
{
    private readonly Settings _settings;
    private IntPtr _hwnd;
    private bool _paused;

    internal CharacterWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    /// <summary>A click on the orb that was not a drag (opens the chat input, or interrupts a reply).</summary>
    internal event Action? OrbClicked;

    public bool ClickThrough { get; private set; }
    public bool NoActivate { get; private set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetExStyleFlag(_hwnd, NativeMethods.WS_EX_TOOLWINDOW, true); // hide from Alt+Tab
        PlaceOnScreen();
    }

    /// <summary>Restore saved position if it is still on a monitor, else bottom-right of the work area.</summary>
    public void PlaceOnScreen()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;

        if (_settings.WindowX is int x && _settings.WindowY is int y)
        {
            var test = new NativeMethods.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
            if (NativeMethods.MonitorFromRect(ref test, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                return;
            }
        }

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var wa = SystemParameters.WorkArea; // DIPs of the primary monitor
        int dx = (int)((wa.Right - 24) * scale) - w;
        int dy = (int)((wa.Bottom - 24) * scale) - h;
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, dx, dy, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private void Orb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        NativeMethods.GetWindowRect(_hwnd, out var before);
        try { DragMove(); } catch (InvalidOperationException) { return; } // returns when the button is released
        if (NativeMethods.GetWindowRect(_hwnd, out var r))
        {
            // Moved less than a few pixels = a click, not a drag.
            if (Math.Abs(r.Left - before.Left) <= 3 && Math.Abs(r.Top - before.Top) <= 3) { OrbClicked?.Invoke(); return; }
            _settings.WindowX = r.Left;
            _settings.WindowY = r.Top;
            _settings.Save();
        }
    }

    public void SetClickThrough(bool on)
    {
        ClickThrough = on;
        NativeMethods.SetExStyleFlag(_hwnd, NativeMethods.WS_EX_TRANSPARENT, on);
    }

    public void SetNoActivate(bool on)
    {
        NoActivate = on;
        NativeMethods.SetExStyleFlag(_hwnd, NativeMethods.WS_EX_NOACTIVATE, on);
    }

    /// <summary>Panic / tray toggle: hidden means no frames drawn (D-006).</summary>
    public void ToggleVisible()
    {
        if (IsVisible) Hide(); else Show();
    }

    /// <summary>Session lock pauses the orb even though the window is technically visible.</summary>
    public void SetSessionPaused(bool paused)
    {
        _paused = paused;
        UpdateAnimation();
    }

    internal void SetActivity(Activity activity) => Orb.Animator.SetActivity(activity);

    private void UpdateAnimation()
    {
        if (IsVisible && !_paused) Orb.Animator.Start(); else Orb.Animator.Pause();
    }
}
