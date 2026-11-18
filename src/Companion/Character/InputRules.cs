namespace Companion.Character;

/// <summary>The rules of the chat input that do not need a window, so they can be tested (D-071, D-073).</summary>
internal static class InputRules
{
    /// <summary>Typing never interrupts a running reply (D-073). Enter, a click on the orb and panic do.</summary>
    public const bool TypingInterrupts = false;

    /// <summary>At most one pre-warm per this long while typing; a warm process is never replaced anyway.</summary>
    public const long ArmAgainMs = 20_000;

    /// <summary>"Never armed": far enough back that now minus it cannot overflow.</summary>
    public const long NotArmed = -1_000_000_000_000;

    /// <summary>
    /// Whether a keystroke should pre-warm the process: there is text, no reply is running (the pre-warm is skipped
    /// then) and it was not armed in the last <see cref="ArmAgainMs"/>.
    /// </summary>
    public static bool ShouldArm(bool hasText, bool replyRunning, long nowTick, long armedTick)
        => hasText && !replyRunning && nowTick - armedTick > ArmAgainMs;
}
