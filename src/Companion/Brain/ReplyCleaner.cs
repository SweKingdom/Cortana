using System.Text;
using System.Text.RegularExpressions;
using Companion.Core;

namespace Companion.Brain;

/// <summary>
/// Cleans a streamed reply before it reaches the bubble: removes tool tags (D-057), which are never shown, and the
/// mood tag at the very end (kept in <see cref="Mood"/>). The text of each tool tag is collected in <see cref="ToolTags"/> for the strict parser (D-057). Streaming: text that might still turn into a
/// tag is held back until it is clear, and trailing whitespace is held until more text follows.
/// <para>Leaked planning: a model sometimes starts with a note to itself ("Give him a quick, decisive pick. Mood: teasing.").
/// The first paragraph is therefore held briefly, judged when it ends, and dropped if it carries a "Mood:" label or talks about
/// Philip in the third person (he, him, his, Philip, with no "you" in it). The hold lasts at most
/// <see cref="LeadHoldMs"/> and ends at the first "you", so ordinary replies still stream. A paragraph shown after the hold
/// and judged a leak later is taken back (<see cref="TakeRetract"/>); a reply that is only that one paragraph is kept.</para>
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

    /// <summary>
    /// The longest the first paragraph is held back before it is shown anyway, in milliseconds. The caller owns the
    /// timer and calls <see cref="ReleaseLead"/> when it runs out; this is the one place the number lives.
    /// </summary>
    public const int LeadHoldMs = 400;

    private static readonly Regex ParagraphBreak = new(@"\r?\n[ \t]*\r?\n", RegexOptions.Compiled);
    private static readonly Regex MoodLabel = new(@"\bmood\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SecondPerson = new(@"\byou(r|rs|rself)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ThirdPerson = new(@"\b(he|him|his|philip)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Vocative = new(@"(^|[,;—-]\s*)Philip\s*(?=[,.!?:;]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private enum Lead { Holding, Streaming, Done }
    private Lead _lead = Lead.Holding;
    private readonly StringBuilder _held = new();   // first-paragraph text not yet handed out (Holding)
    private readonly StringBuilder _para = new();   // the first paragraph so far, handed out or not (until its end)
    private int _handedOut;                         // characters of the first paragraph already handed out
    private int _retract;

    /// <summary>Why the leading paragraph was dropped ("mood_label" or "third_person"), or null. Reason only, never the text.</summary>
    public string? LeakDropped { get; private set; }
    /// <summary>Why the leading paragraph looked like a leak but was kept because it was the whole reply, or null.</summary>
    public string? LeakKept { get; private set; }
    /// <summary>First-paragraph text is being held back, waiting to be judged or released.</summary>
    public bool Holding => _lead == Lead.Holding && _held.Length > 0;

    public string Push(string delta) { _undecided.Append(delta); return Gate(Drain(final: false), final: false); }
    public string Flush() => Gate(Drain(final: true), final: true);

    /// <summary>
    /// The hold ran out: hands out what is held so far. The paragraph is still judged when it ends; if it turns out to be a
    /// leak, <see cref="TakeRetract"/> says how much of it had already gone out.
    /// </summary>
    public string ReleaseLead()
    {
        if (!Holding) return "";
        var s = _held.ToString();
        _held.Clear();
        _lead = Lead.Streaming;
        _handedOut = s.Length;
        return s;
    }

    /// <summary>
    /// After a <c>Push</c> that judged an already released first paragraph to be a leak: how many characters of text handed
    /// out earlier belonged to it (the caller takes them back). Zero otherwise; reading it resets it.
    /// </summary>
    public int TakeRetract() { var r = _retract; _retract = 0; return r; }

    private static string? LeakReason(string paragraph)
    {
        if (MoodLabel.IsMatch(paragraph)) return "mood_label";
        var p = Vocative.Replace(paragraph, "$1");
        return ThirdPerson.IsMatch(p) && !SecondPerson.IsMatch(p) ? "third_person" : null;
    }

    private string Gate(string visible, bool final)
    {
        if (_lead == Lead.Done) return visible;
        if (visible.Length == 0 && !final) return "";
        _para.Append(visible);
        var para = _para.ToString();
        int priorLen = para.Length - visible.Length;   // text of the paragraph from before this piece
        var m = ParagraphBreak.Match(para);
        if (m.Success)
        {
            // The paragraph ended inside this piece (the break arrives whole: whitespace is held with the text after it).
            var reason = LeakReason(para[..m.Index]);
            var held = _held.ToString();
            _held.Clear();
            var before = _lead == Lead.Holding ? 0 : _handedOut;   // what already went out
            _lead = Lead.Done; _para.Clear();
            if (reason == null) return held + visible;
            LeakDropped = reason;
            _retract = before;
            int cut = Math.Max(m.Index + m.Length - priorLen, 0);   // the part of this piece up to the end of the break is still the leak
            return visible.Length > cut ? visible[cut..] : "";
        }
        if (final)
        {
            var held = _held.ToString();
            _held.Clear();
            _lead = Lead.Done; _para.Clear();
            LeakKept = LeakReason(para);
            return held + visible;
        }
        if (_lead == Lead.Streaming) { _handedOut += visible.Length; return visible; }
        _held.Append(visible);
        if (SecondPerson.IsMatch(para) && !MoodLabel.IsMatch(para)) return ReleaseLead();   // an ordinary reply: no reason to wait
        return "";
    }

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
