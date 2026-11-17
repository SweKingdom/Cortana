using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Companion.Native;

namespace Companion.Character;

/// <summary>
/// The text box that opens under the orb (above it when there is no room). It is created once and then shown and hidden
/// (D-071): Enter sends and keeps the box open and empty, so Philip can keep typing; Esc clears it and closes; the
/// hotkey, a click outside and a click on the orb hide it and keep the draft for next time. Hiding unsent is reported
/// so the pre-warmed process can be killed (D-055). A click on the speech bubble does not take focus (it is a
/// non-activating popup), so it never counts as outside.
/// </summary>
internal sealed class ChatInput : Window
{
    private const double BoxWidth = 320, BoxHeight = 44;
    private readonly TextBox _box;
    private bool _programmatic;   // the text is being set by the code (clear after send, Esc), not by Philip
    private bool _open;

    /// <summary>Enter: the text was sent. The box is already empty and stays open.</summary>
    public event Action<string>? Submitted;
    /// <summary>Philip changed the text. The argument says whether any text is left. Typing does not interrupt a reply (D-073).</summary>
    public event Action<bool>? TextEdited;
    /// <summary>The box was hidden (Esc, hotkey, click outside, panic). The draft is kept unless it was cleared.</summary>
    public event Action? Hidden;

    /// <summary>Environment.TickCount64 of the last hide caused by losing focus; the orb click that caused it must not reopen the box.</summary>
    public long LastDeactivatedHideTick { get; private set; }

    public ChatInput()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; ShowActivated = true;
        Width = BoxWidth; Height = BoxHeight;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = -10000; Top = -10000; // moved into place after Show

        _box = new TextBox
        {
            FontSize = 14, FontFamily = new FontFamily("Segoe UI"), VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xFF)), CaretBrush = Brushes.White,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2),
            AcceptsReturn = false, MaxLength = 4000,
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x18, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x40, 0xA0, 0xFF)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 6, 12, 6), Child = _box,
        };

        _box.TextChanged += (_, _) => { if (!_programmatic) TextEdited?.Invoke(_box.Text.Length > 0); };
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (_open) { LastDeactivatedHideTick = Environment.TickCount64; HideBox(clear: false); } };
    }

    /// <summary>The box is on screen.</summary>
    public bool IsOpen => _open;
    /// <summary>There is unsent text (a kept draft counts).</summary>
    public bool HasText => _box.Text.Length > 0;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; HideBox(clear: true); }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            var t = _box.Text.Trim();
            if (t.Length > 0) Send(t);
        }
    }

    /// <summary>Shows the box (with a kept draft, if any) and puts it under the orb window, or above it if the screen has no room below.</summary>
    public void ShowNear(Window orb)
    {
        _open = true;
        Show();
        Activate();
        _box.Focus();
        Keyboard.Focus(_box);
        _box.CaretIndex = _box.Text.Length;

        var orbHwnd = new WindowInteropHelper(orb).Handle;
        var me = new WindowInteropHelper(this).Handle;
        if (orbHwnd == IntPtr.Zero || me == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(orbHwnd, out var o);
        NativeMethods.GetWindowRect(me, out var m);
        var wa = NativeMethods.WorkAreaOf(orbHwnd);
        int w = m.Right - m.Left, h = m.Bottom - m.Top;
        int margin = (int)(14 * VisualTreeHelper.GetDpi(orb).DpiScaleY); // the orb window has an empty rim around the glow

        int x = (o.Left + o.Right) / 2 - w / 2;
        int y = o.Bottom - margin;
        if (y + h > wa.Bottom) y = o.Top + margin - h;                   // no room below: above the orb
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
        y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
        NativeMethods.SetWindowPos(me, IntPtr.Zero, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);
    }

    /// <summary>Types text as Philip would (the perfcheck probe): the same events as a keystroke.</summary>
    public void SetTextForProbe(string text) => _box.Text = text;

    /// <summary>Sends text as Enter does (the perfcheck probe).</summary>
    public void SubmitForProbe(string text) => Send(text);

    /// <summary>Hides the box and keeps the draft (hotkey, orb click).</summary>
    public void HideKeepingDraft() => HideBox(clear: false);

    /// <summary>Hides the box and throws the draft away (panic).</summary>
    public void Dismiss() => HideBox(clear: true);

    private void Send(string text)
    {
        SetText("");   // the box is empty before the message goes, so the next keystroke is a fresh first keystroke
        Submitted?.Invoke(text);
    }

    private void SetText(string text)
    {
        _programmatic = true;
        try { _box.Text = text; }
        finally { _programmatic = false; }
    }

    private void HideBox(bool clear)
    {
        if (!_open) return;
        _open = false;
        if (clear) SetText("");
        Hide();
        Hidden?.Invoke();
    }
}
