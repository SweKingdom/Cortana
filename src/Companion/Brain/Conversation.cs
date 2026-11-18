using System.Diagnostics;
using System.IO;
using System.Text;
using Companion.Core;
using Companion.Memory;

namespace Companion.Brain;

/// <summary>
/// One conversation: the only module that calls the brain (architecture). Runs a turn at a time, tags every
/// reply event with a TurnId (D-038), owns the history (D-055), shows thinking fillers (D-058), saves the notes the
/// model proposes with the `remember` tag (D-057) and folds old history into the summary when idle.
/// Must be used from the UI thread; the turn continues on that thread after each await.
/// </summary>
internal sealed class Conversation
{
    private readonly EventBus _bus;
    private readonly IBrain _brain;
    private readonly History _history;
    private readonly Quota _quota;
    private readonly Fillers _fillers;
    private readonly string _quotaModel;
    private readonly Func<DateTime> _now;
    private readonly int _noWordMs;
    private readonly PromptBuilder _prompts;
    private readonly MemoryStore? _memory;
    private readonly Summarizer? _summarizer;
    private readonly int _summaryDelayMs;

    private int _turnId, _ticket;
    private bool _hasDraft;       // the chat input holds unsent text (open, or hidden with the draft kept): no summary (D-066, refined)
    private CancellationTokenSource? _summaryCts;
    private CancellationTokenSource? _cts;
    private Task _running = Task.CompletedTask;

    public Conversation(EventBus bus, IBrain brain, History history, Quota quota, string quotaModel,
        Fillers? fillers = null, Func<DateTime>? now = null,
        int noWordMs = FillerTuning.NoWordMs, PromptBuilder? prompts = null, MemoryStore? memory = null,
        Summarizer? summarizer = null, int summaryDelayMs = SummaryTuning.IdleDelayMs)
    {
        _bus = bus; _brain = brain; _history = history; _quota = quota; _quotaModel = quotaModel;
        _prompts = prompts ?? PromptBuilder.CreateDefault();
        _memory = memory; _summarizer = summarizer; _summaryDelayMs = summaryDelayMs;
        _fillers = fillers ?? new Fillers(() => _prompts.Personality.Fillers!);
        _now = now ?? (() => DateTime.Now);
        _noWordMs = noWordMs;

        bus.Subscribe<UserMessage>(m => _ = SendAsync(m.Text));
        bus.Subscribe<Interrupt>(_ => Interrupt());
        bus.Subscribe<PanicHide>(_ => { Interrupt(); CancelPreWarm(); });
    }

    public bool IsBusy => _cts != null;
    public int CurrentTurnId => _turnId;
    public History History => _history;
    public MemoryStore? Memory => _memory;
    /// <summary>The background summary job, if one was scheduled after the last reply (for the perfcheck probe and tests).</summary>
    public Task<SummaryResult?>? SummaryTask { get; private set; }
    /// <summary>Usage of the last finished turn (first block type, tokens); for the perfcheck probe and logs.</summary>
    public BrainUsage? LastUsage { get; private set; }

    /// <summary>
    /// Says what part of a turn's reply was actually shown to Philip. An interrupted reply is saved to history as
    /// only that part plus [interrupted] (D-063); unseen text is never saved. With no provider (tests, headless) the
    /// whole produced text counts as seen.
    /// </summary>
    public Func<int, string>? ShownText { get; set; }

    private int _savedTurn;               // the turn of the last history entry, and what was saved for it
    private string _savedReply = "";
    private bool _savedInterrupted;

    /// <summary>
    /// A reply finished and was saved in full, but Philip cut its display short (typing, a new message, panic) after
    /// only part of it had been shown. The history entry becomes the shown part plus [interrupted] (D-063).
    /// Does nothing for another turn, an entry that was already saved as interrupted, or a fully shown reply.
    /// </summary>
    public void TrimLastReply(int turn, string shown)
    {
        if (turn != _savedTurn || _savedInterrupted || shown.Length >= _savedReply.Length) return;
        _history.ReplaceLastReply(shown, interrupted: true);
        _savedInterrupted = true;
    }

    /// <summary>
    /// The input opened, Philip typed the first key after a send, or push-to-talk began: start the process now (D-055,
    /// D-071). Skipped while a reply runs; does nothing if a warm process already exists.
    /// </summary>
    public void PreWarm()
    {
        CancelSummary();   // the summary never runs next to a typing Philip
        if (_cts == null) _brain.PreWarm(_prompts.BuildSystem());
    }
    public void CancelPreWarm() => _brain.CancelPreWarm();

