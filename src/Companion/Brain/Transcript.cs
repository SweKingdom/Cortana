using System.Text;

namespace Companion.Brain;

/// <summary>
/// Builds the one stdin message of a per-message claude process (D-055): the app owns the history and replays
/// the last messages as a transcript, then the new message. Format as in prototype P1.
/// </summary>
internal static class Transcript
{
    public const int MaxHistory = 20;

    /// <param name="messages">System is ignored (it goes in the system prompt file). The last User message is the new one.</param>
    public static string Build(IReadOnlyList<ChatMessage> messages, string userName, string assistantName)
    {
        int last = -1;
        for (int i = messages.Count - 1; i >= 0; i--) if (messages[i].Role == Role.User) { last = i; break; }
        if (last < 0) throw new ArgumentException("No user message", nameof(messages));

        var context = messages.Where(m => m.Role == Role.Context).Select(m => m.Text).ToList();
        var history = messages.Take(last).Where(m => m.Role is Role.User or Role.Assistant).TakeLast(MaxHistory).ToList();

        var post = messages.Where(m => m.Role == Role.PostHistory).Select(m => m.Text).ToList();

        if (context.Count == 0 && history.Count == 0 && post.Count == 0) return messages[last].Text; // first message, nothing to add

        var sb = new StringBuilder();
        if (context.Count > 0) sb.Append(string.Join("\n", context)).Append("\n\n");
        if (history.Count > 0)
        {
            sb.Append("Conversation so far:\n");
            foreach (var m in history) sb.Append(m.Role == Role.User ? userName : assistantName).Append(": ").Append(m.Text).Append('\n');
            sb.Append('\n');
        }
        // Last thing before the new message (D-074): a reminder, not part of the conversation, so she does not answer it.
        if (post.Count > 0) sb.Append("[Reminder for ").Append(assistantName).Append(", not part of the conversation: ").Append(string.Join(" ", post)).Append("]\n\n");
        sb.Append(userName).Append(": ").Append(messages[last].Text);
        return sb.ToString();
    }
}
