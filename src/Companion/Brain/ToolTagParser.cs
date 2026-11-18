using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Memory;

namespace Companion.Brain;

internal sealed record RememberCall(MemoryKind Kind, string Text, string? Replaces);

/// <summary>
/// Strict reader for the text tool protocol <c>&lt;tool name="x"&gt;{json}&lt;/tool&gt;</c> (D-057, D-045 rule 1).
/// Step 3 knows one tool, <c>remember</c>; the rest of the tool loop arrives in step 5. The arguments must be a JSON
/// object with exactly the known properties and the right types; anything else is rejected, never guessed at.
/// </summary>
internal static class ToolTagParser
{
    private static readonly Regex Open = new(@"^\s+name\s*=\s*""(?<n>[a-z_]+)""\s*>(?<body>.*)$", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Id = new(@"^[fi]\d+$", RegexOptions.Compiled);

    /// <param name="raw">Everything between "&lt;tool" and "&lt;/tool&gt;", as <see cref="ReplyCleaner.ToolTags"/> collects it.</param>
    /// <param name="error">A short reason code (no message text) when this returns false.</param>
    public static bool TryParseRemember(string raw, out RememberCall call, out string error)
    {
        call = null!;
        var m = Open.Match(raw);
        if (!m.Success) { error = "bad_tag"; return false; }
        var name = m.Groups["n"].Value;
        if (name != "remember") { error = "unknown_tool:" + name; return false; }

        JsonElement root;
        try { root = JsonDocument.Parse(m.Groups["body"].Value.Trim()).RootElement; }
        catch (JsonException) { error = "bad_json"; return false; }
        if (root.ValueKind != JsonValueKind.Object) { error = "not_object"; return false; }

        string? kind = null, text = null, replaces = null;
        foreach (var p in root.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.String) { error = "bad_type:" + p.Name; return false; }
            switch (p.Name)
            {
                case "kind": kind = p.Value.GetString(); break;
                case "text": text = p.Value.GetString(); break;
                case "replaces": replaces = p.Value.GetString(); break;
                default: error = "unknown_property:" + p.Name; return false;
            }
        }
        if (kind == null || text == null) { error = "missing_property"; return false; }
        MemoryKind? k = kind switch { "fact" => MemoryKind.Fact, "preference" => MemoryKind.Preference, "impression" => MemoryKind.Impression, _ => null };
        if (k == null) { error = "bad_kind"; return false; }
        if (text.Trim().Length is 0 or > MemoryStore.MaxTextChars) { error = "bad_text_length"; return false; }
        if (replaces != null && !Id.IsMatch(replaces)) { error = "bad_replaces"; return false; }

        call = new RememberCall(k.Value, text, replaces);
        error = "";
        return true;
    }
}
