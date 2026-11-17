# P2: reliability of the text tool protocol

Throwaway. Tests the `<tool name="x">{json}</tool>` protocol from the architecture doc on Claude Code,
one process per message (D-055). Not part of `Cortana.sln` or the app.

Run: `dotnet build -c Release`, then `dotnet bin\Release\net10.0\P2.dll` (variant A: 50 requests, about 90 short
Haiku calls on the plan, 5-8 minutes) or `dotnet bin\Release\net10.0\P2.dll B` (variant B: the 42 tool requests, about 50 calls). `dotnet bin\Release\net10.0\P2.dll selftest` feeds the parser,
validator and leak detector 11 handcrafted bad outputs to prove they are not blind. The run writes
`requests.csv` and `replies.jsonl` next to the dll; the copies here are run 1, reported in `results.md`
(`run1-log.txt` is its console log, `selftest-log.txt` the self-test). Variant B's output is `requests-B.csv`, `replies-B.jsonl`, `run1B-log.txt`.

What it does:
- `claude` runs in `%APPDATA%\Companion\brain\` (D-048) with the P1 flags, `--model haiku`.
- System prompt = P1 persona + a context line (fixed "now": Tuesday 2026-11-17 14:32) + the tool section
  (protocol, five tools: `set_reminder{text,when}`, `list_reminders{}`, `cancel_reminder{query}`,
  `open{target}`, `remember{kind,text}`). Text is in `Program.cs` (`Cfg.ToolSection`).
- 50 requests: 34 needing one tool, 8 needing two (one has the same tool twice), 8 needing none (chat,
  and "turn off my monitor" / "send an email" where inventing a call is wrong). Single message each, no history.
- Each request is a tool loop: pass 1 (the message) -> parse -> strict typed validation (D-045 rule 1) and a
  stub tool -> pass 2 (transcript + `Tool result (...)` lines) -> final reply. Pass 2 only if pass 1 had a tag.
  The loop runs ONE round: a tool call in pass 2 is counted as a repeat call, not executed.
- A P2 `ReplyCleaner` strips well-formed tags; whatever markup survives is a leak.
- The stub `cancel_reminder` returns the matches when more than one reminder matches (never guesses).

Definitions (per request):
- **Malformed (protocol):** a tool tag was attempted but is not valid: bad JSON, unknown tool, arguments
  failing the strict schema (exact property set, string values, `when` as `YYYY-MM-DDTHH:mm`, `kind`,
  `open` target must be an http(s) URL or an app name), wrong quote style, an unclosed tag, or another
  format (`<function_calls>`, `<invoke>`, a ```json blob).
- **Leaked (protocol):** tool markup or raw argument JSON left in the text after cleaning, on either pass.
- **Missed (judgment):** an expected tool was not called. A malformed attempt at it counts as malformed, not missed.
- **Wrong arguments (judgment):** right tool, wrong value (checked: reminder time within 2 min, key words in text/query/target).
- **Spurious (judgment):** a valid call that was not expected.
- MCP would fix protocol failures; judgment failures are about what the model decides.

CSV columns: `p1_*`/`p2_*` = first word and total of each pass (from that process's spawn),
`final_first_ms`/`final_total_ms` = from Enter to the final reply (pass 1 total + pass 2 first/total;
no pre-warm in either pass), `p1_block`/`p2_block` = type of the first content block (`thinking` or `text`).

Variant B (`B` argument): the prompt tells her to write a confirmation plus the tag in one reply for fire-and-forget tools
(`set_reminder`, `cancel_reminder`, `open`, `remember`); only `list_reminders`, a tool error or an unparsable tag costs a second
process (up to 3 rounds). A second process is pre-warmed when a tag is seen in the stream, and killed if unused. Times are wall
clock from Enter; "first visible word" is the first text outside a tag. See `results.md`.
