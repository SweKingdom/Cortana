using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Companion.Character;

/// <summary>
/// Idle breathing: the orb scales and the glow brightens in sync (values in <see cref="OrbTuning"/>).
/// One Storyboard driven by the WPF compositor: no timer, no CompositionTarget.Rendering.
/// Pause() stops it so no frames are drawn. Thinking (step 2) is the same breathing, faster and at 30 fps, in a
/// purple tint; it only runs while a reply is slow (D-051, D-058).
/// </summary>
internal sealed class OrbAnimator : IAnimator
{
    private readonly Storyboard _breathing;
    private readonly FrameworkElement _host;
    private readonly OrbParts _parts;
    private readonly int _idleFps;
    private bool _running;
    private Activity _activity = Activity.Idle;

    internal OrbAnimator(FrameworkElement host, OrbParts parts)
    {
        _host = host;
        _parts = parts;
        host.RenderTransform = new ScaleTransform(1, 1);

        var half = TimeSpan.FromSeconds(OrbTuning.CycleSeconds / 2);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        // Animate through the element's property path so WPF resolves the ScaleTransform itself
        // (targeting the transform object directly was silently not applied).
        var sx = new DoubleAnimation(1.0, OrbTuning.BreathAmplitude, half) { AutoReverse = true, EasingFunction = ease };
        var sy = sx.Clone();
        Storyboard.SetTarget(sx, host);
        Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTarget(sy, host);
        Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

        // Glow brightness in sync with the scale (same timing and easing), so every frame differs.
        var op = new DoubleAnimation(OrbTuning.GlowOpacityMin, OrbTuning.GlowOpacityMax, half) { AutoReverse = true, EasingFunction = ease };
        Storyboard.SetTarget(op, parts.Glow);
        Storyboard.SetTargetProperty(op, new PropertyPath(UIElement.OpacityProperty));

        _breathing = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        _breathing.Children.Add(sx);
        _breathing.Children.Add(sy);
        _breathing.Children.Add(op);
        _idleFps = FpsOverride() ?? OrbTuning.IdleFps;
        Timeline.SetDesiredFrameRate(_breathing, _idleFps);
    }

    /// <summary>COMPANION_FPS dev hook (read once at startup), used by perfcheck for P3.</summary>
    private static int? FpsOverride()
        => int.TryParse(Environment.GetEnvironmentVariable("COMPANION_FPS"), out var f) && f is >= 1 and <= 60 ? f : null;

    public Activity Activity => _activity;

    /// <summary>Idle and Thinking are drawn; Talking and Sleeping arrive with steps 3 and 4 and are ignored for now.</summary>
    public void SetActivity(Activity activity)
    {
        if (activity == _activity || activity is Activity.Talking or Activity.Sleeping) return;
        _activity = activity;
        bool thinking = activity == Activity.Thinking;
        OrbVisual.Apply(_parts, thinking ? OrbPalette.Thinking : OrbPalette.Idle);
        Timeline.SetDesiredFrameRate(_breathing, thinking ? OrbTuning.ThinkingFps : _idleFps);
        _breathing.SpeedRatio = thinking ? OrbTuning.ThinkingSpeedRatio : 1.0;
        if (_running) { _breathing.Stop(_host); _breathing.Begin(_host, isControllable: true); } // restart to apply the new rate
    }

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
