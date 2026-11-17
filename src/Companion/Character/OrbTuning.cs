namespace Companion.Character;

/// <summary>
/// Idle breathing values in one place (D-054). Change them here; nothing else needs touching,
/// except that a bigger amplitude must still fit the window (see the note on BreathAmplitude).
/// </summary>
internal static class OrbTuning
{
    /// <summary>Idle frame rate (D-051/D-054). The COMPANION_FPS dev hook can override it for measurement.</summary>
    public const int IdleFps = 10;

    /// <summary>Length of one full breath (in and out), in seconds.</summary>
    public const double CycleSeconds = 4.0;

    /// <summary>
    /// Scale of the orb at the top of the breath (1.0 = resting size). The window is 168 px and the glow
    /// fades to nothing at 92 % of its 152 px size, so anything up to about 1.2 fits without clipping.
    /// </summary>
    public const double BreathAmplitude = 1.14;

    /// <summary>Glow brightness (opacity) at rest and at the top of the breath, moving in sync with the scale.</summary>
    public const double GlowOpacityMin = 0.55;
    public const double GlowOpacityMax = 0.90;

    /// <summary>Thinking (step 2, D-058): frame rate and speed of the same breathing, in a purple tint. Only while a reply is slow.</summary>
    public const int ThinkingFps = 30;
    public const double ThinkingSpeedRatio = 2.0;

    /// <summary>Talking (step 3): the same breathing, quicker, at 30 fps. Runs while the reply is being shown.</summary>
    public const int TalkingFps = 30;
    public const double TalkingCycleSeconds = 1.6;
    public const double TalkingAmplitude = 1.12;
    public const double TalkingGlowMin = 0.60, TalkingGlowMax = 0.95;

    /// <summary>Thinking particles (step 3): small dots circling the orb, only while thinking.</summary>
    public const int ParticleCount = 6;
    public const double ParticleRadius = 66;      // from the centre; the window is 168 px so 84 is the edge
    public const double ParticleSize = 5;
    public const double ParticleSpinSeconds = 3.0;

    // ---- Mood persistence (D-070): the expression colour is held between messages; after this long without chat it fades. ----
    public const int MoodHoldSeconds = 600;
    public const double MoodFadeSeconds = 60;

    // ---- Expressions (step 3). Colour holds until the bubble goes; the motion is what the table in the architecture says. ----
    // 30 fps is only for thinking and for talking (while text is being revealed). Once the reveal has ended every
    // expression motion below runs at the idle frame rate (IdleFps), so a pinned bubble never holds 30 fps (D-068).
    /// <summary>happy: a small bounce (a few quick, bigger breaths), then back to normal breathing in turquoise.</summary>
    public const double HappyAmplitude = 1.20, HappyCycleSeconds = 0.6;
    public const int HappyRepeats = 3;
    /// <summary>teasing: a quick blink of the glow, a few times, then normal breathing in pink.</summary>
    public const double TeasingCycleSeconds = 0.3, TeasingGlowMin = 0.20, TeasingGlowMax = 1.0;
    public const int TeasingRepeats = 5;
    /// <summary>flirty: a slow, soft pulse for as long as the expression lasts, at the idle frame rate.</summary>
    public const double FlirtyAmplitude = 1.10, FlirtyCycleSeconds = 3.0, FlirtyGlowMin = 0.60, FlirtyGlowMax = 0.95;
    /// <summary>concerned: a duller glow and a smaller breath, at the idle frame rate.</summary>
    public const double ConcernedAmplitude = 1.06, ConcernedGlowMin = 0.25, ConcernedGlowMax = 0.50;
}
