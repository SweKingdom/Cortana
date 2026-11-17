using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Companion.Character;

/// <summary>
/// The orb's motion and colour from two inputs (D-043): the Activity (app: idle, thinking, talking) picks the motion
/// and the Expression (mood tag: happy, teasing, flirty, concerned) picks the colour and, when idle, a short motion
/// of its own. Every motion is one Storyboard of the same breathing (scale + glow brightness), driven by the WPF
/// compositor: no timer, no CompositionTarget.Rendering. Idle and every expression motion run at 10 fps (D-054);
/// only thinking and talking (text being revealed) run at 30 fps (D-068), so nothing that outlasts the reveal, such as
/// a pinned bubble, holds 30 fps. Finite motions (happy bounce, teasing blink) hand over to idle breathing when done.
/// Pause() stops everything so no frames are drawn (hidden, locked).
/// Priority: thinking > talking > the expression's own motion > idle breathing. Values are in <see cref="OrbTuning"/>.
/// </summary>
internal sealed class OrbAnimator : IAnimator
{
    /// <summary>One breathing variant. Repeats 0 = forever.</summary>
    private sealed record Profile(double Amplitude, double CycleSeconds, double GlowMin, double GlowMax, int Fps, int Repeats = 0);

    private readonly FrameworkElement _host;
    private readonly OrbParts _parts;
    private readonly Profile _idle, _thinking, _talking, _happy, _teasing, _flirty, _concerned;
    private readonly Dictionary<Profile, Storyboard> _boards = new();
    private readonly Storyboard _spin;

    private Activity _activity = Activity.Idle;
    private Expression _expression = Expression.None;
    private bool _oneShotDone;            // this expression's finite motion (bounce, blink) has played
    private bool _running;
    private Profile? _playing;
    private OrbPalette? _palette;
    private bool _particlesOn;
    private bool _fading;                 // the held expression's colour is fading back to idle blue (D-070)
    private Action? _fadeDone;

