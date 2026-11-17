namespace Companion.Core;

/// <summary>The mood tag at the end of a reply (D-043: it drives the Expression, the colour). Idle means "no expression".</summary>
internal enum Mood { Idle, Happy, Teasing, Flirty, Concerned }

/// <summary>Parser for the [tag] words. The names are the ones the personality file tells her to use.</summary>
internal static class MoodTag
{
    public static readonly string[] Names = { "idle", "happy", "teasing", "flirty", "concerned" };

    /// <summary>"happy" (any case) gives Mood.Happy; anything else, including null, gives null.</summary>
    public static Mood? Parse(string? tag)
    {
        if (tag == null) return null;
        for (int i = 0; i < Names.Length; i++)
            if (Names[i].Equals(tag, StringComparison.OrdinalIgnoreCase)) return (Mood)i;
        return null;
    }
}
