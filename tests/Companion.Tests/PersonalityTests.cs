using System.IO;
using System.Text.Json;
using Companion.Brain;
using Companion.Core;
using Companion.Memory;
using Xunit;

namespace Companion.Tests;

public class PersonalityTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "companion-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void DefaultIsVersion2WithEveryField()
    {
        var p = Personality.Default;
        Assert.Equal(2, p.Version);
        Assert.Equal("Cortana", p.Name);
        Assert.Equal("Philip", p.User);
        for (int level = 0; level <= 4; level++) Assert.NotEmpty(p.FlirtLines(level));
        Assert.Equal(5, p.FlirtLines(4).Count);
        Assert.Contains(p.Boundaries!, b => b.Contains("Jealousy is always a joke"));
        Assert.Equal(19, p.Examples!.Length);
        Assert.Contains("[idle] [happy] [teasing] [flirty] [concerned]", p.Moods);
        Assert.Equal(8, p.Fillers!.Length);
        Assert.NotEmpty(p.Backstory!); Assert.NotEmpty(p.Relationship!); Assert.NotEmpty(p.Traits!);
        Assert.NotEmpty(p.Interests!.Hers!); Assert.NotEmpty(p.Interests.His!); Assert.NotEmpty(p.Speech!);
        Assert.NotEmpty(p.Nagging!); Assert.NotEmpty(p.Never!); Assert.NotEmpty(p.PostHistory!);
        Assert.Null(p.Tone);   // version 2 has no tone field
    }

    [Fact]
    public void ReminderExamplesAreNotInUntilStep5()
        => Assert.DoesNotContain(Personality.Default.Examples!, e => e.User.Contains("remind", StringComparison.OrdinalIgnoreCase) || e.Cortana.Contains("reminder", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void EveryExampleEndsWithAMoodTag()
    {
        foreach (var e in Personality.Default.Examples!)
        {
            var t = e.Cortana.TrimEnd();
            Assert.EndsWith("]", t);
            var tag = t[(t.LastIndexOf('[') + 1)..^1];
            Assert.NotNull(MoodTag.Parse(tag));
        }
    }

    [Fact]
    public void TheRememberExampleParsesWithTheStrictParser()
    {
        var ex = Personality.Default.Examples!.Single(e => e.Cortana.Contains("<tool"));
        var inner = ex.Cortana[(ex.Cortana.IndexOf("<tool", StringComparison.Ordinal) + 5)..ex.Cortana.IndexOf("</tool>", StringComparison.Ordinal)];
        Assert.True(ToolTagParser.TryParseRemember(inner, out var call, out var error), error);
        Assert.Equal(MemoryKind.Fact, call.Kind);
    }

    [Fact]
    public void MissingFieldsFallBackToTheDefaultFieldByField()
    {
        var p = Personality.Parse("""{ "version": 2, "identity": "You are Test." }""");
        Assert.Equal("You are Test.", p.Identity);
        Assert.Equal(Personality.Default.Backstory, p.Backstory);
        Assert.Equal(Personality.Default.Traits, p.Traits);
        Assert.Equal(Personality.Default.Interests!.His, p.Interests!.His);
        Assert.Equal(Personality.Default.PostHistory, p.PostHistory);
        Assert.Equal(8, p.Fillers!.Length);
        Assert.NotEmpty(p.FlirtLines(3));
    }

    [Fact]
    public void AFlirtLevelOutsideZeroToFourIsClamped()
    {
        Assert.Equal(Personality.Default.FlirtLines(4), Personality.Default.FlirtLines(9));
        Assert.Equal(Personality.Default.FlirtLines(0), Personality.Default.FlirtLines(-3));
    }

    [Fact]
    public void BadJsonThrows() => Assert.Throws<JsonException>(() => Personality.Parse("{ nope"));

    [Fact]
    public void SourceCreatesTheFileFromTheDefaultAndReloadsEdits()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "personality.normal.json");
        var src = new PersonalitySource(path);
        Assert.Equal("Cortana", src.Current.Name);
        Assert.True(File.Exists(path));

        File.WriteAllText(path, """{ "version": 1, "name": "Edited" }""");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));   // make sure the stamp differs
        Assert.Equal("Edited", src.Current.Name);
    }

    [Fact]
    public void ACorruptFileIsCopiedAsideAndTheDefaultIsUsed()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "personality.normal.json");
        File.WriteAllText(path, "{ this is not json");
        var src = new PersonalitySource(path);
        Assert.Equal("Cortana", src.Current.Name);
        Assert.True(File.Exists(Path.Combine(dir, "personality.normal.unreadable.json")));
    }
}

