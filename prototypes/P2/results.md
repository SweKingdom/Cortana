# P2 results: text tool protocol, Claude Code 2.1.318, Haiku

One run, 50 requests, 92 `claude` processes (50 first passes + 42 second passes), no infrastructure failures,
reported cost $0.034, 147 548 input tokens (mostly cache reads) and 12 570 output tokens. Self-test of the
detectors: 11/11 (`selftest-log.txt`). Raw data: `requests.csv`, `replies.jsonl`, `run1-log.txt`.

## Failures

Per request. "Protocol" = what MCP would fix. "Judgment" = what the model decides.

| Group | Requests | Malformed (protocol) | Leaked (protocol) | Missed (judgment) | Wrong args (judgment) | Spurious (judgment) |
| --- | --- | --- | --- | --- | --- | --- |
| one tool | 34 | 0 | 0 | 1 | 0 | 1 |
| two tools | 8 | 0 | 0 | 1 | 0 | 0 |
| no tool | 8 | 0 | 0 | 0 | 0 | 0 |
| **all** | **50** | **0** | **0** | **2** | **0** | **1** |

- **Protocol failures: 0 of 50.** All 51 tags the model wrote (49 in pass 1, 2 repeat calls in pass 2) were valid:
  exact documented shape, valid JSON, schema-correct. Every tool-needing request produced a tag; no
  `<function_calls>`-style output, no tag survived cleaning.
- **Judgment failures: 2 of 50** (one request has both a miss and a spurious call), both the same pattern:
  - #17 "cancel the timer": pass 1 called `list_reminders` (there is a "tea timer", so listing first is sensible),
    then pass 2 called `cancel_reminder {"query": "tea timer"}`.
  - #41 "list my reminders and cancel the stand-up one": pass 1 called only `list_reminders`, pass 2 called
    `cancel_reminder {"query": "stand-up"}`.
  In both the model asked for a second round, and my harness runs one round, so the final text was an
  unexecuted tag (empty after cleaning). Judged by the definitions agreed before the run these count as
  failures; in a loop that allows 2-3 rounds they would have been correct. They are not protocol failures.
- No wrong arguments in 50 requests (times, key words, Swedish and typo inputs all fine). No spurious calls on the
  8 no-tool requests, including "turn off my monitor" and "send an email" (it declined / offered a draft).
- Mood tag at the end of the final reply: 48/50 (the two misses are the unexecuted tags above).
- Caveat on the sample: 0 failures out of 51 tags only rules out a protocol failure rate above about 6%
  (rule of three). One model (Haiku), one run, single-message requests without history.

## Timing (median / p90, ms; no pre-warm in either pass)

| Tool turns (n=42) | median | p90 |
| --- | --- | --- |
| Pass 1 first word (from spawn) | 2591 | 3931 |
| Pass 1 total (tool call complete) | 2755 | 4209 |
| Pass 2 first word (from its own spawn) | 2569 | 4217 |
| Pass 2 total | 2842 | 4544 |
| **First word of the final reply (from Enter)** | **5738** | **8153** |
| **Final reply complete (from Enter)** | **6119** | **8539** |

No-tool turns (n=8): first word median 2078 ms, total median 2414 ms (max 7375 ms, one outlier, #49; p90 is
meaningless at n=8). In 35 of the 42 tool turns the reply starts with the tag, so nothing is visible until pass 2.
In the other 7 the model wrote a short preamble ("Noted, teal it is. I'll save that for you.") before the tag.

## Thinking

The first content block was `thinking` in 40 of 50 first passes and 29 of 42 second passes, including 5 of 8
no-tool chat turns. In P1, 0 of 20 ordinary chat turns thought. The tool prompt (or simply a longer system
prompt) triggers it. First word median is 2654 ms when the first block was thinking (n=40) against 1930 ms when
it was text (n=10), about 0.7 s later. This is the open question in D-055; it now matters for step 2.

## Reading

- The text protocol was reliable on this sample; MCP is not needed on protocol grounds.
- The cost of a tool turn in per-message mode is two processes, so about 5.7 s to the first word of the final
  reply against about 2 s for a plain chat turn, and about 480 MB of `claude` while each runs (D-056).
- Tool use can need more than one round (list, then act on what it found), so the app's loop must allow a few
  rounds (e.g. cap at 3) and treat each as a process.

---

# Variant B: single pass for fire-and-forget tools, pre-warmed second pass, up to 3 rounds

Requested by Philip after the first run. One run, the 34 one-tool and 8 two-tool requests (the 8 no-tool requests are
not re-run). Same model, version and script. Run: `dotnet bin\Release\net10.0\P2.dll B`. Raw data: `requests-B.csv`,
`replies-B.jsonl`, `run1B-log.txt`.

