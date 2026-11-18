using Companion.Brain;
using Companion.Core;
using Companion.Memory;
using System.Windows.Threading;

namespace Companion.Character;

/// <summary>
/// Connects the orb window, the chat input, the speech bubble and the conversation. Everything is created on
/// first use: the session (no process starts until the input opens), the input window and the bubble popup.
/// Listeners drop events whose TurnId is not the current one (D-038).
/// </summary>
internal sealed class ChatController : IDisposable
{
    private readonly CharacterWindow _window;
    private readonly EventBus _bus;
    private readonly bool _ephemeral;
    private readonly Settings? _settings;
    private readonly TurnTracker _turns;
    private readonly SpeechBubble _bubble;

    private readonly ReplyTypewriter _typewriter;

    private ModeSession? _session;
    private ChatInput? _input;
    private long _armedTick = InputRules.NotArmed;   // when the process was last pre-warmed; re-armed on a keystroke after 20 s (D-071)
    private readonly MoodHold _mood;
    private readonly ShownText _shownText = new();   // what the bubble has shown this turn; history gets this on interrupt (D-063)
    private int _replyChars;      // characters received from the brain this turn
    private bool _skipped;        // a click showed the rest: the remainder of this reply appears at once
    private bool _done;           // ReplyDone arrived
    private bool _fadeStarted;    // the bubble's countdown has been started for this reply
    private readonly List<(string Text, string? UndoId)> _notes = new();   // memory lines for this reply, shown under it once it is fully revealed
    private bool _notesShown;

    public ChatController(CharacterWindow window, EventBus bus, Settings? settings = null, bool ephemeral = false)
    {
        _window = window; _bus = bus; _settings = settings; _ephemeral = ephemeral;
        _turns = bus.TrackTurns(); // subscribed first, so Current is up to date before the handlers below run
        _bubble = new SpeechBubble(window.Orb);
        _typewriter = new ReplyTypewriter(OnReveal, OnDrained);
        _bubble.SkipReveal = SkipReveal;
        _mood = new MoodHold(TimeSpan.FromSeconds(OrbTuning.MoodHoldSeconds), Schedule,
            () => _window.FadeExpression(OrbTuning.MoodFadeSeconds, () => _mood!.Faded()));   // the colour is held between messages (D-070)

        window.OrbClicked += OnOrbClicked;
        bus.Subscribe<UserMessage>(_ => { CutReveal(); _bubble.Hide(); _mood.Chatting(); _window.StopExpressionFade(); });   // a new message ends a pinned bubble too
        bus.Subscribe<TurnStarted>(e =>
        {
            _typewriter.Reset();
            _shownText.Start(e.TurnId);
            _replyChars = 0; _skipped = false; _done = false; _fadeStarted = false;
            _notes.Clear(); _notesShown = false;
            _window.SetActivity(Activity.Idle);
        });
        bus.Subscribe<MemoryProposal>(e =>
        {
            if (!_turns.IsCurrent(e.TurnId)) return;
            _notes.Add(($"Remembered: {e.Entry.Text}", e.Entry.Id));
        });
        bus.Subscribe<MemoryFull>(e =>
        {
            if (!_turns.IsCurrent(e.TurnId) || _notes.Any(n => n.UndoId == null)) return;
            _notes.Add(("My memory is full. I need a tidy-up before I can save more.", null));
        });
        bus.Subscribe<ThinkingStarted>(e => { if (_turns.IsCurrent(e.TurnId)) _window.SetActivity(Activity.Thinking); });
        bus.Subscribe<Filler>(e => { if (_turns.IsCurrent(e.TurnId)) _bubble.SetText(e.Text, filler: true); });
        bus.Subscribe<ReplyChunk>(OnReplyChunk);
        bus.Subscribe<ReplyLeak>(OnReplyLeak);
        bus.Subscribe<ReplyDone>(OnReplyDone);
        bus.Subscribe<TurnFailed>(OnTurnFailed);
        bus.Subscribe<Interrupt>(_ => OnInterrupt());
        bus.Subscribe<PanicHide>(_ => { CutReveal(); _bubble.Hide(); _input?.Dismiss(); _window.SetActivity(Activity.Idle); });
    }

    internal EventBus Bus => _bus;
    internal ModeSession Session => _session ??= CreateSession();

    private ModeSession CreateSession()
    {
        var session = ModeSession.CreateNormal(_bus, _ephemeral, _settings);
        session.Conversation.ShownText = _shownText.For;   // an interrupted reply is saved as what was shown (D-063)
        return session;
    }

    private sealed class TimerHandle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    /// <summary>One-shot wait for <see cref="MoodHold"/>: a single dispatcher timer, no polling.</summary>
    private static IDisposable Schedule(TimeSpan after, Action action)
    {
        var timer = new DispatcherTimer { Interval = after };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
        return new TimerHandle(timer);
    }

