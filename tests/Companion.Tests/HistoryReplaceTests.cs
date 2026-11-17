using System.IO;
using Companion.Brain;
using Xunit;

namespace Companion.Tests;

public class HistoryReplaceTests
{
    [Fact]
    public void ReplaceLastReplyRewritesTheLastLineOfTheFileAndKeepsTheRest()
    {
        var path = Path.Combine(Path.GetTempPath(), "companion-history-test-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var h = new History(path);
            h.AppendTurn("one", "first full reply", interrupted: false);
            h.AppendTurn("two", "second full reply", interrupted: false);
            h.ReplaceLastReply("second", interrupted: true);

            Assert.Equal("second [interrupted]", h.Messages[^1].Text);
            var again = new History(path);                      // what a restart reads back
            Assert.Equal(new[] { "one", "first full reply", "two", "second [interrupted]" }, again.Messages.Select(m => m.Text));
            Assert.Equal(4, File.ReadAllLines(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReplaceLastReplyOnAnEmptyHistoryDoesNothing()
    {
        var h = new History(null);
        h.ReplaceLastReply("x", interrupted: true);
        Assert.Empty(h.Messages);
    }
}
