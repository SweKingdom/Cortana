using Companion.Memory;

namespace Companion.Core;

internal enum FailKind { Offline, NotInstalled, TooOld, UsageLimit, Other }

/// <summary>Notifications on the <see cref="EventBus"/>. Every reply event carries the TurnId (D-038).</summary>
internal sealed record UserMessage(string Text);
internal sealed record TurnStarted(int TurnId);
/// <summary>Fired only when a filler is shown (slow reply), not when the app merely waits (no flicker on fast replies, D-058).</summary>
internal sealed record ThinkingStarted(int TurnId);
/// <summary>A local line shown while waiting. Not a reply: never saved to history, no model call.</summary>
internal sealed record Filler(int TurnId, string Text);
internal sealed record ReplyChunk(int TurnId, string Text);
/// <summary>The reply is complete. Mood is the tag at its end (null when she left it out or the reply was interrupted).</summary>
internal sealed record ReplyDone(int TurnId, string Text, bool Interrupted, Mood? Mood = null);
/// <summary>She saved a note (the bubble shows it with an Undo button, D-026).</summary>
internal sealed record MemoryProposal(int TurnId, MemoryEntry Entry);
/// <summary>A note was refused because its memory file is at the cap (D-032); the bubble says so, the tidy-up comes in step 4.</summary>
internal sealed record MemoryFull(int TurnId, MemoryKind Kind);
internal sealed record TurnFailed(int TurnId, FailKind Kind, string Message);
internal sealed record Interrupt;
internal sealed record PanicHide;