    /// <summary>
    /// Whether the chat input holds text (D-066 refined by D-071). The first keystroke cancels a waiting summary; when the
    /// box is empty again the summary is scheduled anew (15 s of quiet) if the history is past the cap.
    /// </summary>
    public void SetDraft(bool hasText)
    {
        if (_hasDraft == hasText) return;
        _hasDraft = hasText;
        if (hasText) CancelSummary();
        else if (!IsBusy) ScheduleSummary();
    }

    /// <summary>Stop the running reply: the process is killed (D-055) and the partial reply is saved as [interrupted].</summary>
    public void Interrupt()
    {
        CancelSummary();
        var c = _cts;
        if (c == null) return;
        _cts = null;
        c.Cancel();
    }

    /// <summary>Sends a message. A reply that is still running is interrupted first (and saved with its mark).</summary>
    public Task SendAsync(string text) => SendCoreAsync(text);

    private async Task SendCoreAsync(string text)
    {
        int ticket = ++_ticket;
        _hasDraft = false;
        var previous = _running;
        Interrupt();
        if (!previous.IsCompleted) await previous; // lets the interrupted turn write its history entry first
        if (ticket != _ticket) return;             // a newer message superseded this one

        var cts = new CancellationTokenSource();
        _cts = cts;
        int turn = ++_turnId;
        _bus.Publish(new TurnStarted(turn));
        var task = RunTurnAsync(turn, text, cts);
        _running = task;
        await task;
    }

    private async Task RunTurnAsync(int turn, string text, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        var sw = Stopwatch.StartNew();
        var messages = _prompts.Build(_history.Last(Transcript.MaxHistory), text, _now());
        var cleaner = new ReplyCleaner();
        var shown = new StringBuilder();
        bool gotText = false, filler = false, interrupted = false;
        BrainUsage? usage = null;
        FailKind? failed = null;
        using var fillerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Log.Event("turn_start", ("turn", turn));

        void ShowFiller()
        {
            if (gotText || filler || turn != _turnId || ct.IsCancellationRequested) return;
            filler = true;
            Log.Event("filler", ("turn", turn), ("ms", sw.ElapsedMilliseconds));
            _bus.Publish(new ThinkingStarted(turn));
            _bus.Publish(new Filler(turn, _fillers.Next()));
        }
        async Task ShowFillerAfter(int ms)
        {
            try { await Task.Delay(ms, fillerCts.Token); } catch (OperationCanceledException) { return; }
            ShowFiller();
        }
        bool sawThinking = false;
        int leakRetracted = 0;
        void Emit(string visible)
        {
            if (visible.Length == 0) return;
            if (!gotText)
            {
                gotText = true;
                fillerCts.Cancel();
                Log.Event("first_word", ("turn", turn), ("ms", sw.ElapsedMilliseconds));
            }
            shown.Append(visible);
            _bus.Publish(new ReplyChunk(turn, visible));
        }

        // Leaked planning (ReplyCleaner): the first paragraph is held at most LeadHoldMs, then shown. If it is judged a leak
        // after that, the part already shown is taken back (ReplyLeak) and left out of the history. Everything that touches
        // the cleaner or `shown` goes through `sync`, because the hold timer may run on another thread.
        var sync = new object();
        using var holdCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bool holdStarted = false;
        void Feed(string visible)
        {
            int taken = cleaner.TakeRetract();
            if (taken > 0)
            {
                shown.Remove(0, Math.Min(taken, shown.Length));
                leakRetracted += taken;
                _bus.Publish(new ReplyLeak(turn, taken));
            }
            Emit(visible);
        }
        async Task ReleaseAfterHold()
        {
            try { await Task.Delay(ReplyCleaner.LeadHoldMs, holdCts.Token); } catch (OperationCanceledException) { return; }
            lock (sync) { if (!ct.IsCancellationRequested) Feed(cleaner.ReleaseLead()); }
        }

        try
        {
            _ = ShowFillerAfter(_noWordMs);
            await foreach (var chunk in _brain.StreamAsync(messages, Array.Empty<ToolSpec>(), ct))
            {
                switch (chunk)
                {
                    case ThinkingChunk:   // logged only; a thinking block does not trigger a filler (D-058)
                        if (!sawThinking) { sawThinking = true; Log.Event("thinking_block", ("turn", turn), ("ms", sw.ElapsedMilliseconds)); }
                        break;
                    case TextChunk t:
                        lock (sync)
                        {
                            Feed(cleaner.Push(t.Text));
                            if (!holdStarted && cleaner.Holding) { holdStarted = true; _ = ReleaseAfterHold(); }
                        }
                        break;
                    case DoneChunk d: usage = d.Usage; break;
                }
            }
            lock (sync) Feed(cleaner.Flush());
            if (cleaner.UnclosedTag) Log.Event("cleaner_unclosed_tag", ("turn", turn));
            if (cleaner.LeakDropped != null) Log.Event("reply_leak_dropped", ("turn", turn), ("why", cleaner.LeakDropped), ("retracted", leakRetracted));
            if (cleaner.LeakKept != null) Log.Event("reply_leak_kept", ("turn", turn), ("why", cleaner.LeakKept));
        }
        catch (OperationCanceledException) { interrupted = true; }
        catch (BrainException ex) { failed = ex.Kind; Log.Event("turn_failed", ("turn", turn), ("kind", ex.Kind), ("why", ex.Message)); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            failed = FailKind.Other; Log.Event("turn_failed", ("turn", turn), ("kind", "Other"), ("why", ex.GetType().Name));
        }
        finally
        {
            fillerCts.Cancel();
            holdCts.Cancel();
            if (ReferenceEquals(_cts, cts)) _cts = null;
            cts.Dispose();
        }

        if (failed is { } kind)
        {
            _bus.Publish(new TurnFailed(turn, kind, ""));
            return;
        }

        LastUsage = usage;
        if (gotText || usage != null) _quota.Record(usage?.Model is { Length: > 0 } m ? m : _quotaModel); // a call was made
        var reply = shown.ToString();
        if (interrupted) _history.AppendTurn(text, ShownText?.Invoke(turn) ?? reply, interrupted: true);   // she knows she was cut off, and only knows what was shown
        else if (reply.Length > 0) _history.AppendTurn(text, reply, interrupted: false);
        if (interrupted || reply.Length > 0) { _savedTurn = turn; _savedReply = reply; _savedInterrupted = interrupted; }
        var mood = interrupted ? null : MoodTag.Parse(cleaner.Mood);
        if (!interrupted) RunTools(turn, cleaner);
        Log.Event(interrupted ? "turn_interrupted" : "turn_done", ("turn", turn), ("ms", sw.ElapsedMilliseconds),
            ("block", usage?.FirstBlock), ("out_tokens", usage?.OutputTokens), ("mood", interrupted ? "-" : cleaner.Mood ?? "none"));
        _bus.Publish(new ReplyDone(turn, reply, interrupted, mood));
        if (!interrupted) ScheduleSummary();
    }

