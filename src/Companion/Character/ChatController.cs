using Companion.Brain;
using Companion.Core;

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
    private readonly TurnTracker _turns;
    private readonly SpeechBubble _bubble;

    private ModeSession? _session;
    private ChatInput? _input;
    private bool _replyStarted;
    private int _replyChars;

    public ChatController(CharacterWindow window, EventBus bus, bool ephemeral = false)
    {
        _window = window; _bus = bus; _ephemeral = ephemeral;
        _turns = bus.TrackTurns(); // subscribed first, so Current is up to date before the handlers below run
        _bubble = new SpeechBubble(window.Orb);

        window.OrbClicked += OnOrbClicked;
        bus.Subscribe<TurnStarted>(_ => { _replyStarted = false; _replyChars = 0; _window.SetActivity(Activity.Idle); });
        bus.Subscribe<ThinkingStarted>(e => { if (_turns.IsCurrent(e.TurnId)) _window.SetActivity(Activity.Thinking); });
        bus.Subscribe<Filler>(e => { if (_turns.IsCurrent(e.TurnId) && _input == null) _bubble.SetText(e.Text, filler: true); });
        bus.Subscribe<ReplyChunk>(OnReplyChunk);
        bus.Subscribe<ReplyDone>(OnReplyDone);
        bus.Subscribe<TurnFailed>(OnTurnFailed);
        bus.Subscribe<Interrupt>(_ => { _bubble.Hide(); _window.SetActivity(Activity.Idle); });
        bus.Subscribe<PanicHide>(_ => { _bubble.Hide(); _input?.Dismiss(); _window.SetActivity(Activity.Idle); });
    }

    internal EventBus Bus => _bus;
    internal ModeSession Session => _session ??= ModeSession.CreateNormal(_bus, _ephemeral);

    /// <summary>Hotkey or click: open the text box (and pre-warm the process). Typing interrupts a running reply.</summary>
    public void OpenInput()
    {
        if (_input != null) { _input.Activate(); return; }
        var session = Session;
        _bubble.Hide(); // the input takes the bubble's place; the reply is in the history
        session.Conversation.PreWarm();

        var input = new ChatInput();
        _input = input;
        input.Typing += () => { if (_session?.Conversation.IsBusy == true) _bus.Publish(new Interrupt()); };
        input.Submitted += text => { _input = null; _bus.Publish(new UserMessage(text)); };
        input.Dismissed += () => { _input = null; _session?.Conversation.CancelPreWarm(); };
        input.ShowNear(_window);
    }

    /// <summary>Perfcheck probe: types into the real input and presses Enter.</summary>
    internal void ProbeSubmit(string text)
    {
        _input?.SetTextForProbe(text);
        if (_input != null) _input.SubmitForProbe(text); else _bus.Publish(new UserMessage(text));
    }

    private void OnOrbClicked()
    {
        if (_input != null) { _input.Dismiss(); return; }
        if (_session?.Conversation.IsBusy == true) { _bus.Publish(new Interrupt()); return; } // click on a talking orb stops her
        OpenInput();
    }

    private void OnReplyChunk(ReplyChunk e)
    {
        if (!_turns.IsCurrent(e.TurnId)) return;
        _window.SetActivity(Activity.Idle);
        _replyChars += e.Text.Length;
        if (_input != null) return;
        if (!_replyStarted) { _replyStarted = true; _bubble.SetText(e.Text, filler: false); } // replaces the filler or the old reply
        else _bubble.Append(e.Text);
    }

    private void OnReplyDone(ReplyDone e)
    {
        if (!_turns.IsCurrent(e.TurnId)) return;
        _window.SetActivity(Activity.Idle);
        if (_input != null) return;
        if (e.Interrupted || _replyChars == 0) _bubble.Hide(); // nothing to read (or a filler left over)
        else _bubble.StartFade(_replyChars);
    }

    private void OnTurnFailed(TurnFailed e)
    {
        if (!_turns.IsCurrent(e.TurnId)) return;
        _window.SetActivity(Activity.Idle);
        if (_input != null) return;
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
        _bubble.Hide();
        _session?.Dispose();
    }
}
