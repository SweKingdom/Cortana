using System.Collections.Concurrent;
using System.IO;
using Companion.Brain;
using Companion.Core;
using Companion.Memory;
using Xunit;

namespace Companion.Tests;

public class ReplyCleanerToolTagTests
{
    private static ReplyCleaner Run(params string[] deltas)
    {
        var c = new ReplyCleaner();
        foreach (var d in deltas) c.Push(d);
        c.Flush();
        return c;
    }

    [Fact]
    public void ACompleteTagIsCollected()
    {
        var c = Run("Noted. <tool name=\"remember\">{\"kind\":\"fact\",\"text\":\"x\"}</tool> [happy]");
        Assert.Equal(" name=\"remember\">{\"kind\":\"fact\",\"text\":\"x\"}", Assert.Single(c.ToolTags));
        Assert.Equal("happy", c.Mood);
    }

    [Fact]
    public void ATagSplitAcrossChunksIsCollectedWhole()
    {
        var c = Run("Noted. <to", "ol name=\"reme", "mber\">{\"kind\":\"fact\",\"te", "xt\":\"x\"}</to", "ol> [hap", "py]");
        var raw = Assert.Single(c.ToolTags);
        Assert.True(ToolTagParser.TryParseRemember(raw, out var call, out var error), error);
        Assert.Equal("x", call.Text);
    }

    [Fact]
    public void TwoTagsAreBothCollectedAndTheTextBetweenStaysVisible()
    {
        var shown = new ReplyCleaner();
        var text = shown.Push("A <tool name=\"remember\">{}</tool> and B <tool name=\"remember\">{}</tool>") + shown.Flush();
        Assert.Equal(2, shown.ToolTags.Count);
        Assert.Equal("A and B", text);
    }

    [Fact]
    public void AnUnclosedTagIsNotCollected()
    {
        var c = Run("Hi <tool name=\"remember\">{\"kind\":");
        Assert.Empty(c.ToolTags);
        Assert.True(c.UnclosedTag);
    }
}

public class HistoryFoldTests
{
    private static History Filled(string? path, int turns)
    {
        var h = new History(path);
        for (int i = 0; i < turns; i++) h.AppendTurn($"q{i}", $"a{i}", interrupted: false);
        return h;
    }

    [Fact]
    public void FoldOldestRemovesTheFirstMessages()
    {
        var h = Filled(null, 16);   // 32 messages
        h.FoldOldest(12);
        Assert.Equal(20, h.Count);
        Assert.Equal("q6", h.Messages[0].Text);
        Assert.Equal("a15", h.Messages[^1].Text);
    }