    /// <summary>
    /// Step 3 knows one tool, `remember` (D-057 text protocol, strict parse). Anything else, or a tag that does not
    /// parse, is logged (reason code only, never the text) and dropped; the second-process repair of the full tool
    /// loop comes in step 5.
    /// </summary>
    private void RunTools(int turn, ReplyCleaner cleaner)
    {
        foreach (var raw in cleaner.ToolTags.Take(3))
        {
            if (!ToolTagParser.TryParseRemember(raw, out var call, out var error)) { Log.Event("tool_rejected", ("turn", turn), ("why", error)); continue; }
            if (_memory == null) { Log.Event("tool_ignored", ("turn", turn)); continue; }
            var r = _memory.Add(call.Kind, call.Text, call.Replaces);
            switch (r.Status)
            {
                case AddStatus.Added:
                    Log.Event("memory_added", ("turn", turn), ("id", r.Entry!.Id), ("kind", call.Kind));
                    _bus.Publish(new MemoryProposal(turn, r.Entry));
                    break;
                case AddStatus.Full:
                    Log.Event("memory_full", ("turn", turn), ("kind", call.Kind));
                    _bus.Publish(new MemoryFull(turn, call.Kind));
                    break;
                default: Log.Event("memory_not_added", ("turn", turn), ("why", r.Status)); break;
            }
        }
    }

    // ---- summary (step 3) -----------------------------------------------------------------------------------------

    private void CancelSummary()
    {
        var c = _summaryCts;
        _summaryCts = null;
        c?.Cancel();
    }

    /// <summary>After a reply: when the history is past the cap, fold it into the summary once the chat has been quiet for a while.</summary>
    private void ScheduleSummary()
    {
        if (_summarizer is not { Due: true }) return;
        CancelSummary();
        var cts = new CancellationTokenSource();
        _summaryCts = cts;
        SummaryTask = RunSummaryAsync(cts);
    }

    private async Task<SummaryResult?> RunSummaryAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_summaryDelayMs, cts.Token);
            if (cts.IsCancellationRequested || IsBusy || _hasDraft) return null;   // only when idle and the input box is empty
            return await _summarizer!.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { return null; }
        finally
        {
            if (ReferenceEquals(_summaryCts, cts)) _summaryCts = null;
            cts.Dispose();
        }
    }
}
