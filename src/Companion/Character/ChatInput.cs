using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Companion.Native;

namespace Companion.Character;

/// <summary>
/// The text box that opens under the orb (above it when there is no room). Enter sends, Esc closes. It exists only
/// while it is open. Closing it unsent is reported so the pre-warmed process can be killed (D-055).
/// </summary>
internal sealed class ChatInput : Window
{
    private const double BoxWidth = 320, BoxHeight = 44;
    private readonly TextBox _box;
    private bool _finished;

    public event Action<string>? Submitted;
    /// <summary>Closed without sending (Esc, click away with an empty box, panic).</summary>
    public event Action? Dismissed;
    /// <summary>The text changed (typing starts: a running reply is interrupted).</summary>
    public event Action? Typing;

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

        _box.TextChanged += (_, _) => Typing?.Invoke();
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (_box.Text.Length == 0) Finish(null); };
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Finish(null); }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            var t = _box.Text.Trim();
            if (t.Length > 0) Finish(t);
        }
    }

    /// <summary>Shows the box and puts it under the orb window, or above it if the screen has no room below.</summary>
    public void ShowNear(Window orb)
    {
        Show();
        Activate();
        _box.Focus();
        Keyboard.Focus(_box);

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

    /// <summary>Sets the text without Enter (used by the perfcheck probe).</summary>
    public void SetTextForProbe(string text) => _box.Text = text;

    public void SubmitForProbe(string text) => Finish(text);

    public void Dismiss() => Finish(null);

    private void Finish(string? text)
    {
        if (_finished) return;
        _finished = true;
        if (text != null) Submitted?.Invoke(text); else Dismissed?.Invoke();
        Close();
    }
}
