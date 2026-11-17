# Cortana Desktop

Private, lightweight Windows desktop AI companion. C# + WPF on .NET 10.
Not for distribution.

- Design: `docs/architecture.md`
- Decisions, status and next step: `dev-memory/`
- Rules for Claude Code: `CLAUDE.md`
- How to start each build step: `prompts/next-step.md`

## Performance check

`powershell -File tools\perfcheck.ps1` builds Release, starts the app, and reports the budget
(D-052) as the median of 3 runs. Options: `-Fps 30|15|10` (default 10), `-Render hardware`,
`-Runs`, `-WarmupSeconds`, `-MeasureSeconds`, `-NoBuild`. Results go to `perf-results/` (ignored by Git).

`-Chat` adds a chat run (about 15 short Haiku messages on your plan): first word for the real flow
(input opens, pre-warm, 3 s of simulated typing, Enter) and for cold turns, the app's private bytes after
a chat, the whole tree at rest, and the peak while a `claude` process runs. Options: `-ChatWarm`
(default 10), `-ChatCold` (5), `-TypingMs` (3000). `-ChatOnly` skips the idle runs.

## Tests

`dotnet test tests\Companion.Tests` runs the unit tests (xUnit, dev only): reply cleaner, transcript,
fillers, event bus, quota, history, and the conversation flow against a fake brain.

## Dev environment variables

Read once at startup; they do nothing when unset. For measurement and prototypes only.

| Variable | Effect |
| --- | --- |
| `COMPANION_FPS` | Overrides the idle breathing frame rate (1-60, default 10). Used by perfcheck for P3. |
| `COMPANION_RENDER=hardware` | Turns off the software-rendering default (D-050). |
| `COMPANION_PERF_FILE` | Path where the app writes its start-to-orb time in ms after the first rendered frame. Used by perfcheck. |
| `COMPANION_PERF_CHAT=W:C` | Makes the app drive its own chat path: W turns through the real flow and C cold turns, with history and quota in memory only. Used by `perfcheck.ps1 -Chat`. |
| `COMPANION_PERF_TYPING_MS` | Simulated typing time before Enter in the real-flow turns (default 3000). |
| `COMPANION_PERF_CHAT_FILE` | Path where the chat probe writes one JSON line per turn. |
