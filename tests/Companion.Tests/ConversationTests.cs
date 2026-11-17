using System.Collections.Concurrent;
using Companion.Brain;
using Companion.Core;
using Xunit;

namespace Companion.Tests;

internal sealed class FakeBrain : IBrain
{
    public readonly List<IReadOnlyList<ChatMessage>> Calls = new();
    public Func<int, CancellationToken, IAsyncEnumerable<BrainChunk>> Script = (_, _) => Steps(default);
    public string Name => "Fake";
    public bool SupportsVision => false;
    public bool SupportsTools => false;
    public IAsyncEnumerable<BrainChunk> StreamAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, CancellationToken ct)
    {
        Calls.Add(messages);
        return Script(Calls.Count, ct);
    }
    public void Dispose() { }

    public static async IAsyncEnumerable<BrainChunk> Steps([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, params (int delayMs, BrainChunk? chunk)[] steps)
    {
        foreach (var (delay, chunk) in steps)
        {
            if (delay > 0) await Task.Delay(delay, ct);
            if (chunk != null) yield return chunk;
        }
    }

    public static DoneChunk Done(string model = "claude-haiku-4-5", string block = "text") => new(new BrainUsage(model, 10, 100, 20, block));
}

public class ConversationTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 14, 32, 0);

    private sealed class Rig
    {
        public readonly EventBus Bus = new();
        public readonly FakeBrain Brain = new();
        public readonly History History = new(null);
        public readonly Quota Quota = new(null, () => Now);
        public readonly ConcurrentQueue<object> Events = new();
        public Conversation Conv = null!;

        public Rig(int noWordMs = 5000)
        {
            Conv = new Conversation(Bus, Brain, History, Quota, "haiku", new Fillers(new[] { "F1", "F2" }, new Random(3)), () => Now, noWordMs);
            Bus.Subscribe<TurnStarted>(Events.Enqueue); Bus.Subscribe<ThinkingStarted>(Events.Enqueue); Bus.Subscribe<Filler>(Events.Enqueue);
            Bus.Subscribe<ReplyChunk>(Events.Enqueue); Bus.Subscribe<ReplyDone>(Events.Enqueue); Bus.Subscribe<TurnFailed>(Events.Enqueue);
        }
        public IEnumerable<T> Of<T>() => Events.OfType<T>();
    }

    [Fact]
    public async Task FastReplyShowsNoFillerAndIsCleanedAndSaved()
    {
        var r = new Rig(noWordMs: 400);
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("Hello, ")), (0, new TextChunk("Philip. [hap")), (0, new TextChunk("py]")), (0, FakeBrain.Done()));
        await r.Conv.SendAsync("morning");
        Assert.Empty(r.Of<Filler>());
        Assert.Empty(r.Of<ThinkingStarted>());
        Assert.Equal("Hello, Philip.", string.Concat(r.Of<ReplyChunk>().Select(c => c.Text)));
        var done = Assert.Single(r.Of<ReplyDone>());
        Assert.Equal("Hello, Philip.", done.Text);
        Assert.False(done.Interrupted);
        Assert.Equal(new[] { "morning", "Hello, Philip." }, r.History.Messages.Select(m => m.Text));
        Assert.Equal(1, r.Quota.CountToday("haiku"));
    }

    [Fact]
    public async Task SlowReplyShowsOneFillerThatIsNeverSavedToHistory()
    {
        var r = new Rig(noWordMs: 50);
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (400, new TextChunk("Sorry, got lost. [teasing]")), (0, FakeBrain.Done()));
        await r.Conv.SendAsync("hello");
        var filler = Assert.Single(r.Of<Filler>());
        Assert.Contains(filler.Text, new[] { "F1", "F2" });
        Assert.Single(r.Of<ThinkingStarted>());
        // order: filler first, then the real reply
        var order = r.Events.Select(e => e.GetType().Name).ToList();
        Assert.True(order.IndexOf(nameof(Filler)) < order.IndexOf(nameof(ReplyChunk)));
        Assert.DoesNotContain(r.History.Messages, m => m.Text.Contains("F1") || m.Text.Contains("F2"));
        Assert.Equal("Sorry, got lost.", r.History.Messages[1].Text);
    }

    [Fact]
    public async Task ThinkingBlockAloneDoesNotTriggerAFiller()
    {
        var r = new Rig(noWordMs: 600);
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new ThinkingChunk()), (200, new TextChunk("Answer.")), (0, FakeBrain.Done(block: "thinking")));
        await r.Conv.SendAsync("hard one");
        await Task.Delay(700); // the 600 ms timer must have been cancelled by the first word
        Assert.Empty(r.Of<Filler>());
        Assert.Empty(r.Of<ThinkingStarted>());
    }

    [Fact]
    public async Task SlowReplyWithAThinkingBlockStillGetsTheNoWordFiller()
    {
        var r = new Rig(noWordMs: 100);
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new ThinkingChunk()), (500, new TextChunk("Answer.")), (0, FakeBrain.Done(block: "thinking")));
        await r.Conv.SendAsync("hard one");
        Assert.Single(r.Of<Filler>());
    }

    [Fact]
    public async Task InterruptSavesThePartialReplyWithTheMark()
    {
        var r = new Rig();
        var firstChunk = new TaskCompletionSource();
        r.Bus.Subscribe<ReplyChunk>(_ => firstChunk.TrySetResult());
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("Once upon ")), (10_000, new TextChunk("never shown")));
        var send = r.Conv.SendAsync("tell me a story");
        await firstChunk.Task;
        Assert.True(r.Conv.IsBusy);
        r.Conv.Interrupt();
        await send;
        Assert.False(r.Conv.IsBusy);
        var done = Assert.Single(r.Of<ReplyDone>());
        Assert.True(done.Interrupted);
        Assert.Equal("Once upon [interrupted]", r.History.Messages[1].Text);
    }

    [Fact]
    public async Task InterruptSavesOnlyTheTextThatWasShownPlusTheMark()
    {
        var r = new Rig();
        var firstChunk = new TaskCompletionSource();
        r.Bus.Subscribe<ReplyChunk>(_ => firstChunk.TrySetResult());
        // the brain produced "Once upon a time, ", the bubble had only revealed "Once up"
        r.Conv.ShownText = turn => turn == 1 ? "Once up" : "";
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("Once upon a time, ")), (10_000, new TextChunk("never")));
        var send = r.Conv.SendAsync("tell me a story");
        await firstChunk.Task;
        r.Conv.Interrupt();
        await send;
        Assert.Equal("Once up [interrupted]", r.History.Messages[1].Text);
        Assert.DoesNotContain(r.History.Messages, m => m.Text.Contains("upon a time"));
    }

    [Fact]
    public async Task InterruptBeforeAnythingWasShownSavesOnlyTheMark()
    {
        var r = new Rig();
        var firstChunk = new TaskCompletionSource();
        r.Bus.Subscribe<ReplyChunk>(_ => firstChunk.TrySetResult());
        r.Conv.ShownText = _ => "";
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("Secret words that nobody saw. ")), (10_000, new TextChunk("x")));
        var send = r.Conv.SendAsync("hello");
        await firstChunk.Task;
        r.Conv.Interrupt();
        await send;
        Assert.Equal("[interrupted]", r.History.Messages[1].Text);
    }

    [Fact]
    public async Task TheNextTranscriptAfterAnInterruptHasOnlyTheShownText()
    {
        var r = new Rig();
        var firstChunk = new TaskCompletionSource();
        r.Bus.Subscribe<ReplyChunk>(_ => firstChunk.TrySetResult());
        r.Conv.ShownText = turn => turn == 1 ? "Long" : "";
        r.Brain.Script = (n, ct) => n == 1
            ? FakeBrain.Steps(ct, (0, new TextChunk("Long answer that goes on ")), (10_000, new TextChunk("x")))
            : FakeBrain.Steps(ct, (0, new TextChunk("Sure. [idle]")), (0, FakeBrain.Done()));
        var first = r.Conv.SendAsync("one");
        await firstChunk.Task;
        var second = r.Conv.SendAsync("two");
        await Task.WhenAll(first, second);
        Assert.Contains(r.Brain.Calls[1], m => m.Role == Role.Assistant && m.Text == "Long [interrupted]");
        Assert.DoesNotContain(r.Brain.Calls[1], m => m.Text.Contains("goes on"));
    }

    private static async Task<Rig> FinishedReply()
    {
        var r = new Rig();
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("Once upon a time, there was a PC.")), (0, FakeBrain.Done()));
        await r.Conv.SendAsync("story");
        Assert.Equal("Once upon a time, there was a PC.", r.History.Messages[1].Text);
        return r;
    }

    [Fact]
    public async Task ACompletedReplyCutShortOnScreenIsTrimmedInTheHistory()
    {
        var r = await FinishedReply();
        r.Conv.TrimLastReply(1, "Once upon");
        Assert.Equal("Once upon [interrupted]", r.History.Messages[1].Text);
        Assert.Equal(2, r.History.Messages.Count);
    }

    [Fact]
    public async Task TrimmingWithNothingShownLeavesOnlyTheMark()
    {
        var r = await FinishedReply();
        r.Conv.TrimLastReply(1, "");
        Assert.Equal("[interrupted]", r.History.Messages[1].Text);
    }

    [Fact]
    public async Task TrimmingDoesNothingWhenAllWasShownOrForAnotherTurn()
    {
        var r = await FinishedReply();
        r.Conv.TrimLastReply(1, "Once upon a time, there was a PC.");
        Assert.Equal("Once upon a time, there was a PC.", r.History.Messages[1].Text);
        r.Conv.TrimLastReply(7, "Once");
        Assert.Equal("Once upon a time, there was a PC.", r.History.Messages[1].Text);
    }

    [Fact]
    public async Task TrimmingTwiceKeepsTheFirstCut()
    {
        var r = await FinishedReply();
        r.Conv.TrimLastReply(1, "Once upon a");
        r.Conv.TrimLastReply(1, "Once");
        Assert.Equal("Once upon a [interrupted]", r.History.Messages[1].Text);
    }

    [Fact]
    public async Task TrimmingNeverTouchesAnInterruptedEntry()
    {
        var r = new Rig();
        var firstChunk = new TaskCompletionSource();
        r.Bus.Subscribe<ReplyChunk>(_ => firstChunk.TrySetResult());
        r.Conv.ShownText = _ => "Once up";
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("Once upon a time, ")), (10_000, new TextChunk("x")));
        var send = r.Conv.SendAsync("story");
        await firstChunk.Task;
        r.Conv.Interrupt();
        await send;
        r.Conv.TrimLastReply(1, "Once");
        Assert.Equal("Once up [interrupted]", r.History.Messages[1].Text);
    }

    [Fact]
    public async Task ANewMessageInterruptsAndTheNextTranscriptKnowsAboutIt()
    {
        var r = new Rig();
        var firstChunk = new TaskCompletionSource();
        r.Bus.Subscribe<ReplyChunk>(_ => firstChunk.TrySetResult());
        r.Brain.Script = (n, ct) => n == 1
            ? FakeBrain.Steps(ct, (0, new TextChunk("Long answer ")), (10_000, new TextChunk("x")))
            : FakeBrain.Steps(ct, (0, new TextChunk("Sure. [idle]")), (0, FakeBrain.Done()));
        var first = r.Conv.SendAsync("one");
        await firstChunk.Task;
        var second = r.Conv.SendAsync("two");
        await Task.WhenAll(first, second);

        Assert.Equal(2, r.Brain.Calls.Count);
        Assert.Contains(r.Brain.Calls[1], m => m.Role == Role.Assistant && m.Text == "Long answer [interrupted]");
        Assert.Equal("two", r.Brain.Calls[1][^1].Text);
        // TurnIds: the old reply's done event carries the old id, so listeners drop it
        var ids = r.Of<ReplyDone>().Select(d => d.TurnId).ToList();
        Assert.Equal(new[] { 1, 2 }, ids);
        Assert.Equal(2, r.Conv.CurrentTurnId);
    }

    [Fact]
    public async Task BrainFailureIsReportedAndNothingIsSaved()
    {
        var r = new Rig();
        r.Brain.Script = (_, _) => throw new BrainException(FailKind.Offline, "no net");
        await r.Conv.SendAsync("hello");
        var failed = Assert.Single(r.Of<TurnFailed>());
        Assert.Equal(FailKind.Offline, failed.Kind);
        Assert.Empty(r.History.Messages);
        Assert.Equal(0, r.Quota.TotalToday());
        Assert.Empty(r.Of<ReplyDone>());
    }

    [Fact]
    public async Task ContextLineAndSystemPromptGoToTheBrain()
    {
        var r = new Rig();
        r.Brain.Script = (_, ct) => FakeBrain.Steps(ct, (0, new TextChunk("ok")), (0, FakeBrain.Done()));
        await r.Conv.SendAsync("hi");
        var call = r.Brain.Calls[0];
        Assert.Equal(Role.System, call[0].Role);
        Assert.Contains("Cortana", call[0].Text);
        Assert.Equal("Now: Tuesday 2026-11-17 14:32 (local time).", call[1].Text);
    }
}