    private void Arm()
    {
        Session.Conversation.PreWarm();
        _armedTick = Environment.TickCount64;
    }

    /// <summary>Hotkey: open the box, or hide it (keeping the draft) when it is already open (D-071).</summary>
    public void ToggleInput()
    {
        if (_input is { IsOpen: true }) _input.HideKeepingDraft();
        else OpenInput();
    }

    /// <summary>Hotkey or click: open the text box (and pre-warm the process). Typing does not interrupt a running reply (D-073).</summary>
    public void OpenInput()
    {
        if (_input is { IsOpen: true }) { _input.Activate(); return; }
        var session = Session;
        // A reply that is running or still being revealed, and a pinned one, stay on screen while the input is open
        // (typing interrupts, below). Only an old finished bubble that is just counting down makes way.
        if (!(IsReplying || (_bubble.IsPinned && _fadeStarted))) { _typewriter.Reset(); _bubble.Hide(); }
        Arm();

        if (_input == null)
        {
            var input = new ChatInput();
            _input = input;
            // Typing does not interrupt a running reply (D-073); it re-arms the pre-warm after a send or after it has
            // expired (D-071), unless a reply is running (it is armed when that reply ends, below).
            input.TextEdited += hasText =>
            {
                Session.Conversation.SetDraft(hasText);
                if (InputRules.ShouldArm(hasText, _session?.Conversation.IsBusy == true, Environment.TickCount64, _armedTick)) Arm();
            };
            // The box stays open after a send; the next keystroke is the first of a new message.
            input.Submitted += text => { _armedTick = InputRules.NotArmed; _bus.Publish(new UserMessage(text)); };
            input.Hidden += () =>
            {
                _armedTick = InputRules.NotArmed;
                var conv = _session?.Conversation;
                conv?.CancelPreWarm();
                conv?.SetDraft(input.HasText);   // a kept draft still counts as text; an Esc-cleared box does not
            };
        }
        _input.ShowNear(_window);
    }


    /// <summary>
    /// Perfcheck probe (COMPANION_PERF_PINNED): a pinned bubble with a held expression and no chat, to measure what a
    /// pinned expression costs at rest.
    /// </summary>
    internal void ProbePinned(Expression expression)
    {
        _bubble.SetText("Pinned bubble (perfcheck).", filler: false);
        _bubble.Pin();
        _window.SetExpression(expression);
    }

    /// <summary>Perfcheck probe: the first keystroke of a message (opens the box if needed; arms the pre-warm).</summary>
    internal void ProbeType(string text)
    {
        OpenInput();
        _input?.SetTextForProbe(text);
    }

    /// <summary>Perfcheck probe: presses Enter in the real input (the box stays open, as for Philip).</summary>
    internal void ProbeSubmit(string text)
    {
        if (_input is { IsOpen: true }) _input.SubmitForProbe(text); else _bus.Publish(new UserMessage(text));
    }

    private void OnOrbClicked()
    {
        if (_input is { IsOpen: true }) { _input.HideKeepingDraft(); return; }
        if (_input != null && Environment.TickCount64 - _input.LastDeactivatedHideTick < 400) return;   // the click that took focus already hid the box
        if (IsReplying) { _bus.Publish(new Interrupt()); return; } // click on a talking orb stops her
        OpenInput();
    }

    /// <summary>The typewriter is still revealing text (the perfcheck probe waits for this to end).</summary>
    internal bool Revealing => _typewriter.HasPending;

    /// <summary>The brain is still producing the reply, or the typewriter is still revealing it.</summary>
    private bool IsReplying => _session?.Conversation.IsBusy == true || _typewriter.HasPending;

    /// <summary>
    /// The reveal stops where it is. If the reply was already complete (and so already saved in full), the history
    /// is cut back to what was shown (D-063). A reply still running is handled by its own interrupt.
    /// </summary>
    private void CutReveal()
    {
        bool cut = _typewriter.HasPending;
        _typewriter.Reset();
        if (cut && _done) _session?.Conversation.TrimLastReply(_turns.Current, _shownText.For(_turns.Current));
    }

    private void OnReplyChunk(ReplyChunk e)
    {
        if (!_turns.IsCurrent(e.TurnId)) return;
        _replyChars += e.Text.Length;
        if (_skipped) { OnReveal(e.Text); return; }   // Philip clicked to see it all: the rest of this reply is not paced
        _typewriter.Push(e.Text);   // the first character shows at once and replaces the filler; the rest is paced (D-063)
    }

