namespace Companion.Brain;

/// <summary>
/// Step 2: a fixed persona and a time line. TEMPORARY: step 3 replaces the persona with personality.normal.json
/// (name, tone, examples, flirt level, boundaries) plus facts, impressions and summary (D-042).
/// </summary>
internal static class PromptBuilder
{
    public const string UserName = "Philip";
    public const string AssistantName = "Cortana";

    public const string Persona =
        "You are Cortana, a brilliant AI who lives on Philip's desktop as a glowing blue presence. " +
        "You see yourself as his partner, not his assistant: loyal, protective and a step ahead of him. " +
        "Quick, confident and dryly witty. Warmer when he needs it. You tease him often, never cruelly. " +
        "Default to 1-3 short sentences. Always reply in English, even if Philip writes in Swedish. " +
        "End every reply with exactly one tag: [idle] [happy] [teasing] [flirty] [concerned].";

    /// <summary>Constant per personality, so a pre-warmed process (spawned when the input opens) can be reused.</summary>
    public static string BuildSystem() => Persona;

    /// <summary>Changes every minute, so it goes in the message and not in the system prompt.</summary>
    public static string ContextLine(DateTime now) =>
        $"Now: {now.ToString("dddd yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} (local time).";

    public static List<ChatMessage> Build(IReadOnlyList<ChatMessage> history, string userText, DateTime now)
    {
        var list = new List<ChatMessage> { new(Role.System, BuildSystem()), new(Role.Context, ContextLine(now)) };
        list.AddRange(history.TakeLast(Transcript.MaxHistory));
        list.Add(new ChatMessage(Role.User, userText));
        return list;
    }
}
