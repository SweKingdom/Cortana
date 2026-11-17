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
}
