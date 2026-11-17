using Companion.Brain;
using Companion.Memory;

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
    public MemoryStore Memory { get; }

    private ModeSession(IBrain brain, Conversation conversation, Quota quota, MemoryStore memory)
    {
        Brain = brain; Conversation = conversation; Quota = quota; Memory = memory;
    }

    /// <param name="ephemeral">Keep history, quota and memory in memory only and use the built-in personality (the perfcheck chat probe must not touch the real files).</param>
    /// <param name="settings">Source of the flirt level (D-017); null means the default, level 4.</param>
    public static ModeSession CreateNormal(EventBus bus, bool ephemeral = false, Settings? settings = null)
    {
        var history = new History(ephemeral ? null : System.IO.Path.Combine(AppPaths.MemoryNormal, "history.jsonl"));
        var quota = new Quota(ephemeral ? null : AppPaths.QuotaFile);
        var memory = new MemoryStore(ephemeral ? null : AppPaths.MemoryNormal);
        var personality = new PersonalitySource(ephemeral ? null : AppPaths.PersonalityNormal);
        var prompts = new PromptBuilder(personality, () => settings?.FlirtLevel ?? 4, memory);
        var brain = new ClaudeCodeBrain();
        var summarizer = new Summarizer(brain, history, memory, quota);
        var conversation = new Conversation(bus, brain, history, quota, ClaudeCodeBrain.Model,
            prompts: prompts, memory: memory, summarizer: summarizer);
        return new ModeSession(brain, conversation, quota, memory);
    }

    public void Dispose()
    {
        Conversation.Interrupt();
        Brain.Dispose();
    }
}
