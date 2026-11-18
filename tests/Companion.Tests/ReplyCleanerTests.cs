using Companion.Brain;
using Xunit;

namespace Companion.Tests;

public class ReplyCleanerTests
{
    private static (string shown, ReplyCleaner c) Run(params string[] deltas)
    {
        var c = new ReplyCleaner();
        var shown = string.Concat(deltas.Select(c.Push)) + c.Flush();
        return (shown, c);
    }

    [Fact]
    public void PlainTextPassesThrough() => Assert.Equal("Hello there.", Run("Hello ", "there.").shown);

    [Fact]
    public void TrailingMoodTagIsRemovedAndRemembered()
    {
        var (shown, c) = Run("Morning, sleepyhead. ", "[teas", "ing]");
        Assert.Equal("Morning, sleepyhead.", shown);
        Assert.Equal("teasing", c.Mood);
    }

    [Fact]
    public void MoodTagOnItsOwnLineIsRemoved()
        => Assert.Equal("Done.", Run("Done.\n\n[happy]\n").shown);

    [Fact]
    public void BracketsInTheMiddleAreKept()
        => Assert.Equal("see [1] and [happy] face", Run("see [1] and [happy] ", "face").shown);

    [Fact]
    public void UnknownTrailingBracketTextIsKept()
        => Assert.Equal("a [note]", Run("a [note]").shown);

    [Fact]
    public void WellFormedToolTagIsStripped()
    {
        var (shown, _) = Run("Done.\n<tool name=\"set_reminder\">{\"text\":\"x\",\"when\":\"2026-11-17T18:00\"}</tool>\n[happy]");
        Assert.Equal("Done.", shown);
    }

    [Fact]
    public void ToolTagSplitAcrossChunksNeverLeaksPartOfItIntoTheOutput()
    {
        var c = new ReplyCleaner();
        var parts = new[] { "Sure.\n<to", "ol name=\"op", "en\">{\"target\":\"x\"}</to", "ol>\n[happy]" };
        var seen = new List<string>();
        foreach (var p in parts) seen.Add(c.Push(p));
        seen.Add(c.Flush());
        var all = string.Concat(seen);
        Assert.Equal("Sure.", all);
        Assert.DoesNotContain(seen, s => s.Contains('<') || s.Contains("tool"));
    }

    [Fact]
    public void TwoToolTagsAreBothStripped()
        => Assert.Equal("Ok.", Run("Ok.\n<tool name=\"a\">{}</tool>\n<tool name=\"b\">{}</tool>").shown);

    [Fact]
    public void UnclosedToolTagIsDroppedAndReported()
    {
        var (shown, c) = Run("Ok.\n<tool name=\"open\">{\"target\":\"x\"");
        Assert.Equal("Ok.", shown);
        Assert.True(c.UnclosedTag);
    }

    [Fact]
    public void LessThanThatIsNotATagIsKept()
        => Assert.Equal("1 < 2 and <toolbox> ok", Run("1 < 2 and ", "<toolbox> ok").shown);

    [Fact]
    public void LeadingWhitespaceIsDropped() => Assert.Equal("Hi", Run("\n  Hi").shown);

    [Fact]
    public void OnlyAMoodTagGivesAnEmptyReply() => Assert.Equal("", Run("[idle]").shown);

    // --- leaked planning (step 3b) ---

    [Fact]
    public void ALeadingParagraphWithAMoodLabelIsDropped()
    {
        var (shown, c) = Run("Give him a quick, decisive pick. Mood: teasing.\n\nPick the second one. Done. [teasing]");
        Assert.Equal("Pick the second one. Done.", shown);
        Assert.Equal("mood_label", c.LeakDropped);
        Assert.Equal("teasing", c.Mood);
    }

    [Fact]
    public void ALeadingParagraphAboutPhilipInTheThirdPersonIsDropped()
    {
        var (shown, c) = Run("Philip skriver på svenska, men jag svarar på engelska.\n\nGood evening to you too. [happy]");
        Assert.Equal("Good evening to you too.", shown);
        Assert.Equal("third_person", c.LeakDropped);
    }

    [Theory]
    [InlineData("He wants a quick answer, so keep it short.")]
    [InlineData("His message is casual. Reply in kind.")]
    public void ThirdPersonPronounsWithoutYouAreALeak(string note)
    {
        var (shown, c) = Run(note + "\n\nReal reply. [idle]");
        Assert.Equal("Real reply.", shown);
        Assert.NotNull(c.LeakDropped);
    }