    internal OrbAnimator(FrameworkElement host, OrbParts parts)
    {
        _host = host;
        _parts = parts;
        host.RenderTransform = new ScaleTransform(1, 1);

        int idleFps = FpsOverride() ?? OrbTuning.IdleFps;
        _idle = new(OrbTuning.BreathAmplitude, OrbTuning.CycleSeconds, OrbTuning.GlowOpacityMin, OrbTuning.GlowOpacityMax, idleFps);
        _thinking = new(OrbTuning.BreathAmplitude, OrbTuning.CycleSeconds / OrbTuning.ThinkingSpeedRatio, OrbTuning.GlowOpacityMin, OrbTuning.GlowOpacityMax, OrbTuning.ThinkingFps);
        _talking = new(OrbTuning.TalkingAmplitude, OrbTuning.TalkingCycleSeconds, OrbTuning.TalkingGlowMin, OrbTuning.TalkingGlowMax, OrbTuning.TalkingFps);
        _happy = new(OrbTuning.HappyAmplitude, OrbTuning.HappyCycleSeconds, OrbTuning.GlowOpacityMin, OrbTuning.GlowOpacityMax, idleFps, OrbTuning.HappyRepeats);
        _teasing = new(1.0, OrbTuning.TeasingCycleSeconds, OrbTuning.TeasingGlowMin, OrbTuning.TeasingGlowMax, idleFps, OrbTuning.TeasingRepeats);
        _flirty = new(OrbTuning.FlirtyAmplitude, OrbTuning.FlirtyCycleSeconds, OrbTuning.FlirtyGlowMin, OrbTuning.FlirtyGlowMax, idleFps);
        _concerned = new(OrbTuning.ConcernedAmplitude, OrbTuning.CycleSeconds, OrbTuning.ConcernedGlowMin, OrbTuning.ConcernedGlowMax, idleFps);

        // The thinking particles circle once every few seconds; started and stopped with the thinking activity.
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(OrbTuning.ParticleSpinSeconds)) { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(spin, parts.Particles);
        Storyboard.SetTargetProperty(spin, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spin = new Storyboard();
        _spin.Children.Add(spin);
        Timeline.SetDesiredFrameRate(_spin, OrbTuning.ThinkingFps);
    }

    /// <summary>COMPANION_FPS dev hook (read once at startup), used by perfcheck for P3.</summary>
    private static int? FpsOverride()
        => int.TryParse(Environment.GetEnvironmentVariable("COMPANION_FPS"), out var f) && f is >= 1 and <= 60 ? f : null;

    public Activity Activity => _activity;
    public Expression Expression => _expression;

    /// <summary>Idle, Thinking and Talking are drawn; Sleeping arrives with step 4 and is ignored for now.</summary>
    public void SetActivity(Activity activity)
    {
        if (activity == _activity || activity == Activity.Sleeping) return;
        _activity = activity;
        Apply();
    }

    /// <summary>The reply's mood tag (None clears it). A new expression plays its own motion once the orb is idle.</summary>
    public void SetExpression(Expression expression)
    {
        StopFade();   // a new reply (even with the same tag) brings the colour back
        if (expression == _expression) return;
        _expression = expression;
        _oneShotDone = false;
        Apply();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _playing = null;   // force a restart of whatever should be playing
        Apply();
    }

    public void Pause()
    {
        if (!_running) return;
        _running = false;
        StopPlaying();
        SetParticles(false);
        if (_fading) FinishFade();   // hidden: no frames are drawn, so the fade just completes
    }

    /// <summary>
    /// After a long quiet (D-070): the colour of the held expression fades to idle blue over <paramref name="seconds"/>,
    /// at the idle frame rate, then the expression is none and <paramref name="done"/> is called. Any new expression or
    /// <see cref="StopFade"/> cancels it and brings the colour back.
    /// </summary>
    public void FadeExpression(double seconds, Action done)
    {
        if (_expression == Expression.None || _fading) return;
        if (!_running || _activity != Activity.Idle) { _expression = Expression.None; _oneShotDone = false; Apply(); done(); return; }
        _fading = true;
        _fadeDone = done;
        var to = OrbPalette.Idle;
        int fps = _idle.Fps;
        for (int i = 0; i < to.Glow.Length; i++) FadeStop(_parts.GlowBrush.GradientStops[i], to.Glow[i], seconds, fps, last: false);
        for (int i = 0; i < to.Core.Length; i++) FadeStop(_parts.CoreBrush.GradientStops[i], to.Core[i], seconds, fps, last: i == to.Core.Length - 1);
    }

    private void FadeStop(GradientStop stop, Color to, double seconds, int fps, bool last)
    {
        var a = new ColorAnimation(to, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.HoldEnd };
        Timeline.SetDesiredFrameRate(a, fps);
        if (last) a.Completed += (_, _) => { if (_fading) FinishFade(); };
        stop.BeginAnimation(GradientStop.ColorProperty, a);
    }

    private void ClearFadeAnimations()
    {
        foreach (var s in _parts.GlowBrush.GradientStops) s.BeginAnimation(GradientStop.ColorProperty, null);
        foreach (var s in _parts.CoreBrush.GradientStops) s.BeginAnimation(GradientStop.ColorProperty, null);
    }

    /// <summary>Cancels a fade in progress and shows the held colour again.</summary>
    public void StopFade()
    {
        if (!_fading) return;
        _fading = false; _fadeDone = null;
        ClearFadeAnimations();
        if (_palette != null) OrbVisual.Apply(_parts, _palette);
    }

    private void FinishFade()
    {
        var done = _fadeDone;
        _fading = false; _fadeDone = null;
        ClearFadeAnimations();
        _expression = Expression.None;
        _oneShotDone = false;
        Apply();
        done?.Invoke();
    }

    // ---- what to show -----------------------------------------------------------------------------------------

    private void Apply()
    {
        OrbPalette palette;
        Profile profile;
        bool particles = false;
        switch (_activity)
        {
            case Activity.Thinking: palette = OrbPalette.For(_expression); profile = _thinking; particles = true; break;   // purple particles over the held colour (D-070)
            case Activity.Talking:
                palette = _expression == Expression.None ? OrbPalette.Talking : OrbPalette.For(_expression);   // talking + flirty = warm pink, pulsing
                profile = _talking; break;
            default:
                palette = OrbPalette.For(_expression);
                profile = IdleProfile(_expression, _oneShotDone); break;
        }

        if (!ReferenceEquals(palette, _palette)) { if (_fading) { _fading = false; _fadeDone = null; ClearFadeAnimations(); } OrbVisual.Apply(_parts, palette); _palette = palette; }
        SetParticles(particles && _running);
        if (_running && !Equals(profile, _playing)) Play(profile);
    }

    /// <summary>The expression's own motion while idle: a finite one plays first, then the resting motion.</summary>
    private Profile IdleProfile(Expression e, bool oneShotDone) => e switch
    {
        Expression.Happy when !oneShotDone => _happy,
        Expression.Teasing when !oneShotDone => _teasing,
        Expression.Flirty => _flirty,
        Expression.Concerned => _concerned,
        _ => _idle,
    };

    private void Play(Profile profile)
    {
        StopPlaying();
        var board = Board(profile);
        _playing = profile;
        board.Begin(_host, isControllable: true);
    }

    private void StopPlaying()
    {
        if (_playing != null && _boards.TryGetValue(_playing, out var b)) b.Stop(_host);
        _playing = null;
    }

    private void SetParticles(bool on)
    {
        if (on == _particlesOn) return;
        _particlesOn = on;
        if (on) { _parts.Particles.Visibility = Visibility.Visible; _spin.Begin(_parts.Particles, isControllable: true); }
        else { _spin.Stop(_parts.Particles); _parts.Particles.Visibility = Visibility.Collapsed; }
    }

    private Storyboard Board(Profile p)
    {
        if (_boards.TryGetValue(p, out var existing)) return existing;
        var half = TimeSpan.FromSeconds(p.CycleSeconds / 2);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        // Animate through the element's property path so WPF resolves the ScaleTransform itself
        // (targeting the transform object directly was silently not applied).
        var sx = new DoubleAnimation(1.0, p.Amplitude, half) { AutoReverse = true, EasingFunction = ease };
        var sy = sx.Clone();
        Storyboard.SetTarget(sx, _host);
        Storyboard.SetTargetProperty(sx, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTarget(sy, _host);
        Storyboard.SetTargetProperty(sy, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

        // Glow brightness in sync with the scale (same timing and easing), so every frame differs.
        var op = new DoubleAnimation(p.GlowMin, p.GlowMax, half) { AutoReverse = true, EasingFunction = ease };
        Storyboard.SetTarget(op, _parts.Glow);
        Storyboard.SetTargetProperty(op, new PropertyPath(UIElement.OpacityProperty));

        var board = new Storyboard { RepeatBehavior = p.Repeats > 0 ? new RepeatBehavior(p.Repeats) : RepeatBehavior.Forever };
        board.Children.Add(sx);
        board.Children.Add(sy);
        board.Children.Add(op);
        Timeline.SetDesiredFrameRate(board, p.Fps);
        if (p.Repeats > 0)
            board.Completed += (_, _) =>
            {
                if (!Equals(_playing, p)) return;   // stopped or replaced meanwhile
                _oneShotDone = true;
                _playing = null;
                board.Stop(_host);                  // release the held end values
                Apply();                            // hands over to idle breathing in the expression's colour
            };
        return _boards[p] = board;
    }
}