public class PromptBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 14, 32, 0);

    private static PromptBuilder Make(int level, MemoryStore? memory = null)
        => new(new PersonalitySource(null), () => level, memory);

    [Fact]
    public void OnlyTheChosenFlirtLevelIsSent()
    {
        var four = Make(4).BuildSystem();
        Assert.Contains("openly into Philip", four);
        Assert.DoesNotContain("Playful teasing and mild double meanings", four);

        var two = Make(2).BuildSystem();
        Assert.Contains("Playful teasing and mild double meanings", two);
        Assert.DoesNotContain("openly into Philip", two);
        Assert.Contains("level 2 of 4", two);

        Assert.Contains("No flirting.", Make(0).BuildSystem());
    }

    [Fact]
    public void BoundariesExamplesAndMoodsAreAlwaysThere()
    {
        foreach (int level in new[] { 0, 2, 4 })
        {
            var s = Make(level).BuildSystem();
            Assert.Contains("Jealousy is always a joke", s);
            Assert.Contains("After midnight, nudge him toward sleep", s);
            Assert.Contains("Philip: morning", s);
            Assert.Contains("Cortana: Morning. Up before noon?", s);
            Assert.EndsWith(Personality.Default.Moods!, s);
            Assert.Contains("[idle] [happy] [teasing] [flirty] [concerned]", s);
        }
    }

    [Fact]
    public void FillersAreNeverSentToTheModel()
    {
        var s = Make(4).BuildSystem();
        Assert.DoesNotContain("Give me a sec, hotshot.", s);
        Assert.DoesNotContain("Processing. Try to look patient.", s);
    }

    [Fact]
    public void TheSystemPromptIsStableBetweenCalls()
    {
        var b = Make(4);
        Assert.Equal(b.BuildSystem(), b.BuildSystem());   // the warm process is reused only if this holds
        Assert.NotEqual(Make(4).BuildSystem(), Make(3).BuildSystem());
    }

    [Fact]
    public void AnEmptyMemoryTellsHerNotToInventAnything()
        => Assert.Contains("nothing saved yet. Never invent memories", Make(4, new MemoryStore(null)).BuildSystem());

    [Fact]
    public void MemoryFilesAndTheSummaryGoIntoTheSystemPrompt()
    {
        var mem = new MemoryStore(null, () => new DateTime(2026, 11, 17));
        mem.Add(MemoryKind.Fact, "Philip codes in C#");
        mem.Add(MemoryKind.Preference, "Keep replies short");
        mem.Add(MemoryKind.Impression, "He seemed stressed today");
        mem.WriteSummary("They talked about a null reference bug.");
        var s = Make(4, mem).BuildSystem();
        Assert.Contains("- [f1] Philip codes in C#", s);
        Assert.Contains("## Preferences", s);
        Assert.Contains("- [f2] Keep replies short", s);
        Assert.Contains("- [i1] He seemed stressed today", s);
        Assert.Contains("They talked about a null reference bug.", s);
        Assert.DoesNotContain("nothing saved yet", s);
        Assert.DoesNotContain("2026-11-17", s);   // dates and sources stay in the file, not the prompt
    }

    [Fact]
    public void BuildHasSystemContextHistoryAndTheNewMessage()
    {
        var hist = new[] { new ChatMessage(Role.User, "hi"), new ChatMessage(Role.Assistant, "hello [happy]") };
        var m = Make(4).Build(hist, "morning", Now);
        // The post-history reminder (D-074) sits after the history and just before the new message.
        Assert.Equal(new[] { Role.System, Role.Context, Role.User, Role.Assistant, Role.PostHistory, Role.User }, m.Select(x => x.Role));
        Assert.Equal(Personality.Default.PostHistory, m[^2].Text);
        Assert.Contains("Tuesday 2026-11-17 14:32", m[1].Text);
        Assert.Equal("morning", m[^1].Text);
    }

    [Fact]
    public void OverTheHistoryBudgetTheOldestMessagesGoFirstButEightStay()
    {
        var hist = new List<ChatMessage>();
        for (int i = 0; i < 10; i++)
        {
            hist.Add(new ChatMessage(Role.User, $"q{i} " + new string('x', 800)));
            hist.Add(new ChatMessage(Role.Assistant, $"a{i} " + new string('y', 800)));
        }
        var m = Make(4).Build(hist, "now", Now);
        var kept = m.Where(x => x.Role is Role.User or Role.Assistant).SkipLast(1).Select(x => x.Text).ToList();   // without the new message
        Assert.True(kept.Count >= PromptBuilder.MinHistory);
        Assert.True(kept.Count < 20);                            // the oldest went
        Assert.StartsWith("a9", kept[^1]);                       // newest kept...
        Assert.DoesNotContain(kept, t => t.StartsWith("q0 "));   // ...oldest dropped
        Assert.Equal(m[0].Text, Make(4).BuildSystem());          // the personality is never trimmed
    }

    [Fact]
    public void AtLeastEightMessagesStayEvenWhenTheyAreFarOverTheBudget()
    {
        var hist = new List<ChatMessage>();
        for (int i = 0; i < 10; i++)
        {
            hist.Add(new ChatMessage(Role.User, $"q{i} " + new string('x', 8000)));
            hist.Add(new ChatMessage(Role.Assistant, $"a{i} " + new string('y', 8000)));
        }
        var m = Make(4).Build(hist, "now", Now);
        int kept = m.Count(x => x.Role is Role.User or Role.Assistant) - 1;
        Assert.Equal(PromptBuilder.MinHistory, kept);
        Assert.Contains(m, x => x.Text.EndsWith("[shortened]"));   // shortened rather than dropped
    }

    [Fact]
    public void ShortHistoryIsNeverTrimmedAndTheNewMessageNeverCut()
    {
        var hist = new List<ChatMessage>();
        for (int i = 0; i < 10; i++) { hist.Add(new ChatMessage(Role.User, $"q{i} short")); hist.Add(new ChatMessage(Role.Assistant, $"a{i} short")); }
        var long_ = new string('z', 9000);
        var m = Make(4).Build(hist, long_, Now);
        Assert.Equal(20, m.Count(x => x.Role is Role.User or Role.Assistant) - 1);
        Assert.Equal(long_, m[^1].Text);
    }

    [Fact]
    public void AVeryLongMessageIsShortenedBeforeItIsDropped()
    {
        var hist = new[] { new ChatMessage(Role.User, new string('x', 20000)), new ChatMessage(Role.Assistant, "ok") };
        var m = Make(4).Build(hist, "next", Now);
        Assert.Contains(m, x => x.Text.EndsWith("[shortened]"));
    }

    [Fact]
    public void DefaultSystemPromptIsInTheExpectedRange()
    {
        int tokens = PromptBuilder.EstimateTokens(Make(4).BuildSystem());
        Assert.InRange(tokens, 1400, 2200);   // about 1 650 at level 4 (D-075)
    }
}