    [Fact]
    public void TheLeakIsDroppedEvenWhenItArrivesInSmallChunks()
    {
        var c = new ReplyCleaner();
        var parts = new[] { "Give him a ", "quick pick. Mo", "od: teasing.", "\n", "\nTake the red one. ", "[teasing]" };
        var seen = parts.Select(c.Push).ToList();
        seen.Add(c.Flush());
        Assert.Equal("Take the red one.", string.Concat(seen));
        Assert.DoesNotContain(seen, x => x.Contains("quick pick") || x.Contains("Mood"));
    }

    [Theory]
    [InlineData("Philip, that is a terrible idea.\n\nSecond paragraph.")]
    [InlineData("Nice one, Philip.\n\nSecond paragraph.")]
    [InlineData("You said he would call. Philip never does.\n\nSecond paragraph.")]
    [InlineData("Morning. Up before noon?\n\nSecond paragraph.")]
    public void OrdinaryFirstParagraphsAreKept(string reply)
        => Assert.Equal(reply, Run(reply).shown);

    [Fact]
    public void OnlyTheLeadingParagraphIsJudged()
        => Assert.Equal("Fine.\n\nGive him a pick. Mood: teasing.", Run("Fine.\n\nGive him a pick. Mood: teasing.").shown);

    [Fact]
    public void AReplyThatMentionsYouStreamsWithoutWaitingForTheParagraphEnd()
    {
        var c = new ReplyCleaner();
        Assert.Equal("You were right about that one,", c.Push("You were right about that one, "));
    }

    [Fact]
    public void ReleaseLeadHandsOutWhatIsHeldAndTheNextTextStreams()
    {
        var c = new ReplyCleaner();
        Assert.Equal("", c.Push("Plain words here "));
        Assert.True(c.Holding);
        Assert.Equal("Plain words here", c.ReleaseLead());
        Assert.False(c.Holding);
        Assert.Equal(" and more.", c.Push("and more."));
    }

    [Fact]
    public void ReleaseLeadWithNothingHeldDoesNothing() => Assert.Equal("", new ReplyCleaner().ReleaseLead());

    [Fact]
    public void ALeakJudgedAfterTheReleaseIsTakenBackAndTheRestOfItSkipped()
    {
        var c = new ReplyCleaner();
        Assert.Equal("", c.Push("Give him a quick pick."));
        var released = c.ReleaseLead();
        Assert.Equal("Give him a quick pick.", released);
        Assert.Equal(" Mood: teasing.", c.Push(" Mood: teasing."));   // still the same paragraph: not yet judged
        var next = c.Push("\n\nTake the red one. ");
        Assert.Equal("Take the red one.", next + c.Push("[teasing]") + c.Flush());
        Assert.Equal("mood_label", c.LeakDropped);
        Assert.Equal(released.Length + " Mood: teasing.".Length, c.TakeRetract());
        Assert.Equal(0, c.TakeRetract());
    }

    [Fact]
    public void ALeakJudgedWhileStillHeldNeedsNoRetract()
    {
        var (shown, c) = Run("Give him a quick pick.\n\nTake the red one.");
        Assert.Equal("Take the red one.", shown);
        Assert.Equal(0, c.TakeRetract());
    }

    [Fact]
    public void AnOrdinaryParagraphReleasedByTheTimerIsNotRetracted()
    {
        var c = new ReplyCleaner();
        c.Push("Plain words here ");
        var first = c.ReleaseLead();
        var rest = c.Push("and more.\n\nSecond paragraph.");
        Assert.Equal("Plain words here", first);
        Assert.Equal(" and more.\n\nSecond paragraph.", rest);
        Assert.Null(c.LeakDropped);
        Assert.Equal(0, c.TakeRetract());
    }

    [Fact]
    public void AReplyThatIsOnlyALeakLookingParagraphIsKeptAndReported()
    {
        var (shown, c) = Run("Give him a pick. Mood: teasing.");
        Assert.Equal("Give him a pick. Mood: teasing.", shown);
        Assert.Null(c.LeakDropped);
        Assert.Equal("mood_label", c.LeakKept);
    }
}
