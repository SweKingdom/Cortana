using System.Diagnostics;
using System.IO;
using System.Text;
using Companion.Core;

namespace Companion.Brain;

/// <summary>
/// One conversation: the only module that calls the brain (architecture). Runs a turn at a time, tags every
/// reply event with a TurnId (D-038), owns the history (D-055) and shows thinking fillers (D-058).
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

    private int _turnId, _ticket;
    private CancellationTokenSource? _cts;
    private Task _running = Task.CompletedTask;

    public Conversation(EventBus bus, IBrain brain, History history, Quota quota, string quotaModel,
        Fillers? fillers = null, Func<DateTime>? now = null,
        int noWordMs = FillerTuning.NoWordMs)
    {
        _bus = bus; _brain = brain; _history = history; _quota = quota; _quotaModel = quotaModel;
        _fillers = fillers ?? new Fillers();
        _now = now ?? (() => DateTime.Now);
        _noWordMs = noWordMs;

        bus.Subscribe<UserMessage>(m => _ = SendAsync(m.Text));
        bus.Subscribe<Interrupt>(_ => Interrupt());
        bus.Subscribe<PanicHide>(_ => { Interrupt(); CancelPreWarm(); });
    }

    public bool IsBusy => _cts != null;
    public int CurrentTurnId => _turnId;
    public History History => _history;
    /// <summary>Usage of the last finished turn (first block type, tokens); for the perfcheck probe and logs.</summary>
    public BrainUsage? LastUsage { get; private set; }

    /// <summary>The input opened or push-to-talk began: start the process now (D-055). Skipped while a reply runs.</summary>
    public void PreWarm() { if (_cts == null) _brain.PreWarm(PromptBuilder.BuildSystem()); }
    public void CancelPreWarm() => _brain.CancelPreWarm();

    /// <summary>Stop the running reply: the process is killed (D-055) and the partial reply is saved as [interrupted].</summary>
    public void Interrupt()
    {
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
        var messages = PromptBuilder.Build(_history.Last(Transcript.MaxHistory), text, _now());
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
                    case TextChunk t: Emit(cleaner.Push(t.Text)); break;
                    case DoneChunk d: usage = d.Usage; break;
                }
            }
            Emit(cleaner.Flush());
            if (cleaner.UnclosedTag) Log.Event("cleaner_unclosed_tag", ("turn", turn));
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
        if (interrupted) _history.AppendTurn(text, reply, interrupted: true);        // she knows she was cut off
        else if (reply.Length > 0) _history.AppendTurn(text, reply, interrupted: false);
        Log.Event(interrupted ? "turn_interrupted" : "turn_done", ("turn", turn), ("ms", sw.ElapsedMilliseconds),
            ("block", usage?.FirstBlock), ("out_tokens", usage?.OutputTokens));
        _bus.Publish(new ReplyDone(turn, reply, interrupted));
    }
}
