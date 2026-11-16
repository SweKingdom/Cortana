# Decisions

Owns every project decision: ID, status, reason. Design details live in the
architecture doc (https://claude.ai/artifact/2J4bZZSfKJv2FCWrGrHm87).

Rules:
- Never edit a past row's meaning. To change a decision, add a new row that says
  `Supersedes D-NNN` and set the old row's status to `SUPERSEDED by D-NNN`.
- Statuses: ACCEPTED, SUPERSEDED, REJECTED, DEFERRED.
- Only Philip accepts a decision. Claude may propose rows marked PROPOSED.

| ID | Date | Decision | Reason | Status |
| --- | --- | --- | --- | --- |
| D-001 | 2026-11-13 | Stack: C# + WPF on .NET 8, Windows only | Light (~50–150 MB RAM), easy transparent windows | SUPERSEDED by D-036 |
| D-002 | 2026-11-13 | Performance budget: <300 MB RAM (hosted), ~0% idle CPU, <50 MB download, <2 s start | Main difference from Capri | SUPERSEDED by D-040 |
| D-003 | 2026-11-13 | Character: Cortana-inspired personality, v1 look is a code-drawn glowing orb | Private project, max ~2 people originally | ACCEPTED |
| D-004 | 2026-11-13 | Never publish with the Cortana name or any Halo audio/lines/art | Microsoft IP | ACCEPTED |
| D-005 | 2026-11-13 | Modules talk only through an event bus; interfaces IBrain, ITool, IVoice, IAnimator | Swap parts without touching others | SUPERSEDED by D-038 |
| D-006 | 2026-11-13 | Animation via WPF Storyboards at 30 fps, stopped when hidden/sleeping/locked; no render loop | ~0% idle CPU | ACCEPTED |
| D-007 | 2026-11-13 | Default brain: free Gemini key | Free, light | SUPERSEDED by D-012 |
| D-008 | 2026-11-16 | Two modes: normal and explicit, each with own personality, brain and memory folder | Claude/Gemini don't allow explicit content | ACCEPTED |
| D-009 | 2026-11-16 | Explicit mode via hosted Venice/OpenRouter | Keep PC free | SUPERSEDED by D-010 |
| D-010 | 2026-11-16 | Explicit mode runs a local model (Ollama or llama-server); normal mode runs hosted on Claude | Explicit chats stay on the PC | ACCEPTED |
| D-011 | 2026-11-16 | Local model cap: 4–5 GB total incl. context; start with 3–4B Q4, max 7–8B Q3; 4K context; all on GPU | RAM already near full | ACCEPTED |
| D-012 | 2026-11-16 | Normal mode uses Philip's Claude plan via Claude Code (ClaudeCodeBrain). Supersedes D-007 | No extra cost | ACCEPTED |
| D-013 | 2026-11-16 | Claude model: Haiku default, auto-escalate to Sonnet (code, long msg, screen glance, multi-tool, `[escalate]`), lockable in settings | Speed vs smarts | ACCEPTED |
| D-014 | 2026-11-16 | Game mode: auto on fullscreen game; pause animations, click-through, no focus, explicit mode locked, input tools off | Overwatch/Minecraft + anti-cheat | ACCEPTED |
| D-015 | 2026-11-16 | Always speaks English; knows user is Philip, invents own nicknames | Philip's choice | ACCEPTED |
| D-016 | 2026-11-16 | Only Philip uses the app; no build for others; PIN for explicit mode optional | Philip's choice | ACCEPTED |
| D-017 | 2026-11-16 | Normal mode is flirty as far as Claude allows; flirt slider 0–4 (4 = max) in settings and tray | Philip's choice | ACCEPTED |
| D-018 | 2026-11-16 | Proactive comments: "often" (foreground app name only, max 1 per 20 min, never mid-game, frequency slider) | Philip's choice | ACCEPTED |
| D-019 | 2026-11-16 | No autostart with Windows by default | Philip's choice | ACCEPTED |
| D-020 | 2026-11-16 | Dev: VS Code + private GitHub repo SweKingdom/Cortana | Philip's setup | ACCEPTED |
| D-021 | 2026-11-16 | Build hosted first, local explicit mode much later | Priority | ACCEPTED |
| D-022 | 2026-11-16 | Reply output setting: Text or Voice; voice likely cloud TTS via API key, provider TBD | Voice not chosen yet | ACCEPTED |
| D-023 | 2026-11-16 | v2 character art likely by an artist | Quality | DEFERRED |
| D-024 | 2026-11-17 | Reuse Capri code only as ports with its Required Notice lines in NOTICE.md; never its art | PolyForm Noncommercial license | ACCEPTED |
| D-025 | 2026-11-17 | Adopt Neuro-sama ideas: example dialogues, short replies, speak first sentence early, interruptible, explicit mode as a "twin" | Feel fast and in-character | ACCEPTED |
| D-026 | 2026-11-17 | Drift guards: personality re-sent verbatim every call; facts vs impressions split; ask before saving memories; append-only corrections; past consent never reused; leak test between modes | Castle Memory Kit principles, light | ACCEPTED |
| D-027 | 2026-11-17 | Project continuity via dev-memory/ (decisions, session memory, checklist) + CLAUDE.md rules | Prevent drift between sessions | ACCEPTED |
| D-028 | 2026-11-17 | ClaudeCodeBrain keeps one persistent `claude` process (stream-json input) instead of one per message; restart on crash or after 1 h idle. Target: first word ~2 s | Per-message startup adds seconds | SUPERSEDED by D-037 |
| D-029 | 2026-11-17 | Plan quota: daily cap on proactive calls (default 30) and auto-Sonnet escalations (default 20); usage counter in settings; still answers when spoken to | Protect plan limits for real work | ACCEPTED |
| D-030 | 2026-11-17 | Panic hotkey (suggested Ctrl+Alt+H): hide instantly, cancel reply, mute voice; returns in normal mode | Privacy when someone walks in | ACCEPTED |
| D-031 | 2026-11-17 | memory/flirty/ encrypted with DPAPI; explicit-mode logs contain no message text | Sensitive data on disk | ACCEPTED |
| D-032 | 2026-11-17 | Memory cap ~800 tokens each for facts.md and impressions.md; she proposes weekly/at-cap tidy-ups, applied only after approval, old versions kept in memory/archive/ | Keep prompts small, no silent loss | ACCEPTED |
| D-033 | 2026-11-17 | Healthy-relationship rules in personality `boundaries` (all flirt levels, both modes): jealousy is a joke, never guilt-trips, happy about his real life, nudges toward sleep after midnight | Fun, not draining | ACCEPTED |
| D-034 | 2026-11-17 | tools/perfcheck.ps1 measures RAM, idle CPU and start time against D-002 before every major commit | Stop the budget eroding | SUPERSEDED by D-040 |
| D-035 | 2026-11-17 | Voice in two rounds. First version (steps 0–5) is text-only. Step 6 alpha uses the built-in Windows voice to build the whole voice pipeline. A cloud voice with more flair (e.g. Fish Audio) is added later as a new IVoice, provider undecided; Windows voice stays as fallback. Never clone Halo's voice actress. Refines D-022 | Test the pipeline free first, choose the flair voice later | ACCEPTED |
| D-036 | 2026-11-17 | Target net10.0-windows (.NET 10 LTS, supported to Nov 2028); publish framework-dependent. Supersedes D-001 | .NET 8 ended support 10 Nov 2026; WPF can't be trimmed | ACCEPTED |
| D-037 | 2026-11-17 | ClaudeCodeBrain runs either one process per message or one per session, chosen by prototype P1; never mixed; exactly one owner of conversation history. Supersedes D-028 | Persistent process unproven (Capri uses one per call) and clashes with app-owned history | ACCEPTED |
| D-038 | 2026-11-17 | Commands are direct calls (Conversation -> IBrain, ToolRegistry); notifications go on the bus. Reply events carry an integer TurnId; stale turns are dropped. Supersedes D-005 | Clear rule; no stale chunks after interrupt | ACCEPTED |
| D-039 | 2026-11-17 | Explicit-mode trust boundary in code: loopback-only brain URLs, Windows voice only, no cloud tools, leak test counts outbound non-loopback requests (must be 0), no full crash dumps. Threat model: single-user PC; PIN is a privacy latch, not a security boundary | Make "explicit stays local" enforceable | ACCEPTED |
| D-040 | 2026-11-17 | Performance budget = 5 metrics: app private bytes <150 MB, whole tree incl. claude <300 MB (reported separately), idle CPU <0.5% of one core over 60 s, start to orb <1 s, first word ~2 s; first audio added at step 6. perfcheck.ps1 measures these. Supersedes D-002, D-034 | Unambiguous measurement | ACCEPTED |
| D-041 | 2026-11-17 | Each mode is a disposable ModeSession (brain, memory, conversation, voice); switching = cancel turn, stop voice, dispose, create new | Nothing survives a switch | ACCEPTED |
| D-042 | 2026-11-17 | Memory lines: `- [id \| date \| source] text`, corrections as new lines with `replaces id`; preferences under `## Preferences` in facts.md; PromptBuilder budget ~3,000 tokens (chars/4), trims oldest verbatim messages first, never personality. No confidence or expiry fields | Unbounded verbatim messages; traceable corrections | ACCEPTED |
| D-043 | 2026-11-17 | Mood is two dimensions: Activity (app: idle, thinking, talking, sleeping) drives motion; Expression (tag: happy, teasing, flirty, concerned) drives color | They were mixed | ACCEPTED |
| D-044 | 2026-11-17 | Every JSON file has `"version": 1`; migrations only when a v2 exists | Cheap future-proofing | ACCEPTED |
| D-045 | 2026-11-17 | Tool rules in code: typed args via System.Text.Json, `open` only http/https + Start-menu apps, 10 s timeout, confirmation shows exact action and args, clipboard/screen content quoted as data and forces Confirm for later tool calls that turn | Prompt injection and bad args | ACCEPTED |
| D-046 | 2026-11-17 | Proactive comments also suppressed by: tray Do-not-disturb, Windows presentation/busy/Focus Assist state, input in last ~5 s, exclude list (Teams, Zoom). Refines D-018 | Avoid being annoying | ACCEPTED |
| D-047 | 2026-11-17 | IVoice.Level optional; Windows voice drives orb pulse from VisemeReached; recovery rules (restart claude once, offline mood, corrupt JSON -> .unreadable copy + defaults, restart claude and re-arm timer after wake); Per Monitor V2 DPI | Gaps found in counter-review | ACCEPTED |
| D-048 | 2026-11-17 | The app's own `claude` brain process always runs in `%APPDATA%\Companion\brain\` (empty, app-owned), never in the repo | Otherwise it reads the repo's CLAUDE.md and acts as the builder, not as her | ACCEPTED |

## Open

- Chat hotkey (suggested Ctrl+Alt+Space) and panic hotkey (suggested Ctrl+Alt+H)
- Exact quota numbers (defaults 30 proactive / 20 Sonnet per day are guesses)
- Cloud voice provider (e.g. Fish Audio) and the voice itself, after the alpha voice
- Which 3–4B local model (license must allow adult content: prefer Apache 2.0)