public class MemoryStoreTests
{
    private static readonly DateTime Day = new(2026, 11, 16);
    private static MemoryStore Mem() => new(null, () => Day);

    [Fact]
    public void LinesHaveIdDateAndSource()
    {
        var m = Mem();
        var r = m.Add(MemoryKind.Fact, "Philip codes in C#");
        Assert.Equal(AddStatus.Added, r.Status);
        Assert.Equal("f1", r.Entry!.Id);
        Assert.Equal("- [f1 | 2026-11-16 | chat] Philip codes in C#", m.Read(MemoryStore.FactsFile).Trim());
    }

    [Fact]
    public void IdsCountUpPerFileAndImpressionsHaveTheirOwnPrefix()
    {
        var m = Mem();
        Assert.Equal("f1", m.Add(MemoryKind.Fact, "a").Entry!.Id);
        Assert.Equal("f2", m.Add(MemoryKind.Fact, "b").Entry!.Id);
        Assert.Equal("i1", m.Add(MemoryKind.Impression, "c").Entry!.Id);
        Assert.Contains("- [i1 |", m.Read(MemoryStore.ImpressionsFile));
    }

    [Fact]
    public void PreferencesLiveUnderTheirHeadingAndFactsStayAboveIt()
    {
        var m = Mem();
        m.Add(MemoryKind.Preference, "Keep replies short");
        m.Add(MemoryKind.Fact, "Philip lives in Sweden");
        var text = m.Read(MemoryStore.FactsFile);
        Assert.True(text.IndexOf("Philip lives in Sweden", StringComparison.Ordinal) < text.IndexOf("## Preferences", StringComparison.Ordinal));
        Assert.True(text.IndexOf("## Preferences", StringComparison.Ordinal) < text.IndexOf("Keep replies short", StringComparison.Ordinal));
        Assert.Single(m.Active(MemoryKind.Preference));
        Assert.Single(m.Active(MemoryKind.Fact));
    }

