using System.IO;
using System.Text.Json;

namespace Companion.Brain;

/// <summary>
/// The one owner of the conversation history (D-055). Persisted as memory/normal/history.jsonl, one JSON object
/// per line with "version": 1 (D-044). Step 3 adds the summary and the 30-message cap; until then it only grows.
/// With no path it stays in memory (tests and the perfcheck chat probe).
/// </summary>
internal sealed class History
{
    public const string InterruptedMark = "[interrupted]";
    private sealed record Line(int Version, string Role, string Text, string Time);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string? _path;
    private readonly List<ChatMessage> _messages = new();

    public History(string? path)
    {
        _path = path;
        if (path != null) Load();
    }

    public IReadOnlyList<ChatMessage> Messages => _messages;
    public IReadOnlyList<ChatMessage> Last(int n) => _messages.TakeLast(n).ToList();

    /// <summary>A finished or interrupted turn: the user's message and her reply (an interrupted one ends with [interrupted]).</summary>
    public void AppendTurn(string userText, string replyText, bool interrupted)
    {
        var reply = interrupted ? (replyText.Length > 0 ? replyText + " " : "") + InterruptedMark : replyText;
        Add(new ChatMessage(Role.User, userText));
        Add(new ChatMessage(Role.Assistant, reply));
    }

    private void Add(ChatMessage m)
    {
        _messages.Add(m);
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(new Line(1, m.Role == Role.User ? "user" : "assistant", m.Text, DateTime.Now.ToString("s")), Json);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (IOException) { /* a failed save must not break the chat */ }
    }

    private void Load()
    {
        if (!File.Exists(_path!)) return;
        bool bad = false;
        try
        {
            foreach (var raw in File.ReadLines(_path!))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try
                {
                    var l = JsonSerializer.Deserialize<Line>(raw, Json);
                    if (l is { Role: "user" or "assistant" }) _messages.Add(new ChatMessage(l.Role == "user" ? Role.User : Role.Assistant, l.Text));
                    else bad = true;
                }
                catch (JsonException) { bad = true; }
            }
        }
        catch (IOException) { return; }
        // D-047: unreadable lines -> keep a copy of the file, carry on with what could be read.
        if (bad) try { File.Copy(_path!, Path.ChangeExtension(_path!, ".unreadable.jsonl"), overwrite: true); } catch (IOException) { }
    }
}