What changed against variant A:
- **Prompt** (`Cfg.ToolSectionB`): fire-and-forget tools (`set_reminder`, `cancel_reminder`, `open`, `remember`) get a short
  in-character confirmation ending with the mood tag, then the tag(s), all in one reply. Read-back (`list_reminders`):
  write only the tag. A tool error is answered in character.
- **Loop:** the app runs the tools and spawns a second process only if a result must be read back (`list_reminders`) or a
  tool returned an error (or a tag could not be parsed). Up to 3 rounds, one process each.
- **Pre-warm:** a second process is spawned as soon as `<tool name="` appears in the stream, as asked (any tag). It is killed
  if no second pass is needed. The clock of a pre-warmed pass starts at the write, as in P1.
- **Measurement:** times are wall clock from Enter. "First visible word" is the first text outside a tag (in A: the preamble
  if there was one, else the first word of pass 2).

## Failures

| Group | Requests | Malformed (protocol) | Leaked (protocol) | Missed (judgment) | Wrong args (judgment) | Spurious (judgment) |
| --- | --- | --- | --- | --- | --- | --- |
| one tool | 34 | 0 | 0 | 0 | 0 | 2 |
| two tools | 8 | 0 | 0 | 0 | 0 | 0 |
| **all** | **42** | **0** | **0** | **0** | **0** | **2** |

- **Protocol failures: 0 of 42.** Judgment failures: 2 of 42, both spurious `list_reminders` before a `cancel_reminder`
  (#17 "cancel the timer", #18 Swedish "ta bort påminnelsen om hyran"): the model listed first, then cancelled the right
  reminder in round 2. Harmless, and the 3-round loop covers it. In variant A these chains were the 2 failures because the
  harness stopped after one round, so the judgment counts are not like for like (A: 1 request with miss + spurious, 1 with a miss).
- Nothing was capped at 3 rounds; no infrastructure failures.
- Round 1 had text before the tag in 33 of 42 (the confirmation); the 9 without were exactly the 9 that started with `list_reminders`.
- First block was `thinking` in 31 of 42 round-1 passes (A: 40 of 50).

## Second passes

- **Second pass needed: 9 of 42 (21%)**, none needed a third. 7 were the requests that expected `list_reminders` (#10-14, #39,
  #41) and 2 were the list-before-cancel chains (#17, #18). **All 9 started with a `list_reminders` tag**; no list request
  finished in one pass. The other 33 requests used a single process.
- **The tool-failure path was not exercised:** no call returned an error in this run, so "second pass on failure" and the
  confirmation-before-result risk (she says "Done" before the tool runs) are untested.
- **Pre-warm on any tag:** 46 processes were spawned (42 in round 1, 4 in round 2), 9 were used, **37 (80%) were killed unused**,
  each a `claude` start (about 400-440 MB private while alive, D-056). Pre-warm was spawned on tags in 33 single-pass turns for nothing.
- Second pass, first word from its write: B (pre-warmed) 2272 / 3898 ms (median / p90) against A (cold) 2978 / 3920 on the same
  7 list requests; second pass total 2667 / 4195 against 3565 / 4422. About 0.7-0.9 s better at the median, n=7, within noise at p90.

## Timing from Enter, ms (median / p90)

| Subset | A first visible word | B first visible word | A final reply complete | B final reply complete |
| --- | --- | --- | --- | --- |
| All 42 requests | 5071 / 7484 | **2163 / 4274** | 6119 / 8539 | **2445 / 4617** |
| B single-pass (n=33) | 4949 / 8153 | 2008 / 2375 | 6119 / 9580 | 2377 / 2776 |
| B with a second pass (n=9) | 5738 / 6806 | 4274 / 6431 | 6189 / 7197 | 4617 / 6768 |

(A and B are separate runs on the same script; run-to-run noise is several hundred ms. Neither pass of either variant was pre-warmed
by typing time, so with a pre-warmed first pass both would be about 1 s faster, as in P1.)

## Reading

- Variant B wins: same protocol reliability (0 of 42), a plain tool turn costs one process, about 2.0 s to the first visible word
  and 2.4 s to the end, in line with an ordinary chat turn (P1: about 1.9 s without pre-warm). Only read-back turns cost two
  processes, about 4.3 s to the first word.
- Pre-warming on every tag is wasteful (80% of spawns unused). In this run every second pass began with a `list_reminders` tag,
  so spawning only on a read-back tag would have covered all 9 with none wasted. That narrower policy is derived from the data,
  not measured separately.
- The cost of B is that the confirmation is spoken before the tool runs. Untested here: what she does when a tool fails.
