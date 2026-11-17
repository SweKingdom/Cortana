using Companion.Brain;

namespace Companion.Core;

/// <summary>
/// One mode's disposable session (D-041): brain, conversation and history (voice and memory arrive in later
/// steps). Dispose cancels the running turn and kills any process, so nothing survives a switch.
/// Only the normal mode exists in step 2 (the explicit mode comes in step 7).
/// </summary>
internal sealed class ModeSession : IDisposable
{
    public IBrain Brain { get; }
    public Conversation Conversation { get; }
    public Quota Quota { get; }

    private ModeSession(IBrain brain, Conversation conversation, Quota quota)
    {
        Brain = brain; Conversation = conversation; Quota = quota;
    }

    /// <param name="ephemeral">Keep history and quota in memory only (the perfcheck chat probe must not touch the real files).</param>
    public static ModeSession CreateNormal(EventBus bus, bool ephemeral = false)
    {
        var history = new History(ephemeral ? null : System.IO.Path.Combine(AppPaths.MemoryNormal, "history.jsonl"));
        var quota = new Quota(ephemeral ? null : AppPaths.QuotaFile);
        var brain = new ClaudeCodeBrain();
        return new ModeSession(brain, new Conversation(bus, brain, history, quota, ClaudeCodeBrain.Model), quota);
    }

    public void Dispose()
    {
        Conversation.Interrupt();
        Brain.Dispose();
    }
}