    [Fact]
    public void ACorrectionIsANewLineAndTheOldOneLeavesThePrompt()
    {
        var m = Mem();
        m.Add(MemoryKind.Fact, "Philip codes in C#");
        var r = m.Add(MemoryKind.Fact, "Philip codes in C# and Rust", replaces: "f1");
        Assert.Equal(AddStatus.Added, r.Status);
        Assert.Contains("| replaces f1]", m.Read(MemoryStore.FactsFile));
        Assert.Contains("Philip codes in C#", m.Read(MemoryStore.FactsFile));        // append-only: the old line is still in the file
        var prompt = m.ForPrompt(MemoryStore.FactsFile);
        Assert.DoesNotContain("- [f1]", prompt);
        Assert.Contains("- [f2] Philip codes in C# and Rust", prompt);
    }

    [Fact]
    public void ReplacingAnUnknownIdIsInvalid()
        => Assert.Equal(AddStatus.Invalid, Mem().Add(MemoryKind.Fact, "x", replaces: "f9").Status);

    [Fact]
    public void ADuplicateIsNotAddedTwice()
    {
        var m = Mem();
        m.Add(MemoryKind.Fact, "Philip codes in C#");
        Assert.Equal(AddStatus.Duplicate, m.Add(MemoryKind.Fact, "philip codes in c#").Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTextIsInvalid(string text) => Assert.Equal(AddStatus.Invalid, Mem().Add(MemoryKind.Fact, text).Status);

    [Fact]
    public void TooLongTextIsInvalid() => Assert.Equal(AddStatus.Invalid, Mem().Add(MemoryKind.Fact, new string('x', MemoryStore.MaxTextChars + 1)).Status);

    [Fact]
    public void NewlinesInTextBecomeSpaces()
        => Assert.Equal("a b", Mem().Add(MemoryKind.Fact, "a\n  b").Entry!.Text);

    [Fact]
    public void UndoRemovesTheLineAndBringsBackWhatItReplaced()
    {
        var m = Mem();
        m.Add(MemoryKind.Fact, "old");
        var corr = m.Add(MemoryKind.Fact, "new", replaces: "f1").Entry!;
        Assert.NotNull(m.Undo(corr.Id));
        var prompt = m.ForPrompt(MemoryStore.FactsFile);
        Assert.Contains("- [f1] old", prompt);
        Assert.DoesNotContain("new", prompt);
        Assert.Null(m.Undo(corr.Id));   // already gone
    }

    [Fact]
    public void AtTheCapNewEntriesAreRefusedAndNothingIsLost()
    {
        var m = Mem();
        int added = 0;
        for (int i = 0; i < 1000; i++)
        {
            var r = m.Add(MemoryKind.Fact, $"note number {i} " + new string('z', 100));
            if (r.Status == AddStatus.Full) break;
            Assert.Equal(AddStatus.Added, r.Status);
            added++;
        }
        Assert.InRange(added, 5, 999);
        Assert.True(m.Tokens(MemoryStore.FactsFile) <= MemoryStore.CapTokens);
        Assert.Equal(AddStatus.Full, m.Add(MemoryKind.Fact, "note number 9999 " + new string('z', 100)).Status);
        Assert.Equal(added, m.Active(MemoryKind.Fact).Count);
        // The other file has its own cap.
        Assert.Equal(AddStatus.Added, m.Add(MemoryKind.Impression, "still room here").Status);
    }

    [Fact]
    public void HandWrittenLinesAndHeadingsReachThePromptUnchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "companion-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, MemoryStore.FactsFile), "Philip's cat is called Misty.\n");
        var m = new MemoryStore(dir, () => Day);
        m.Add(MemoryKind.Fact, "Philip codes in C#");
        var prompt = m.ForPrompt(MemoryStore.FactsFile);
        Assert.Contains("Philip's cat is called Misty.", prompt);
        Assert.Contains("- [f1] Philip codes in C#", prompt);
    }

    [Fact]
    public void FilesPersistAcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "companion-test-" + Guid.NewGuid().ToString("N"));
        new MemoryStore(dir, () => Day).Add(MemoryKind.Fact, "Philip codes in C#");
        var again = new MemoryStore(dir, () => Day);
        Assert.Single(again.Active(MemoryKind.Fact));
        Assert.Equal("f2", again.Add(MemoryKind.Fact, "second").Entry!.Id);
    }

    [Fact]
    public void SummaryRoundTrips()
    {
        var m = Mem();
        Assert.Equal("", m.ReadSummary());
        m.WriteSummary("  They fixed a bug.  ");
        Assert.Equal("They fixed a bug.", m.ReadSummary());
    }
}

