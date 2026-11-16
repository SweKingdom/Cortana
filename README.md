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

## Dev environment variables

Read once at startup; they do nothing when unset. For measurement and prototypes only.

| Variable | Effect |
| --- | --- |
| `COMPANION_FPS` | Overrides the idle breathing frame rate (1-60, default 10). Used by perfcheck for P3. |
| `COMPANION_RENDER=hardware` | Turns off the software-rendering default (D-050). |
| `COMPANION_PERF_FILE` | Path where the app writes its start-to-orb time in ms after the first rendered frame. Used by perfcheck. |
