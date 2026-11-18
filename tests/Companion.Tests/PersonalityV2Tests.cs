using System.IO;
using System.Text.Json.Nodes;
using Companion.Brain;
using Xunit;

namespace Companion.Tests;

/// <summary>Personality file version 2 (D-074): new fields, minLevel, post-history, v1 compatibility and the upgrade.</summary>
public class PersonalityV2Tests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "companion-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Cortana.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Cortana.sln not found above " + AppContext.BaseDirectory);
    }

    // An edited version 1 file: the old default with a different identity.
    private const string EditedV1 = """
        { "version": 1, "name": "Cortana", "user": "Philip", "identity": "You are Cortana, edited by hand.",
          "tone": "Calm and brief.", "flirting": { "4": ["Edited level four."] },
          "examples": [ { "user": "hi", "cortana": "Hello. [idle]" } ] }
        """;

    [Fact]
    public void TheDocsCopyAndTheBuiltInDefaultAreTheSameFile()
    {
        var docs = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "docs", "personality-v2.json")));
        Assert.True(JsonNode.DeepEquals(docs, JsonNode.Parse(Personality.DefaultText)), "docs/personality-v2.json differs from default.personality.normal.json");
    }

    [Fact]
    public void TheBuiltInVersion1DefaultIsVersion1()
    {
        var v1 = Personality.Parse(Personality.V1DefaultText);
        Assert.Equal(1, v1.Version);
        Assert.NotEmpty(v1.Tone!);
        Assert.Equal(4, v1.Examples!.Length);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 16)]
    [InlineData(2, 17)]
    [InlineData(3, 18)]
    [InlineData(4, 19)]
    public void ExamplesAreFilteredByMinLevel(int level, int expected)
        => Assert.Equal(expected, Personality.Default.ExamplesFor(level).Count);

    [Fact]
    public void TheSystemPromptHoldsOnlyTheExamplesForTheLevel()
    {
        var four = new PromptBuilder(new PersonalitySource(null), () => 4).BuildSystem();
        var zero = new PromptBuilder(new PersonalitySource(null), () => 0).BuildSystem();
        Assert.Contains("wish you were here", four);
        Assert.Contains("I tried another AI today", four);
        Assert.DoesNotContain("wish you were here", zero);
        Assert.DoesNotContain("I tried another AI today", zero);
        Assert.Contains("five more minutes", zero);   // minLevel 0 stays
    }

    [Fact]
    public void TheSystemPromptCarriesTheNewFields()
    {
        var s = new PromptBuilder(new PersonalitySource(null), () => 4).BuildSystem();
        var p = Personality.Default;
        Assert.Contains(p.Backstory!, s); Assert.Contains(p.Relationship!, s);
        foreach (var t in p.Traits!) Assert.Contains(t, s);
        Assert.Contains(p.Interests!.Hers!, s); Assert.Contains(p.Interests.His!, s);
        foreach (var t in p.Speech!) Assert.Contains(t, s);
        Assert.Contains(p.Nagging!, s); Assert.Contains(p.Never!, s);
        Assert.DoesNotContain(p.PostHistory!, s);   // it goes after the history, not in the system prompt
        Assert.DoesNotContain("Tone:", s);
    }

    [Fact]
    public void PostHistoryComesLastInTheTranscriptRightBeforeTheNewMessage()
    {
        var b = new PromptBuilder(new PersonalitySource(null), () => 4);
        var hist = new[] { new ChatMessage(Role.User, "hi"), new ChatMessage(Role.Assistant, "hello [happy]") };
        var t = Transcript.Build(b.Build(hist, "morning", new DateTime(2026, 11, 18, 14, 0, 0)), "Philip", "Cortana");
        int conv = t.IndexOf("Cortana: hello [happy]", StringComparison.Ordinal);
        int post = t.IndexOf(Personality.Default.PostHistory!, StringComparison.Ordinal);
        int last = t.IndexOf("Philip: morning", StringComparison.Ordinal);
        Assert.True(conv >= 0 && conv < post && post < last, t);
        Assert.EndsWith("Philip: morning", t);
        Assert.Equal(last, t.LastIndexOf("Philip:", StringComparison.Ordinal));   // nothing between the reminder and the message
    }

    [Fact]
    public void PostHistoryIsSentEvenOnTheFirstMessage()
    {
        var b = new PromptBuilder(new PersonalitySource(null), () => 4);
        var t = Transcript.Build(b.Build(Array.Empty<ChatMessage>(), "hi", new DateTime(2026, 11, 18, 14, 0, 0)), "Philip", "Cortana");
        Assert.Contains(Personality.Default.PostHistory!, t);
        Assert.DoesNotContain("Conversation so far", t);
        Assert.EndsWith("Philip: hi", t);
    }

    // --- version 1 files ---

    [Fact]
    public void AVersion1FileStillLoadsAndKeepsItsOwnBehaviour()
    {
        var dir = TempDir(); var path = Path.Combine(dir, "personality.normal.json");
        File.WriteAllText(path, EditedV1);
        var src = new PersonalitySource(path);
        var s = new PromptBuilder(src, () => 4).BuildSystem();
        Assert.Contains("You are Cortana, edited by hand.", s);
        Assert.Contains("Tone: Calm and brief.", s);
        Assert.Contains("Edited level four.", s);
        Assert.Contains("Philip: hi", s);
        Assert.DoesNotContain("Backstory:", s);        // v2 fields are not filled in for a v1 file
        Assert.DoesNotContain("Who you are:", s);
        Assert.DoesNotContain("Nudges:", s);
        Assert.DoesNotContain("Never:", s);
        Assert.DoesNotContain("wish you were here", s); // the default's examples are not mixed in
        var msgs = new PromptBuilder(src, () => 4).Build(Array.Empty<ChatMessage>(), "x", DateTime.Now);
        Assert.DoesNotContain(msgs, m => m.Role == Role.PostHistory);
        Assert.NotEmpty(src.Current.Fillers!);          // legacy fields still fall back field by field
    }

    [Fact]
    public void AnEditedVersion1FileIsKeptAndANoteIsLogged()
    {
        var dir = TempDir(); var path = Path.Combine(dir, "personality.normal.json");
        File.WriteAllText(path, EditedV1);
        var src = new PersonalitySource(path);
        _ = src.Current;
        Assert.Equal(V1Outcome.KeptEdited, src.V1);
        Assert.Equal(EditedV1, File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(dir, "personality.normal.v1.json")));
    }

    [Fact]
    public void AnUneditedVersion1DefaultIsUpgradedAndTheOldFileIsKept()
    {
        var dir = TempDir(); var path = Path.Combine(dir, "personality.normal.json");
        File.WriteAllText(path, Personality.V1DefaultText);
        var src = new PersonalitySource(path);
        Assert.Equal(2, src.Current.Version);
        Assert.Equal(V1Outcome.Upgraded, src.V1);
        Assert.Equal(Personality.DefaultText, File.ReadAllText(path));
        Assert.Equal(Personality.V1DefaultText, File.ReadAllText(Path.Combine(dir, "personality.normal.v1.json")));
    }

    [Fact]
    public void ReformattingTheVersion1DefaultIsNotAnEdit()
    {
        var dir = TempDir(); var path = Path.Combine(dir, "personality.normal.json");
        var compact = JsonNode.Parse(Personality.V1DefaultText)!.ToJsonString();   // same content, no whitespace
        File.WriteAllText(path, compact);
        var src = new PersonalitySource(path);
        Assert.Equal(2, src.Current.Version);
        Assert.Equal(V1Outcome.Upgraded, src.V1);
    }

    [Fact]
    public void AVersion2FileIsLeftAlone()
    {
        var dir = TempDir(); var path = Path.Combine(dir, "personality.normal.json");
        const string text = """{ "version": 2, "name": "Cortana", "identity": "Mine." }""";
        File.WriteAllText(path, text);
        var src = new PersonalitySource(path);
        Assert.Equal("Mine.", src.Current.Identity);
        Assert.Equal(V1Outcome.NotV1, src.V1);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void AMissingFileIsCreatedAsVersion2()
    {
        var dir = TempDir(); var path = Path.Combine(dir, "personality.normal.json");
        var src = new PersonalitySource(path);
        Assert.Equal(2, src.Current.Version);
        Assert.Equal(Personality.DefaultText, File.ReadAllText(path));
    }

    [Fact]
    public void TheUpgradeIsOnlyCheckedOncePerRun()
    {
        var dir = TempDir(); var path = Path.Combine(dir, "personality.normal.json");
        File.WriteAllText(path, EditedV1);
        var src = new PersonalitySource(path);
        _ = src.Current;
        // Later, the user pastes the old default back in by hand: it is read as a v1 file, not rewritten mid-run.
        File.WriteAllText(path, Personality.V1DefaultText);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(1, src.Current.Version);
        Assert.Equal(Personality.V1DefaultText, File.ReadAllText(path));
    }
}
