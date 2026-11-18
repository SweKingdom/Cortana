using Companion.Core;

namespace Companion.Brain;

/// <summary>
/// System = personality/system prompt; Context = a line such as the current time; User and Assistant = the chat;
/// PostHistory = the personality's reminder, placed after the history and just before the new message (D-074).
/// </summary>
internal enum Role { System, Context, User, Assistant, PostHistory }

internal sealed record ChatMessage(Role Role, string Text);

/// <summary>Placeholder until tools exist (step 5, D-057). Brains get an empty list now.</summary>
internal sealed record ToolSpec(string Name);

internal sealed record BrainUsage(string Model, long InputTokens, long CacheTokens, long OutputTokens, string FirstBlock);

/// <summary>What a brain streams: text, a marker that a thinking block started (for fillers, D-058), and the end.</summary>
internal abstract record BrainChunk;
internal sealed record TextChunk(string Text) : BrainChunk;
internal sealed record ThinkingChunk : BrainChunk;
internal sealed record DoneChunk(BrainUsage Usage) : BrainChunk;

internal sealed class BrainException(FailKind kind, string message) : Exception(message)
{
    public FailKind Kind { get; } = kind;
}

internal interface IBrain : IDisposable
{
    string Name { get; }
    bool SupportsVision { get; }
    bool SupportsTools { get; }

    IAsyncEnumerable<BrainChunk> StreamAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, CancellationToken ct);

    /// <summary>Start the process for the next message early (D-055). No-op for brains that don't need it.</summary>
    void PreWarm(string systemPrompt) { }
    void CancelPreWarm() { }
}
