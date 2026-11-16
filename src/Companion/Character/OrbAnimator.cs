using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Companion.Character;

/// <summary>
/// Idle breathing: scale only, 4 s cycle, 10 fps. One Storyboard driven by the WPF compositor:
/// no timer, no CompositionTarget.Rendering. Pause() stops it so no frames are drawn.
/// </summary>
public sealed class OrbAnimator
{
    internal const int DefaultFps = 10;

    private readonly Storyboard _breathing;
    private readonly FrameworkElement _host;
    private bool _running;

    internal OrbAnimator(FrameworkElement host)
    {
        _host = host;
        host.RenderTransform = new ScaleTransform(1, 1);

        var half = TimeSpan.FromSeconds(2);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        // Animate through the element's property path so WPF resolves the ScaleTransform itself
        // (targeting the transform object directly was silently not applied).
        var sx = new DoubleAnimation(1.0, OrbVisual.MaxScale, half) { AutoReverse = true, EasingFunction = ease };
        var sy = sx.Clone();
        Storyboard.SetTarget(sx, host);
        Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTarget(sy, host);
        Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

        _breathing = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        _breathing.Children.Add(sx);
        _breathing.Children.Add(sy);
        Timeline.SetDesiredFrameRate(_breathing, FpsOverride() ?? DefaultFps);
    }

    /// <summary>COMPANION_FPS dev hook (read once at startup), used by perfcheck for P3.</summary>
    internal static int? FpsOverride()
        => int.TryParse(Environment.GetEnvironmentVariable("COMPANION_FPS"), out var f) && f is >= 1 and <= 60 ? f : null;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _breathing.Begin(_host, isControllable: true);
    }

    public void Pause()
    {
        if (!_running) return;
        _running = false;
        _breathing.Stop(_host);
    }
}