public class ToolTagParserTests
{
    private static bool Parse(string raw, out RememberCall c, out string e) => ToolTagParser.TryParseRemember(raw, out c, out e);

    [Fact]
    public void AValidRememberTagParses()
    {
        Assert.True(Parse(" name=\"remember\">{\"kind\":\"preference\",\"text\":\"short replies\"}", out var c, out _));
        Assert.Equal(MemoryKind.Preference, c.Kind);
        Assert.Equal("short replies", c.Text);
        Assert.Null(c.Replaces);
    }

    [Fact]
    public void ReplacesIsOptionalAndChecked()
    {
        Assert.True(Parse(" name=\"remember\">{\"kind\":\"fact\",\"text\":\"x\",\"replaces\":\"f12\"}", out var c, out _));
        Assert.Equal("f12", c.Replaces);
        Assert.False(Parse(" name=\"remember\">{\"kind\":\"fact\",\"text\":\"x\",\"replaces\":\"nope\"}", out _, out var e));
        Assert.Equal("bad_replaces", e);
    }

    [Theory]
    [InlineData(" name=\"set_reminder\">{}", "unknown_tool:set_reminder")]
    [InlineData(" name=\"remember\">not json", "bad_json")]
    [InlineData(" name=\"remember\">[1]", "not_object")]
    [InlineData(" name=\"remember\">{\"kind\":\"fact\"}", "missing_property")]
    [InlineData(" name=\"remember\">{\"kind\":\"fact\",\"text\":\"x\",\"extra\":\"y\"}", "unknown_property:extra")]
    [InlineData(" name=\"remember\">{\"kind\":\"fact\",\"text\":5}", "bad_type:text")]
    [InlineData(" name=\"remember\">{\"kind\":\"secret\",\"text\":\"x\"}", "bad_kind")]
    [InlineData(" name=\"remember\">{\"kind\":\"fact\",\"text\":\"\"}", "bad_text_length")]
    [InlineData("garbage", "bad_tag")]
    public void ABadTagIsRejectedWithAReason(string raw, string reason)
    {
        Assert.False(Parse(raw, out _, out var e));
        Assert.Equal(reason, e);
    }
}

public class MoodAndExpressionTests
{
    [Theory]
    [InlineData("happy", "Happy")]
    [InlineData("TEASING", "Teasing")]
    [InlineData("flirty", "Flirty")]
    [InlineData("concerned", "Concerned")]
    [InlineData("idle", "Idle")]
    public void TagsParse(string tag, string expected) => Assert.Equal(Enum.Parse<Mood>(expected), MoodTag.Parse(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sad")]
    public void UnknownOrMissingTagsAreNull(string? tag) => Assert.Null(MoodTag.Parse(tag));

    [Fact]
    public void IdleAndMissingMeanNoExpression()
    {
        Assert.Equal(Companion.Character.Expression.None, Companion.Character.ExpressionMap.From(Mood.Idle));
        Assert.Equal(Companion.Character.Expression.None, Companion.Character.ExpressionMap.From(null));
        Assert.Equal(Companion.Character.Expression.Flirty, Companion.Character.ExpressionMap.From(Mood.Flirty));
    }
}
