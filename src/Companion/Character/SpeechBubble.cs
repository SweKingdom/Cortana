using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Companion.Character;

/// <summary>
/// The speech bubble: a Popup above the orb (below it when there is no room), created when first shown and
/// released when hidden, so it costs nothing while she is quiet. Grows with the text up to about 320 px and
/// scrolls on long replies. After the last character is shown it stays max(8 s, 90 ms per character) and then
/// fades (D-062, values in <see cref="BubbleTuning"/>). Hovering pauses the countdown; a click pins the bubble
/// until it is clicked again or <see cref="Hide"/> is called (a new message). The countdown is a Storyboard, no timer.
/// </summary>
internal sealed class SpeechBubble
{
    private const double MaxTextWidth = 296;
    private const double Overlap = 6;         // the orb control has an empty margin around the glow; sit just inside it
    private static readonly Brush NormalBorder = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0xA0, 0xFF)));
    private static readonly Brush PinnedBorder = Freeze(new SolidColorBrush(Color.FromArgb(0xFF, 0x60, 0xB8, 0xFF)));
    private readonly FrameworkElement _target;

    private Popup? _popup;
    private Border? _border;
    private ScrollViewer? _scroll;
    private TextBlock? _text;
    private StackPanel? _notes;      // note rows under the text (memory notes with Undo, the memory-full line)
    private Storyboard? _fade;
    private int _fadeVersion;
    private bool _isFiller;

    private bool _hover, _pinned;
    private bool _countdown;          // the reply is complete and the bubble is meant to fade
    private bool _fading;             // the fade storyboard is running (its delay is the countdown)
    private double _remainingMs;      // countdown left while paused
    private double _fadeDelayMs;
    private long _fadeBegan;

    public SpeechBubble(FrameworkElement target) => _target = target;

    /// <summary>Set by the owner: called on a click; returns true when it showed text that was still waiting (the click is used up).</summary>
    public Func<bool>? SkipReveal { get; set; }

    /// <summary>The bubble was removed (faded out, hidden or replaced). The owner clears the orb's expression then.</summary>
    public event Action? Closed;

    public bool IsOpen => _popup is { IsOpen: true };
    public bool IsPinned => _pinned;
    /// <summary>True while the bubble shows a filler line (to be replaced by the real reply).</summary>
    public bool ShowsFiller => IsOpen && _isFiller;

    /// <summary>Replaces the content: a filler line, an error line, or the first piece of a reply.</summary>
    public void SetText(string text, bool filler)
    {
        Ensure();
        StopFade();
        _countdown = false;
        _isFiller = filler;
        _text!.Text = text;
        _text.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _notes!.Children.Clear();
        _text.FontStyle = filler ? FontStyles.Italic : FontStyles.Normal;
        _text.Opacity = filler ? 0.75 : 1.0;
        _scroll!.ScrollToTop();
        _popup!.IsOpen = true;
    }

    /// <summary>Adds revealed text to the reply.</summary>
    public void Append(string delta)
    {
        if (!IsOpen) { SetText(delta, filler: false); return; }
        StopFade();
        _countdown = false;
        _text!.Visibility = Visibility.Visible;
        _text.Text += delta;
        _scroll!.ScrollToEnd();
    }

    /// <summary>
    /// The last character has been shown: stay for max(8 s, 90 ms per character), then fade out and go away.
    /// The countdown waits while the pointer is over the bubble or it is pinned.
    /// </summary>
    public void StartFade(int chars)
    {
        if (_border == null || !IsOpen) return;
        _countdown = true;
        _remainingMs = Math.Max(BubbleTuning.MinShowMs, BubbleTuning.MsPerChar * chars);
        if (!(_hover || _pinned)) BeginFade(_remainingMs);
    }

    /// <summary>
    /// Adds a small line under the reply, e.g. "Remembered: ..." with an Undo link. <paramref name="undo"/> runs when the
    /// link is clicked and returns the line's new text. With no bubble open it makes a bubble that holds only the note.
    /// </summary>
    public void AddNote(string text, Func<string>? undo = null)
    {
        if (!IsOpen) SetText("", filler: false);
        StopFade();
        var label = new TextBlock
        {
            Text = text, FontSize = 11, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, MaxWidth = MaxTextWidth - (undo != null ? 48 : 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xC4, 0xE8)), VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(label);
        if (undo != null)
        {
            var link = new TextBlock
            {
                Text = "Undo", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xC8, 0xFF)), TextDecorations = TextDecorations.Underline,
            };
            link.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;   // not a click on the bubble: no pin, no skip
                label.Text = undo();
                row.Children.Remove(link);
            };
            row.Children.Add(link);
        }
        _notes!.Children.Add(row);
    }

    /// <summary>Pins the bubble as a click would (perfcheck's pinned-expression probe).</summary>
    internal void Pin()
    {
        if (_border == null) return;
        _pinned = true;
        _border.BorderBrush = PinnedBorder;
        UpdatePause();
    }

    /// <summary>Removes the bubble at once and releases its window. Also what unpins it.</summary>
    public void Hide()
    {
        StopFade();
        bool wasOpen = _popup != null;
        if (_popup != null) { _popup.IsOpen = false; _popup.Child = null; }
        _popup = null; _border = null; _scroll = null; _text = null; _notes = null;
        _isFiller = false; _hover = false; _pinned = false; _countdown = false;
        if (wasOpen) Closed?.Invoke();
    }

    private void BeginFade(double delayMs)
    {
        StopFade();
        int version = ++_fadeVersion;
        var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(BubbleTuning.FadeMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(anim, _border);
        Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
        _fade = new Storyboard();
        _fade.Children.Add(anim);
        _fade.Completed += (_, _) => { if (version == _fadeVersion) Hide(); };
        _fade.Begin(_border!, isControllable: true);
        _fading = true; _fadeDelayMs = delayMs; _fadeBegan = Stopwatch.GetTimestamp();
    }

    /// <summary>Hover or pin changed: freeze the countdown (keeping what is left) or start it again.</summary>
    private void UpdatePause()
    {
        if (_border == null || !_countdown) return;
        if (_hover || _pinned)
        {
            if (!_fading) return;
            double elapsed = Stopwatch.GetElapsedTime(_fadeBegan).TotalMilliseconds;
            _remainingMs = Math.Max(_fadeDelayMs - elapsed, BubbleTuning.MinResumeMs);   // also covers "already fading out"
            StopFade();
        }
        else if (!_fading) BeginFade(_remainingMs);
    }

    private void StopFade()
    {
        _fadeVersion++;
        _fading = false;
        if (_fade != null && _border != null) _fade.Stop(_border);
        _fade = null;
        if (_border != null) _border.Opacity = 1.0;
    }

    private void Ensure()
    {
        if (_popup != null) return;
        _text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, MaxWidth = MaxTextWidth, FontSize = 13, FontFamily = new FontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF2, 0xFF)),
        };
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220, Content = _text };
        _notes = new StackPanel();
        var body = new StackPanel();
        body.Children.Add(_scroll);
        body.Children.Add(_notes);
        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x18, 0x30)),
            BorderBrush = NormalBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 8, 11, 8),
            Child = body, Cursor = Cursors.Hand,
        };
        _border.MouseEnter += (_, _) => { _hover = true; UpdatePause(); };
        _border.MouseLeave += (_, _) => { _hover = false; UpdatePause(); };
        _border.MouseLeftButtonUp += (_, e) =>
        {
            // While text is still being revealed a click shows the rest at once instead of pinning (D-063).
            if (SkipReveal?.Invoke() == true) { e.Handled = true; return; }
            _pinned = !_pinned;
            if (_border != null) _border.BorderBrush = _pinned ? PinnedBorder : NormalBorder;
            UpdatePause();
            e.Handled = true;
        };
        _popup = new Popup
        {
            AllowsTransparency = true, StaysOpen = true, Focusable = false,
            Placement = PlacementMode.Custom, CustomPopupPlacementCallback = Place, PlacementTarget = _target,
            Child = _border,
        };
        // A Popup does not re-place itself when its child grows, so nudge it (the callback runs again).
        _border.SizeChanged += (_, _) => { if (_popup != null) _popup.HorizontalOffset = _popup.HorizontalOffset == 0 ? 0.01 : 0; };
    }

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    private static CustomPopupPlacement[] Place(Size popup, Size target, Point offset)
    {
        double x = (target.Width - popup.Width) / 2;
        return new[]
        {
            new CustomPopupPlacement(new Point(x, -popup.Height + Overlap), PopupPrimaryAxis.Horizontal),   // above the orb
            new CustomPopupPlacement(new Point(x, target.Height - Overlap), PopupPrimaryAxis.Horizontal),   // below, if no room above
        };
    }
}
