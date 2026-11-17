namespace Companion.Character;

/// <summary>How long the speech bubble stays (D-062). The only place these values live.</summary>
internal static class BubbleTuning
{
    /// <summary>The bubble stays at least this long after the last character is shown.</summary>
    public const double MinShowMs = 8000;
    /// <summary>...or this long per character of the reply, whichever is longer.</summary>
    public const double MsPerChar = 90;
    public const double FadeMs = 400;
    /// <summary>After a hover or an unpin, the bubble stays at least this long before it starts to fade.</summary>
    public const double MinResumeMs = 1500;
}

/// <summary>How reply text is revealed (D-063). The only place these values live.</summary>
internal static class TypewriterTuning
{
    /// <summary>Normal pace while little text is buffered: speech pace.</summary>
    public const double BaseCps = 16;
    /// <summary>Below this many buffered characters the pace stays at <see cref="BaseCps"/>.</summary>
    public const int CatchUpFromChars = 250;
    /// <summary>Each buffered character beyond <see cref="CatchUpFromChars"/> adds this much to the pace.</summary>
    public const double CatchUpGainCps = 0.2;
    /// <summary>The pace never goes above this, however much is buffered.</summary>
    public const double MaxCps = 40;
    /// <summary>Pause after . ? ! when followed by a space or the end of the text.</summary>
    public const double SentencePauseMs = 400;
    /// <summary>Pause after , ; : when followed by a space or the end of the text.</summary>
    public const double CommaPauseMs = 180;
    /// <summary>Timer period while text is being revealed. The timer is stopped the rest of the time.</summary>
    public const int TickMs = 33;
    /// <summary>A tick after a stall never reveals more than this much time worth of text.</summary>
    public const double MaxStepMs = 250;
}
