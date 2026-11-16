# P1: one `claude` process per message (A) vs per session (B)

Throwaway. Decides D-037. Not part of `Cortana.sln` or the app.

Run: `dotnet build -c Release`, then `dotnet bin\Release\net10.0\P1.dll [A|B|AB]`.
Writes `turns.csv` next to the dll. `turns-run1.csv` and `turns-run2.csv` here are the two runs
reported in `results.md`; `run2-log.txt` is run 2's console log. Each run is about 60 short
Haiku calls on Philip's Claude plan.

What it does:
- Runs `claude` in `%APPDATA%\Companion\brain\` (D-048) with
  `-p --input-format stream-json --output-format stream-json --verbose --include-partial-messages
  --tools "" --strict-mcp-config --no-session-persistence --setting-sources project --safe-mode
  --disable-slash-commands --model haiku --system-prompt-file <persona>`.
- A: spawns a process per message; the app replays the last 20 messages as one transcript.
  B: one process, only the new message is written to stdin.
- 20 scripted chat turns (fact at turn 9, recall at turn 17, "hike" at 19), then 5 interrupts
  (A: kill the process; B: `control_request` `interrupt`), then `set_model` (B: `control_request`;
  A: `--model sonnet` on the next spawn).
- Memory: private bytes of the `claude` process tree (same metric as `tools/perfcheck.ps1`),
  plus working set.

CSV columns: `first_word_ms` = first text delta (A: from process start; B: from the stdin write,
turn 1 from process start), `init_ms` = to the `system init` event (A: startup cost),
`ram_mb` = private bytes of the tree when the reply finished, `in/cache_create/cache_read/out` =
token usage of that turn, `cost_usd` (cumulative per session in B).
