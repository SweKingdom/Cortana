using System.IO;
using Companion.Brain;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class EffortTests
{
    [Theory]
    [InlineData("haiku", "low")]
    [InlineData("claude-haiku-4-5", "low")]
    [InlineData("sonnet", "low")]
    [InlineData("claude-sonnet-5-5", "low")]
    [InlineData("opus", null)]
    public void HaikuAndSonnetRunAtLowEffort(string model, string? expected)
        => Assert.Equal(expected, ClaudeCodeBrain.EffortFor(model));
}

public class TranscriptTests
{
    [Fact]
    public void FirstMessageWithoutContextIsSentAsIs()
        => Assert.Equal("morning", Transcript.Build(new[] { new ChatMessage(Role.System, "s"), new ChatMessage(Role.User, "morning") }, "Philip", "Cortana"));

    [Fact]
    public void HistoryIsReplayedThenTheNewMessage()
    {
        var m = new List<ChatMessage>
        {
            new(Role.System, "persona"), new(Role.Context, "Now: Tuesday."),
            new(Role.User, "hi"), new(Role.Assistant, "hello [happy]"), new(Role.User, "what now?"),
        };
        Assert.Equal("Now: Tuesday.\n\nConversation so far:\nPhilip: hi\nCortana: hello [happy]\n\nPhilip: what now?",
            Transcript.Build(m, "Philip", "Cortana"));
    }

    [Fact]
    public void OnlyTheLast20MessagesAreReplayed()
    {
        var m = new List<ChatMessage> { new(Role.System, "s") };
        for (int i = 0; i < 30; i++) { m.Add(new(Role.User, $"u{i}")); m.Add(new(Role.Assistant, $"a{i}")); }
        m.Add(new(Role.User, "new"));
        var t = Transcript.Build(m, "Philip", "Cortana");
        Assert.DoesNotContain("Philip: u19\n", t);   // 60 messages, the last 20 are indexes 40..59 = u20..a29
        Assert.DoesNotContain("Cortana: a19\n", t);
        Assert.Contains("Philip: u20\n", t);
        Assert.Equal(20, t.Split('\n').Count(l => l.StartsWith("Philip: u") || l.StartsWith("Cortana: a")));
    }

    [Fact]
    public void WithoutUserMessageItThrows()
        => Assert.Throws<ArgumentException>(() => Transcript.Build(new[] { new ChatMessage(Role.System, "s") }, "P", "C"));
}

public class FillersTests
{
    [Fact]
    public void NeverTheSameLineTwiceInARow()
    {
        var f = new Fillers(new[] { "a", "b", "c" }, new Random(1));
        string prev = "";
        for (int i = 0; i < 500; i++)
        {
            var n = f.Next();
            Assert.NotEqual(prev, n);
            prev = n;
        }
    }

    [Fact]
    public void EveryLineIsUsed()
    {
        var f = new Fillers(new[] { "a", "b", "c" }, new Random(2));
        var seen = Enumerable.Range(0, 200).Select(_ => f.Next()).ToHashSet();
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void ASingleLineIsRepeated()
    {
        var f = new Fillers(new[] { "only" });
        Assert.Equal("only", f.Next());
        Assert.Equal("only", f.Next());
    }

    [Fact]
    public void WithNoListItUsesTheDefaultPersonalitysFillers() { var f = new Fillers(); Assert.Contains(f.Next(), Personality.Default.Fillers!); }
}

public class EventBusTests
{
    [Fact]
    public void SubscribersGetPublishedEventsAndCanUnsubscribe()
    {
        var bus = new EventBus();
        var got = new List<int>();
        var sub = bus.Subscribe<ReplyChunk>(e => got.Add(e.TurnId));
        bus.Publish(new ReplyChunk(1, "x"));
        sub.Dispose();
        bus.Publish(new ReplyChunk(2, "y"));
        Assert.Equal(new[] { 1 }, got);
    }

    [Fact]
    public void TurnTrackerDropsStaleTurns()
    {
        var bus = new EventBus();
        var turns = bus.TrackTurns();
        var shown = new List<string>();
        bus.Subscribe<ReplyChunk>(e => { if (turns.IsCurrent(e.TurnId)) shown.Add(e.Text); });

        bus.Publish(new TurnStarted(1));
        bus.Publish(new ReplyChunk(1, "first "));
        bus.Publish(new TurnStarted(2));           // interrupted, next message
        bus.Publish(new ReplyChunk(1, "late chunk from the old reply"));
        bus.Publish(new ReplyChunk(2, "second"));
        Assert.Equal(new[] { "first ", "second" }, shown);
    }
}

public class QuotaTests
{
    [Fact]
    public void CountsPerModelAndResetsWhenTheDateChanges()
    {
        var day = new DateTime(2026, 11, 17, 10, 0, 0);
        var q = new Quota(null, () => day);
        q.Record("claude-haiku-4-5-20251001");
        q.Record("haiku");
        q.Record("claude-sonnet-4-6");
        Assert.Equal(2, q.CountToday("haiku"));
        Assert.Equal(1, q.CountToday("sonnet"));
        Assert.Equal(3, q.TotalToday());
        day = day.AddDays(1);
        Assert.Equal(0, q.TotalToday());
    }

    [Fact]
    public void PersistsToFileWithVersionAndSurvivesCorruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cq-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "quota.json");
        var day = new DateTime(2026, 11, 17);
        new Quota(path, () => day).Record("haiku");
        Assert.Contains("\"version\": 1", File.ReadAllText(path));
        Assert.Equal(1, new Quota(path, () => day).CountToday("haiku"));

        File.WriteAllText(path, "{ not json");
        Assert.Equal(0, new Quota(path, () => day).TotalToday());
        Assert.True(File.Exists(Path.Combine(dir, "quota.unreadable.json")));
        Directory.Delete(dir, true);
    }
}

public class HistoryTests
{
    [Fact]
    public void InterruptedReplyIsSavedWithTheMark()
    {
        var h = new History(null);
        h.AppendTurn("tell me a story", "Once upon", interrupted: true);
        h.AppendTurn("hi", "", interrupted: true);
        Assert.Equal("Once upon [interrupted]", h.Messages[1].Text);
        Assert.Equal("[interrupted]", h.Messages[3].Text);
    }

    [Fact]
    public void RoundTripsThroughTheFileAndKeepsUnicode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ch-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "memory", "normal", "history.jsonl");
        new History(path).AppendTurn("påminn mig om mjölk", "Done, Philip. [happy]", false);
        var again = new History(path);
        Assert.Equal(2, again.Messages.Count);
        Assert.Equal("påminn mig om mjölk", again.Messages[0].Text);
        Assert.Contains("\"version\":1", File.ReadAllText(path));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void UnreadableLinesAreSkippedAndTheFileIsCopied()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "history.jsonl");
        File.WriteAllText(path, "garbage\n{\"version\":1,\"role\":\"user\",\"text\":\"hi\",\"time\":\"t\"}\n");
        Assert.Single(new History(path).Messages);
        Assert.True(File.Exists(Path.Combine(dir, "history.unreadable.jsonl")));
        Directory.Delete(dir, true);
    }
}

public class LogTests
{
    [Fact]
    public void KeyLikeStringsAreRedacted()
    {
        Assert.Equal("key=***", "key=" + Log.Redact("sk-ant-api03-abcdefghijklmnop"));
        Assert.Equal("***", Log.Redact(new string('a', 40)));
        Assert.Equal("first_word", Log.Redact("first_word"));
    }
}
