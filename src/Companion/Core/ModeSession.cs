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
    private readonly IBrain? _lightBrain;
    public Conversation Conversation { get; }
    public Quota Quota { get; }
    public MemoryStore Memory { get; }

    private ModeSession(IBrain brain, IBrain lightBrain, Conversation conversation, Quota quota, MemoryStore memory)
    {
        Brain = brain; _lightBrain = lightBrain; Conversation = conversation; Quota = quota; Memory = memory;
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
        var chatModel = ClaudeCodeBrain.ChatModelFromEnvironment();
        var brain = new ClaudeCodeBrain(chatModel, ClaudeCodeBrain.ChatEffortFromEnvironment(chatModel));                           // the chat (Sonnet, D-076)
        var lightBrain = new ClaudeCodeBrain(ClaudeCodeBrain.LightModel);     // the summary: Haiku at low effort
        var summarizer = new Summarizer(lightBrain, history, memory, quota);
        var conversation = new Conversation(bus, brain, history, quota, chatModel,
            prompts: prompts, memory: memory, summarizer: summarizer);
        return new ModeSession(brain, lightBrain, conversation, quota, memory);
    }

    public void Dispose()
    {
        Conversation.Interrupt();
        Brain.Dispose();
        _lightBrain?.Dispose();
    }
}