    [Fact]
    public void FoldOldestRewritesTheFileAndItReloads()
    {
        var path = Path.Combine(Path.GetTempPath(), "companion-test-" + Guid.NewGuid().ToString("N"), "history.jsonl");
        var h = Filled(path, 16);
        h.FoldOldest(12);
        Assert.Equal(20, File.ReadAllLines(path).Count(l => l.Length > 0));
        var again = new History(path);
        Assert.Equal(20, again.Count);
        Assert.Equal("q6", again.Messages[0].Text);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void FoldingNothingOrTooMuchIsSafe()
    {
        var h = Filled(null, 2);
        h.FoldOldest(0);
        Assert.Equal(4, h.Count);
        h.FoldOldest(99);
        Assert.Equal(0, h.Count);
    }
}

public class Step3ConversationTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 14, 32, 0);

    private sealed class Rig
    {
        public readonly EventBus Bus = new();
        public readonly FakeBrain Brain = new();
        public readonly History History = new(null);
        public readonly Quota Quota = new(null, () => Now);
        public readonly MemoryStore Memory = new(null, () => Now);
        public readonly ConcurrentQueue<object> Events = new();
        public readonly Conversation Conv;

        public Rig(int summaryDelayMs = 10, bool withMemory = true)
        {
            var prompts = new PromptBuilder(new PersonalitySource(null), () => 4, withMemory ? Memory : null);
            var summarizer = new Summarizer(Brain, History, Memory, Quota);
            Conv = new Conversation(Bus, Brain, History, Quota, "haiku", null, () => Now, 5000, prompts,
                withMemory ? Memory : null, summarizer, summaryDelayMs);
            Bus.Subscribe<ReplyDone>(Events.Enqueue); Bus.Subscribe<MemoryProposal>(Events.Enqueue); Bus.Subscribe<MemoryFull>(Events.Enqueue);
            Bus.Subscribe<ReplyChunk>(Events.Enqueue);
        }
        public IEnumerable<T> Of<T>() => Events.OfType<T>();
        public void Say(params string[] chunks)
            => Brain.Script = (_, ct) => FakeBrain.Steps(ct, chunks.Select(c => (0, (BrainChunk?)new TextChunk(c))).Append((0, (BrainChunk?)FakeBrain.Done())).ToArray());
    }

    [Fact]
    public async Task ReplyDoneCarriesTheMoodTag()
    {
        var r = new Rig();
        r.Say("Cute. A little needy. ", "[flirt", "y]");
        await r.Conv.SendAsync("what do you think of chatgpt");
        var done = Assert.Single(r.Of<ReplyDone>());
        Assert.Equal(Mood.Flirty, done.Mood);
        Assert.Equal("Cute. A little needy.", done.Text);
    }

    [Fact]
    public async Task AMissingOrIdleTagGivesNoExpression()
    {
        var r = new Rig();
        r.Say("No tag here.");
        await r.Conv.SendAsync("a");
        r.Say("Calm. [idle]");
        await r.Conv.SendAsync("b");
        var dones = r.Of<ReplyDone>().ToList();
        Assert.Null(dones[0].Mood);
        Assert.Equal(Mood.Idle, dones[1].Mood);   // the controller maps Idle to "no expression"
    }

    [Fact]
    public async Task AnInterruptedReplyHasNoMood()
    {
        var r = new Rig();
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("Hel")), (5000, new TextChunk("lo [happy]")), (0, FakeBrain.Done()));
        var t = r.Conv.SendAsync("hi");
        await Task.Delay(100);
        r.Conv.Interrupt();
        await t;
        Assert.Null(Assert.Single(r.Of<ReplyDone>()).Mood);
    }

    [Fact]
    public async Task ARememberTagSavesANoteAndPublishesAProposal()
    {
        var r = new Rig();
        r.Say("Noted: mornings are the enemy. ", "<tool name=\"remember\">{\"kind\":\"fact\",\"text\":\"Philip hates mornings\"}</tool>", " [happy]");
        await r.Conv.SendAsync("I hate mornings, by the way");
        var p = Assert.Single(r.Of<MemoryProposal>());
        Assert.Equal("f1", p.Entry.Id);
        Assert.Equal("Philip hates mornings", p.Entry.Text);
        Assert.Contains("Philip hates mornings", r.Memory.Read(MemoryStore.FactsFile));
        var done = Assert.Single(r.Of<ReplyDone>());
        Assert.Equal("Noted: mornings are the enemy.", done.Text);   // the tag is never shown or saved to history
        Assert.DoesNotContain("<tool", r.History.Messages[1].Text);
        Assert.Equal(Mood.Happy, done.Mood);
    }

    [Fact]
    public async Task TheProposalComesBeforeReplyDone()
    {
        var r = new Rig();
        r.Say("Noted.<tool name=\"remember\">{\"kind\":\"fact\",\"text\":\"x\"}</tool> [happy]");
        await r.Conv.SendAsync("x");
        var order = r.Events.Where(e => e is MemoryProposal or ReplyDone).Select(e => e.GetType()).ToList();
        Assert.Equal(new[] { typeof(MemoryProposal), typeof(ReplyDone) }, order);
    }

    [Fact]
    public async Task ABadTagSavesNothing()
    {
        var r = new Rig();
        r.Say("Sure. <tool name=\"remember\">{\"kind\":\"fact\",\"text\":\"x\",\"oops\":1}</tool> [happy]");
        await r.Conv.SendAsync("x");
        Assert.Empty(r.Of<MemoryProposal>());
        Assert.Equal("", r.Memory.Read(MemoryStore.FactsFile));
        Assert.Equal("Sure.", Assert.Single(r.Of<ReplyDone>()).Text);
    }

    [Fact]
    public async Task AnUnknownToolIsDroppedNotRun()
    {
        var r = new Rig();
        r.Say("Done. <tool name=\"set_reminder\">{\"text\":\"call mom\",\"when\":\"2026-11-17T18:00\"}</tool> [happy]");
        await r.Conv.SendAsync("remind me");
        Assert.Empty(r.Of<MemoryProposal>());
    }

    [Fact]
    public async Task AtTheCapShePublishesMemoryFullInsteadOfSavingSilently()
    {
        var r = new Rig();
        for (int i = 0; i < 2000 && r.Memory.Add(MemoryKind.Fact, $"n{i}").Status == AddStatus.Added; i++) { }   // short notes until nothing more fits
        r.Say("Noted. <tool name=\"remember\">{\"kind\":\"fact\",\"text\":\"one more thing\"}</tool> [happy]");
        await r.Conv.SendAsync("remember this");
        Assert.Empty(r.Of<MemoryProposal>());
        var full = Assert.Single(r.Of<MemoryFull>());
        Assert.Equal(MemoryKind.Fact, full.Kind);
        Assert.DoesNotContain("one more thing", r.Memory.Read(MemoryStore.FactsFile));
    }

    [Fact]
    public async Task SavedMemoryReachesTheNextPrompt()
    {
        var r = new Rig();
        r.Say("Noted. <tool name=\"remember\">{\"kind\":\"fact\",\"text\":\"Philip has a cat called Misty\"}</tool> [happy]");
        await r.Conv.SendAsync("I have a cat called Misty");
        r.Say("Of course you do. [teasing]");
        await r.Conv.SendAsync("what is my cat called?");
        Assert.Contains("- [f1] Philip has a cat called Misty", r.Brain.Calls[1][0].Text);
        Assert.DoesNotContain("Misty", r.Brain.Calls[0][0].Text);
    }

    [Fact]
    public async Task NoSummaryUntilTheHistoryIsPastThirtyMessages()
    {
        var r = new Rig();
        r.Say("ok [idle]");
        for (int i = 0; i < 15; i++) await r.Conv.SendAsync("m" + i);   // 30 messages
        Assert.Equal(30, r.History.Count);
        Assert.Null(r.Conv.SummaryTask);
    }

    [Fact]
    public async Task PastThirtyMessagesTheOldestAreFoldedIntoTheSummary()
    {
        var r = new Rig(summaryDelayMs: 10);
        r.Say("ok [idle]");
        for (int i = 0; i < 16; i++) await r.Conv.SendAsync("m" + i);   // 32 messages
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("They chatted about nothing much.")), (0, FakeBrain.Done()));
        var task = r.Conv.SummaryTask;
        Assert.NotNull(task);
        var result = await task!;
        Assert.NotNull(result);
        Assert.Equal(12, result!.Folded);
        Assert.Equal(20, r.History.Count);
        Assert.Equal("m6", r.History.Messages[0].Text);
        Assert.Equal("They chatted about nothing much.", r.Memory.ReadSummary());
        // The summary call was counted, and it asked for a summary of what happened, not for a persona.
        Assert.Equal(17, r.Quota.CountToday("haiku"));
        var call = r.Brain.Calls[^1];
        Assert.Equal(Summarizer.Instructions, call[0].Text);
        Assert.Contains("m0", call[1].Text);
        Assert.DoesNotContain("m6", call[1].Text);   // only the folded messages are in it
    }

    [Fact]
    public async Task TheSummaryReachesTheNextPromptAndItIsNotAboutWhoSheIs()
    {
        var r = new Rig(summaryDelayMs: 10);
        r.Say("ok [idle]");
        for (int i = 0; i < 16; i++) await r.Conv.SendAsync("m" + i);
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("They fixed a bug.")), (0, FakeBrain.Done()));
        await r.Conv.SummaryTask!;
        r.Say("ok [idle]");
        await r.Conv.SendAsync("next");
        Assert.Contains("They fixed a bug.", r.Brain.Calls[^1][0].Text);
        Assert.Contains("not who you are", r.Brain.Calls[^1][0].Text);
        Assert.Contains("never describe Cortana's personality", Summarizer.Instructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ANewMessageCancelsTheSummaryAndNothingChanges()
    {
        var r = new Rig(summaryDelayMs: 300);
        r.Say("ok [idle]");
        for (int i = 0; i < 16; i++) await r.Conv.SendAsync("m" + i);
        var task = r.Conv.SummaryTask!;
        await r.Conv.SendAsync("typing again");   // starts a turn: the pending job is cancelled
        Assert.Null(await task);
        Assert.Equal(34, r.History.Count);
        Assert.Equal("", r.Memory.ReadSummary());
    }

    [Fact]
    public async Task OpeningTheInputCancelsTheSummary()
    {
        var r = new Rig(summaryDelayMs: 300);
        r.Say("ok [idle]");
        for (int i = 0; i < 16; i++) await r.Conv.SendAsync("m" + i);
        var task = r.Conv.SummaryTask!;
        r.Conv.PreWarm();
        Assert.Null(await task);
        Assert.Equal(32, r.History.Count);
    }

    [Fact]
    public async Task AFailedSummaryChangesNothingAndIsRetriedAfterTheNextReply()
    {
        var r = new Rig(summaryDelayMs: 10);
        r.Say("ok [idle]");
        for (int i = 0; i < 16; i++) await r.Conv.SendAsync("m" + i);
        r.Brain.Script = (_, _) => throw new BrainException(FailKind.Offline, "no net");
        Assert.Null(await r.Conv.SummaryTask!);
        Assert.Equal(32, r.History.Count);
        Assert.Equal("", r.Memory.ReadSummary());
        Assert.True(new Summarizer(r.Brain, r.History, r.Memory, r.Quota).Due);   // still due: tried again later
    }

    [Fact]
    public async Task TheFirstKeystrokeCancelsTheSummaryAndAnEmptyBoxBringsItBack()
    {
        var r = new Rig(summaryDelayMs: 200);
        r.Say("ok [idle]");
        for (int i = 0; i < 16; i++) await r.Conv.SendAsync("m" + i);
        var first = r.Conv.SummaryTask!;
        r.Conv.SetDraft(true);                 // Philip starts typing
        Assert.Null(await first);
        Assert.Equal(32, r.History.Count);
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("They chatted.")), (0, FakeBrain.Done()));
        r.Conv.SetDraft(false);                // the box is empty again (deleted, or Esc): 15 s of quiet starts over
        var second = r.Conv.SummaryTask!;
        Assert.NotSame(first, second);
        var result = await second;
        Assert.NotNull(result);
        Assert.Equal(20, r.History.Count);
    }

    [Fact]
    public async Task ADraftKeptInAHiddenBoxStillBlocksTheSummary()
    {
        var r = new Rig(summaryDelayMs: 50);
        r.Say("ok [idle]");
        for (int i = 0; i < 15; i++) await r.Conv.SendAsync("m" + i);   // 30 messages, not yet due
        r.Conv.SetDraft(true);
        await r.Conv.SendAsync("one more");   // sending clears the draft state; this reply makes it due
        r.Conv.SetDraft(true);                // typed again, box then hidden with the draft kept
        Assert.Null(await r.Conv.SummaryTask!);
        Assert.Equal(32, r.History.Count);
    }

    [Fact]
    public async Task SendingATypedMessageClearsTheDraftSoTheSummaryCanRun()
    {
        var r = new Rig(summaryDelayMs: 10);
        r.Say("ok [idle]");
        r.Conv.SetDraft(true);
        for (int i = 0; i < 16; i++) await r.Conv.SendAsync("m" + i);
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("They chatted.")), (0, FakeBrain.Done()));
        Assert.NotNull(await r.Conv.SummaryTask!);
    }
}
