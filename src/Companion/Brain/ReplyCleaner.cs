using System.Text;
using Companion.Core;

namespace Companion.Brain;

/// <summary>
/// Cleans a streamed reply before it reaches the bubble: removes tool tags (D-057), which are never shown, and the
/// mood tag at the very end (kept in <see cref="Mood"/>). The text of each tool tag is collected in <see cref="ToolTags"/> for the strict parser (D-057). Streaming: text that might still turn into a
/// tag is held back until it is clear, and trailing whitespace is held until more text follows.
/// </summary>
internal sealed class ReplyCleaner
{
    private static readonly string[] Moods = MoodTag.Names;
    private const string ToolOpen = "<tool";
    private const string ToolClose = "</tool>";

    private readonly StringBuilder _undecided = new();
    private readonly StringBuilder _tagBuf = new();
    private string _heldWhitespace = "";
    private bool _inTag, _started;
    private bool _skipSpaceAfterTag;   // text stood before a dropped tag: the whitespace after it is dropped, so no double space

    public string? Mood { get; private set; }
    /// <summary>For each well-formed tool tag in the reply, what stood between "&lt;tool" and "&lt;/tool&gt;" (e.g. <c> name="remember"&gt;{...}</c>).</summary>
    public List<string> ToolTags { get; } = new();
    /// <summary>True if the reply ended inside a tool tag; the rest was dropped, never shown.</summary>
    public bool UnclosedTag { get; private set; }

    public string Push(string delta) { _undecided.Append(delta); return Drain(final: false); }
    public string Flush() => Drain(final: true);

    private string Drain(bool final)
    {
        var s = _undecided.ToString();
        _undecided.Clear();
        var o = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            if (_inTag)
            {
                int end = s.IndexOf(ToolClose, i, StringComparison.OrdinalIgnoreCase);
                if (end >= 0)
                {
                    _tagBuf.Append(s, i, end - i);
                    ToolTags.Add(_tagBuf.ToString()); _tagBuf.Clear();
                    _skipSpaceAfterTag = _heldWhitespace.Length > 0;
                    _inTag = false; i = end + ToolClose.Length; continue;
                }
                var partial = final ? "" : PartialSuffix(s, i, ToolClose); // "</to" may complete next time
                _tagBuf.Append(s, i, s.Length - i - partial.Length);
                _undecided.Append(partial);
                i = s.Length;
                break;
            }

            char c = s[i];
            if (c == '<')
            {
                var rest = s.AsSpan(i);
                if (rest.Length >= ToolOpen.Length && rest[..ToolOpen.Length].Equals(ToolOpen, StringComparison.OrdinalIgnoreCase))
                {
                    if (rest.Length == ToolOpen.Length)
                    {
                        if (!final) { Hold(s, i); i = s.Length; break; }
                    }
                    else if (char.IsWhiteSpace(rest[ToolOpen.Length]) || rest[ToolOpen.Length] == '>')
                    {
                        _inTag = true; _tagBuf.Clear(); i += ToolOpen.Length; continue;
                    }
                }
                else if (!final && rest.Length < ToolOpen.Length && ToolOpen.AsSpan(0, rest.Length).Equals(rest, StringComparison.OrdinalIgnoreCase))
                {
                    Hold(s, i); i = s.Length; break;
                }
                Emit(o, c); i++;
            }
            else if (c == '[')
            {
                int close = s.IndexOf(']', i);
                if (close < 0)
                {
                    var partial = s.Substring(i + 1);
                    if (!final && partial.Length <= 9 && Moods.Any(m => m.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
                    { Hold(s, i); i = s.Length; break; }
                    Emit(o, c); i++;
                    continue;
                }
                var word = s.Substring(i + 1, close - i - 1);
                var mood = Moods.FirstOrDefault(m => m.Equals(word, StringComparison.OrdinalIgnoreCase));
                if (mood != null && s.AsSpan(close + 1).IsWhiteSpace())
                {
                    // A mood tag with nothing after it: dropped for good at the end of the reply, otherwise we wait.
                    if (final) { Mood = mood; i = s.Length; break; }
                    Hold(s, i); i = s.Length; break;
                }
                Emit(o, c); i++;
            }
            else { Emit(o, c); i++; }
        }

        if (final && _inTag) UnclosedTag = true;
        return o.ToString();
    }

    private void Hold(string s, int from) => _undecided.Append(s, from, s.Length - from);

    private static string PartialSuffix(string s, int from, string target)
    {
        for (int len = Math.Min(target.Length - 1, s.Length - from); len > 0; len--)
            if (s.AsSpan(s.Length - len).Equals(target.AsSpan(0, len), StringComparison.OrdinalIgnoreCase)) return s.Substring(s.Length - len);
        return "";
    }

    /// <summary>Whitespace is held back and only written when real text follows, so nothing trails a dropped tag.</summary>
    private void Emit(StringBuilder o, char c)
    {
        if (char.IsWhiteSpace(c))
        {
            if (_started && !_skipSpaceAfterTag) _heldWhitespace += c;   // leading whitespace of the reply is dropped
            return;
        }
        _skipSpaceAfterTag = false;
        _started = true;
        if (_heldWhitespace.Length > 0) { o.Append(_heldWhitespace); _heldWhitespace = ""; }
        o.Append(c);
    }
}