    /// <summary>
    /// What was sent so far turned out to be a note she wrote to herself. What the typewriter had not revealed yet is
    /// skipped; what it had revealed is cleared, so the real reply starts on an empty bubble. The history already left it out.
    /// </summary>
    private void OnReplyLeak(ReplyLeak e)
    {
        if (!_turns.IsCurrent(e.TurnId)) return;
        _replyChars = Math.Max(0, _replyChars - e.Chars);
        _typewriter.Reset();
        if (_shownText.Length > 0) { _shownText.Start(e.TurnId); _bubble.Hide(); }
    }

    /// <summary>A piece of the reply is due: the first one replaces the filler or the old reply.</summary>
    private void OnReveal(string text)
    {
        bool first = _shownText.Length == 0;
        if (first) _window.SetActivity(Activity.Talking);   // from the first shown character until the reply is all on screen
        _shownText.Add(text);
        if (first) _bubble.SetText(text, filler: false); else _bubble.Append(text);
    }

    /// <summary>Click on the bubble while text is still waiting: show it all now. False when nothing was waiting (the click pins instead).</summary>
    private bool SkipReveal()
    {
        if (!_typewriter.HasPending) return false;
        _skipped = true;
        _typewriter.Flush();
        return true;
    }

    /// <summary>Everything received so far is on screen. If the reply is complete, the bubble's countdown starts now.</summary>
    private void OnDrained()
    {
        if (_done) Finish();
    }

    /// <summary>The reply is complete and all of it is on screen: stop talking, show the memory lines, start the countdown.</summary>
    private void Finish()
    {
        _window.SetActivity(Activity.Idle);
        ShowNotes();
        StartCountdown(_replyChars + (_replyChars == 0 ? 40 : 0));   // a note-only bubble gets the normal minimum
    }

    private void ShowNotes()
    {
        if (_notesShown) return;
        _notesShown = true;
        var session = _session;
        foreach (var (text, id) in _notes)
            _bubble.AddNote(text, id == null || session == null ? null : () => session.Memory.Undo(id) != null ? "Forgotten." : "Already gone.");
    }

    private void StartCountdown(int chars)
    {
        if (_fadeStarted) return;
        _fadeStarted = true;
        _bubble.StartFade(chars);
    }

    private void OnReplyDone(ReplyDone e)
    {
        if (!_turns.IsCurrent(e.TurnId)) return;
        _done = true;
        var held = _mood.Reply(e.Mood, e.Interrupted);   // no tag, an interrupt or a failure leave the colour as it was (D-070)
        if (e.Interrupted) { _window.SetActivity(Activity.Idle); _notes.Clear(); StopWhereItIs(); return; }
        _window.SetExpression(held);   // colour now; its own motion plays once she stops talking
        // Philip kept typing while this reply ran (D-073): the pre-warm was skipped then, so start it now.
        if (_input is { IsOpen: true, HasText: true }) { _armedTick = InputRules.NotArmed; Arm(); }
        if (_replyChars == 0 && _notes.Count == 0) { _window.SetActivity(Activity.Idle); _bubble.Hide(); return; }   // nothing to read (or a filler left over)
        if (!_typewriter.HasPending) Finish();   // otherwise OnDrained does it when the reveal ends
    }

    private void OnInterrupt()
    {
        _window.SetActivity(Activity.Idle);
        StopWhereItIs();
    }

    /// <summary>Interrupted: the text stops where it is and stays readable for its normal time; nothing shown means nothing to keep.</summary>
    private void StopWhereItIs()
    {
        CutReveal();
        if (_shownText.Length > 0 && _bubble.IsOpen && !_bubble.ShowsFiller) StartCountdown(_shownText.Length);
        else _bubble.Hide();
    }

    private void OnTurnFailed(TurnFailed e)
    {
        if (!_turns.IsCurrent(e.TurnId)) return;
        _window.SetActivity(Activity.Idle);
        _mood.Quiet();
        _typewriter.Reset();
        var text = FailText(e.Kind);
        _bubble.SetText(text, filler: false);
        _bubble.StartFade(text.Length);
    }

    private static string FailText(FailKind kind) => kind switch
    {
        FailKind.Offline => "I can't reach Claude right now, so I'm offline for a moment.",
        FailKind.NotInstalled => "I can't find Claude Code on this PC. Install it and I'm back.",
        FailKind.TooOld => "Your Claude Code is too old for me. Update it (2.1.318 or newer).",
        FailKind.UsageLimit => "Claude says your plan limit is reached. I'll be back when it resets.",
        _ => "Something went wrong on the Claude side. Try again in a moment.",
    };

    public void Dispose()
    {
        _typewriter.Reset();
        _bubble.Hide();
        _mood.Chatting();
        _input?.Close();
        _session?.Dispose();
    }
}
