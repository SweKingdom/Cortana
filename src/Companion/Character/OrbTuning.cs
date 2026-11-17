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
}
