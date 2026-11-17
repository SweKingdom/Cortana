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
        _text!.Text += delta;
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

    /// <summary>Removes the bubble at once and releases its window. Also what unpins it.</summary>
    public void Hide()
    {
        StopFade();
        if (_popup != null) { _popup.IsOpen = false; _popup.Child = null; }
        _popup = null; _border = null; _scroll = null; _text = null;
        _isFiller = false; _hover = false; _pinned = false; _countdown = false;
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
        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x18, 0x30)),
            BorderBrush = NormalBorder,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 8, 11, 8),
            Child = _scroll, Cursor = Cursors.Hand,
        };
        _border.MouseEnter += (_, _) => { _hover = true; UpdatePause(); };
        _border.MouseLeave += (_, _) => { _hover = false; UpdatePause(); };
        _border.MouseLeftButtonUp += (_, e) =>
        {
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
