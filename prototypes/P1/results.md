# P1 results (2026-11-17)

Throwaway notes. Claude Code 2.1.318 (updated from 2.1.49 for this test), Haiku (`claude-haiku-5-5`),
Windows 11, Philip's claude.ai plan. Two full runs (A then B each time); run 2 is the main one,
run 1 is in brackets. Scripted 20-turn chat with a ~100-token persona. Median / p90 / max.

## Measurements

| Metric | A: process per message | B: process per session |
| --- | --- | --- |
| First word, ms (A turns 1-20; B turns 2-20) | **1925** / 2565 / 3004 [1816 / 2321 / 2878] | **500** / 1121 / 1397 [493 / 588 / 943] |
| Turns with first word over 2000 ms | 9 of 20 [5 of 20] | 0 of 19 [0 of 19] |
| First word, B turn 1 (includes spawn) | n/a | 1844 [1363] |
| Spawn to `system init` (startup cost), ms | 849 / 917 / 1006 [846 / 922 / 950] | paid once, ~850 |
| Whole turn, ms | 2283 / 3115 / 3195 | 952 / 1478 / 1633 |
| `claude` tree private bytes, MB (D-052 metric) | 407 / 415 / 423 [404 / 424 / 431] | 431 / 436 / 441 [432 / 435 / 462] |
| `claude` tree working set, MB | 207 / 209 / 209 | 221 / 224 / 228 |
| `claude` RAM at rest (between messages) | **0** (no process) | **435** private, 222 working set (10 s after last turn) [426] |
| Failed turns of 20 (timeout, error, malformed, empty) | 0 [0] | 0 [0] |
| Input tokens per turn, turn 1 / 10 / 20 | 1043 / 1774 / 1785 | 1045 / 2111 / 3069 |
| Input growth | plateaus at ~1.8-1.9k from turn 11 (app caps replay at 20 messages) | +~100 per turn, unbounded until restart |
| Fresh input tokens over 20 turns (input + cache write) | 32 464, cache read 0 | 3 107, cache read 39 398 |
| Output tokens over 20 turns | 3359 | 1356 (different replies) |
| API-equivalent cost over 20 turns | $0.0112 | $0.0018 (session total) |
| Interrupt (5 tries) | kill: process gone in 16-24 ms; next spawn works 5/5 (first word 1.4-1.6 s) | `control_request` `interrupt`: `control_response` in 1-13 ms, stream ends 4-19 ms later, process stays alive 5/5, next turn works 5/5 |
| `set_model` | `--model sonnet` on the next spawn: reply came from `claude-sonnet-5-5`, history replayed, recall OK | `control_request` `set_model`: `control_response` in 4-9 ms, next reply from `claude-sonnet-5-5`, history kept (recall OK, both runs) |

The `claude` tree is `claude.exe` (400-440 MB private) plus a 1 MB `conhost`. Add the app (75 MB):
about 480 MB private while A replies, and about 510 MB private whenever B's process is alive.

## Notes

- **RAM, not mode, is the problem.** A running `claude` is 400-440 MB private bytes in both modes
  (207-228 MB working set). D-052's whole-tree limit of 300 MB cannot hold while a process exists.
  Only A has no process at rest, so only A meets the limit at rest. By working set the `claude` part is
  207-228 MB, but the project's metric is private bytes (perfcheck.ps1) and the app's working set
  was not measured here.
- **Interrupt in B:** the interrupted turn ends with a `result` event with `subtype:
  error_during_execution` and `is_error: true`. That is the normal outcome of an interrupt, not a
  crash, and the app must not treat it as one.
- **Model switch in B** rewrites the prompt cache once: the turn after `set_model` had 5686 cache-write
  tokens and 0 cache read (run 1).
- **First word on long creative prompts:** the first content block was `thinking`, and in A the first
  text came 9-14 s after the start (the 400-word story prompt). Ordinary chat turns had a `text`
  block first. Not investigated further.
- **Caching:** in A the persona + system overhead (~1000 tokens) never hits the prompt cache across
  processes (cache read 0 on every turn). In B history crosses the cache threshold after a few turns.
- **Flags on 2.1.318:** `--safe-mode`, `--system-prompt-file`, `--append-system-prompt-file` and
  `--disable-slash-commands` all exist and were accepted. 2.1.49 had neither `--safe-mode` nor the
  `-file` flags in its help. The init event still lists the user's plugins (5), skills (22) and
  slash commands (57) with `--setting-sources project --safe-mode`, but asked directly, the model
  answered NONE under `--safe-mode --disable-slash-commands`: nothing leaked into the prompt (D-048 holds).
- **Not measured:** restart with replayed history in B, a system-prompt change in B (needs a
  restart by design), idle teardown timing, quota effect of A's larger fresh-token volume
  (how the plan weighs cached vs fresh tokens is not visible here), P2 tool reliability.
- Run-to-run: A's first word varied more than B's between runs (9 vs 5 turns over 2 s); the
  medians stayed within ~110 ms.

## Follow-up: pre-warm and thinking (run 3, same day)

**Pre-warm (mode W, 10 turns):** spawn `claude`, wait 3 s (simulated typing), then write the message.
First word measured from the write: **787 ms median / 1131 p90 / 1544 max** (A without pre-warm: 1925 /
2565 / 3004; B per session: 500 / 1121 / 1397). Startup to `system init` fell to 35 ms after the
write. The later turns were slower (turn 9: 1131, turn 10: 1544, with longer replies); 10 turns is
a small sample. Private bytes of the warm `claude` tree 360-399 MB. Raw: `turns-run3-prewarm.csv`, `run3-log.txt`.

**Disabling thinking** (long 400-word prompt, one sample each, Haiku): `MAX_THINKING_TOKENS=0`,
`CLAUDE_CODE_DISABLE_THINKING=1`, `--thinking disabled`, `--effort low` and
`--settings {"alwaysThinkingEnabled":false}` all still produced a `thinking` block before the text
(time to first text 2.0-15.8 s, noisy). No switch that removes it was found. The 20 ordinary chat turns
never had a thinking block. The mechanism is left for step 2.
