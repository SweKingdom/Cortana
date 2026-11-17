using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Companion.Character;

/// <summary>
/// The speech bubble: a Popup above the orb (below it when there is no room), created when first shown and
/// released when hidden, so it costs nothing while she is quiet. Grows with the text up to about 320 px, scrolls
/// on long replies and fades after max(6 s, 60 ms per character) with a Storyboard (no timer).
/// </summary>
internal sealed class SpeechBubble
{
    private const double MaxTextWidth = 296;
    private const double Overlap = 6;         // the orb control has an empty margin around the glow; sit just inside it
    private readonly FrameworkElement _target;

    private Popup? _popup;
    private Border? _border;
    private ScrollViewer? _scroll;
    private TextBlock? _text;
    private Storyboard? _fade;
    private int _fadeVersion;
    private bool _isFiller;

    public SpeechBubble(FrameworkElement target) => _target = target;

    public bool IsOpen => _popup is { IsOpen: true };
    /// <summary>True while the bubble shows a filler line (to be replaced by the real reply).</summary>
    public bool ShowsFiller => IsOpen && _isFiller;

    /// <summary>Replaces the content: a filler line, an error line, or the first piece of a reply.</summary>
    public void SetText(string text, bool filler)
    {
        Ensure();
        StopFade();
        _isFiller = filler;
        _text!.Text = text;
        _text.FontStyle = filler ? FontStyles.Italic : FontStyles.Normal;
        _text.Opacity = filler ? 0.75 : 1.0;
        _scroll!.ScrollToTop();
        _popup!.IsOpen = true;
    }

    /// <summary>Adds streamed text to the reply.</summary>
    public void Append(string delta)
    {
        if (!IsOpen) { SetText(delta, filler: false); return; }
        StopFade();
        _text!.Text += delta;
        _scroll!.ScrollToEnd();
    }

    /// <summary>The reply is complete: stay for max(6 s, 60 ms per character), then fade out and go away.</summary>
    public void StartFade(int chars)
    {
        if (_border == null || !IsOpen) return;
        StopFade();
        int version = ++_fadeVersion;
        var delay = TimeSpan.FromMilliseconds(Math.Max(6000, 60.0 * chars));
        var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400)) { BeginTime = delay, FillBehavior = FillBehavior.HoldEnd };
        Storyboard.SetTarget(anim, _border);
        Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
        _fade = new Storyboard();
        _fade.Children.Add(anim);
        _fade.Completed += (_, _) => { if (version == _fadeVersion) Hide(); };
        _fade.Begin(_border, isControllable: true);
    }

    /// <summary>Removes the bubble at once and releases its window.</summary>
    public void Hide()
    {
        StopFade();
        if (_popup != null) { _popup.IsOpen = false; _popup.Child = null; }
        _popup = null; _border = null; _scroll = null; _text = null;
        _isFiller = false;
    }

    private void StopFade()
    {
        _fadeVersion++;
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
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0xA0, 0xFF)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 8, 11, 8),
            Child = _scroll,
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
