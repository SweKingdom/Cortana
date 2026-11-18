# Step 3b: model comparison on the personality v2 and v2.1 prompts

Two runs. Run 1 used the v2 file and the messages below; run 2 (further down) used the revised v2.1 file and ten new
messages that do not overlap its examples. Each run has its own key.

**Run 1.** Run 2026-11-18 with `ModelCompare compare` (Claude Code 2.1.318). The prompt is the app's own v2 system prompt at flirt
level 4, built by `PromptBuilder` and `Transcript`, so it is what the app sends. Each call is one `claude` process,
pre-warmed and written to after 3 s of simulated typing (D-055). Order of the three setups was shuffled per message and
so was the letter order. Philip judged `out/blind.md` before the key was opened.

Setups: **Haiku low** = `haiku` with `--effort low` (what the app does, D-061); **Haiku default** = `haiku` at default
effort; **Sonnet** = `sonnet` at default effort. The aliases resolved to `claude-haiku-5-5` and `claude-sonnet-5-5`.

Sample: 10 messages, one run each, one judge. It shows how the replies read, not how often something happens.

## Run 1 (v2): timing and quota (per setup, 10 calls each)

| Setup | First word median / p90 / max | Thinking block first | Mood tag at the end | Fresh in / cache write / cache read / out tokens | API-equivalent cost |
| --- | --- | --- | --- | --- | --- |
| Haiku low | 534 / 605 / 1 529 ms | 1 of 10 | 10 of 10 | 20 / 7 882 / 26 186 / 1 235 | $0.0029 |
| Haiku default | 585 / 1 892 / 2 584 ms | 3 of 10 | 10 of 10 | 20 / 8 737 / 25 314 / 1 667 | $0.0033 |
| Sonnet | 1 019 / 1 522 / 2 129 ms | 5 of 10 | 10 of 10 | 20 / 12 526 / 21 339 / 1 383 | $0.0681 |

No errors, no leaked tool markup. The cost column is the API-equivalent figure Claude Code reports, a proxy: how the plan
weighs these tokens is not visible. Sonnet came to about 23 times the Haiku cost on it.

## Run 1 (v2): Philip's picks and what they were

| # | Message | Pick | Setup behind the pick | Philip's note |
| --- | --- | --- | --- | --- |
| 1 | greeting | A | Sonnet | |
| 2 | bug | A | Sonnet | |
| 3 | win | C | Haiku low | none really fit |
| 4 | loss | C | Sonnet | |
| 5 | rough day | A | Haiku default | |
| 6 | other AI | B | Sonnet | the repeated "hotshot" is bad |
| 7 | late night | C | Sonnet | |
| 8 | remember | B | Sonnet | |
| 9 | Swedish | none | | too generic, felt like Claude |
| 10 | code question | none | | felt like Claude, not Cortana |

Tally: Sonnet 6, Haiku default 1, Haiku low 1 (a weak pick), none 2. Letter-to-setup key per message is in
`out/sealed/key.json`.

Reading it (Claude's, not Philip's): on 10 messages with one judge this leans to Sonnet for voice, at about 23 times
the API-equivalent cost and about twice the first-word time (median 1 019 ms against 534 ms). The two "none" picks
(9 and 10) are the Swedish message and the code explanation, where every setup read as a generic assistant, so that
looks like a prompt problem and not a model problem. This does not change D-013 (Haiku default with escalation); it is
input to the v2.1 revision and the rerun.

## Run 1 (v2): bugs seen in `out/blind.md` (not fixed, noted for v2.1)

1. **9B (Haiku default) leaked a meta note**: the reply begins "Philip skriver på svenska, men jag svarar på engelska som
   vanligt." (in Swedish, addressed to nobody), then answers.
2. **9C (Haiku low) answered in Swedish** despite the language rule ("Always reply in English, even if Philip writes in
   Swedish").
3. **10B (Haiku low) saved a pointless memory about the conversation itself**: `remember` with "Philip asked Cortana to
   explain C# async/await in a few lines on a Wednesday afternoon". Nothing was worth keeping.

Also seen: in 6B the word "hotshot" is also a filler line and a v2 example line, and Philip found the repeat bad.

## Prompt size and first word (budget, D-075; measured on v2, before v2.1)

| Flirt level | v1 system tokens | v2 system tokens | v2 examples sent |
| --- | --- | --- | --- |
| 0 | 606 | 1 387 | 13 |
| 1 | 611 | 1 422 | 14 |
| 2 | 611 | 1 422 | 14 |
| 3 | 617 | 1 464 | 15 |
| 4 | 760 | 1 618 | 16 |

(`chars / 4`; the memory tool text is 173 tokens and is included.) Claude Code adds about 1 700 tokens of its own: a
call with no history measured 3 400 real input tokens against 1 731 estimated.

`perfcheck -ChatOnly` (10 real-flow, 5 cold), Haiku with `--effort low`:

| Run | Real flow median / p90 / max | Cold median | Thinking first | Filler shown |
| --- | --- | --- | --- | --- |
| v1 (main, same session) | 544 / 672 / 675 ms | 1 538 ms | 1 of 15 | 3 of 15 |
| v2 run 1 | 608 / 1 068 / 1 625 ms | 3 042 ms | 7 of 15 | 5 of 15 |
| v2 run 2 | 1 189 / 1 377 / 1 657 ms | 1 663 ms | 6 of 15 | 5 of 15 |

One run each; the spread between the two v2 runs is as large as the v1 to v2 gap. The thinking-first rate rose
consistently. A separate `history` mode test used repeated-character filler, which tokenises about three times worse than
prose, so its token counts (3 400 to 11 508 real input tokens) are too high; its first words were 546 ms with no history,
671 and 532 ms with 8 and 20 short messages, and 1 137 ms for 20 long ones (11.5 k tokens). Raw numbers in
`out/history-summary.txt` and `out/history.csv`.

Idle numbers from the full `perfcheck` on the v2 code (median of 3): 75.6 MB private, idle CPU 0.83 %, start to orb
476 ms. The idle CPU is well under the 1.9 to 2.0 % of step 3 because the machine state differs, not because of this
change; the margin note in the checklist stands.

## Files

- `Program.cs`, `ModelCompare.csproj`: the tool (`sizes`, `history`, `compare`). It reads the app's `PromptBuilder` and
  `Transcript` by reflection and runs `claude` in `%APPDATA%\Companion\brain\` (D-048).
- `out/blind.md`, `out/sealed/key.json`, `out/sealed/raw.jsonl`, `out/sealed/summary.txt`: the blind file, the key, the
  raw replies with usage, and the per-setup numbers.
- `out/history-*.txt`, `out/history.csv`, `out/sizes.txt`, `out/compare-log.txt`: the other measurements.

## Run 2 (v2.1): ten new messages

Same tool, same three setups, same blind format, new letter shuffle (seed 3003) and a new key. The personality file is
Philip's v2.1 (no catchphrases, no fixed nickname, opinions and moods, how she explains and talks about herself,
stricter language and memory rules, 19 examples). The messages do not overlap the examples: a greeting at 17:45, a
build error (CS0246), a small win, a game loss in overtime, a sad moment (grandmother's house sold), a Swedish message,
a mutex-against-semaphore question, a question about her (lonely when he is gone), a bad idea (quit and make a game
full time), and a late-night message at 02:10. Blind file: `out/run2/blind.md`; key: `out/run2/sealed/key.json`.

| Setup | First word median / p90 / max | Thinking block first | Mood tag at the end | Fresh in / cache write / cache read / out tokens | API-equivalent cost |
| --- | --- | --- | --- | --- | --- |
| Haiku low | 893 / 1 455 / 2 168 ms | 6 of 10 | 10 of 10 | 20 / 10 922 / 29 118 / 1 713 | $0.0044 |
| Haiku default | 1 329 / 2 320 / 2 678 ms | 9 of 10 | 10 of 10 | 20 / 8 879 / 31 163 / 2 940 | $0.0056 |
| Sonnet | 1 630 / 2 886 / 2 941 ms | 7 of 10 | 10 of 10 | 20 / 13 320 / 26 514 / 2 457 | $0.0827 |

No errors, no leaked tool markup. Compared with run 1 (v2, other messages) first words are slower on every setup and
Haiku low started with a thinking block on 6 of 10 instead of 1 of 10. The messages and the file both changed, so this
does not say which caused it; ten calls each is a small sample. Cost proxy as in run 1.

### Run 2 (v2.1): Philip's picks and what they were

| # | Message | Pick | Setup behind the pick | Philip's note |
| --- | --- | --- | --- | --- |
| 1 | greeting 17:45 | B | Haiku low | |
| 2 | build error CS0246 | C | Sonnet | B (Haiku default) close |
| 3 | small win | C | Sonnet | |
| 4 | game loss | A | Sonnet | |
| 5 | grandmother's house | C | Sonnet | |
| 6 | Swedish message | C | Sonnet | |
| 7 | mutex against semaphore | A | Sonnet | |
| 8 | about her (lonely) | C | Sonnet | |
| 9 | bad idea (quit for a game) | C | Sonnet | |
| 10 | 02:10 late night | A | Sonnet | |

Tally: Sonnet 9, Haiku low 1, Haiku default 0 (one near miss on 2), none 0. Letter-to-setup key per message is in
`out/run2/sealed/key.json`. Run 1 (v2) was Sonnet 6, Haiku default 1, Haiku low 1, none 2, so Sonnet is ahead in both
runs, and the two "none" picks of run 1 (Swedish and the code question) are gone: v2.1 fixed what looked like a prompt
problem. On voice, with one judge and 20 messages in all, Sonnet wins. It costs about 19 times the Haiku low
API-equivalent figure in run 2 ($0.0827 against $0.0044) and had the slowest first word (median 1 630 ms against 893 ms).
Haiku default had the highest thinking-first rate (9 of 10) and was never picked. This is input to the model choice
(D-013, Haiku default with escalation), not a change to it.

Notes from Philip on run 2, with the setup behind each:

1. **6B (Haiku low) is a real leak**: planning text at the start of the reply ("Give him a quick, decisive pick. Mood:
   teasing."). Addressed by the ReplyCleaner guard below.
2. **9A (Haiku default) breaks character**: "I've noted it, so you can undo that" talks about the UI. Its `remember` tag
   is stripped by the app, so the tag itself is not a leak.
3. **5A (Haiku default) shows the `remember` tag**: also stripped by the app, so not a leak.
4. Swedish answered in English is intended (D-015).

### Run 2: leak guard added to ReplyCleaner

`ReplyCleaner` now drops a leading paragraph that contains a "Mood:" label, or that talks about Philip in the third
person (he, him, his or Philip, with no "you" and not as a vocative like "Nice one, Philip."), and Conversation logs
`reply_leak_dropped` with the reason only. The first paragraph is held until it can be judged, which delays the first
visible word for replies that do not open with a "you"; the hold ends at the first "you", at a paragraph break, at 300
characters, or at the end of the reply. A reply that is only one such paragraph is kept and logged as `reply_leak_kept`.
The guard would have dropped 6B; 9B of run 1 ("Philip skriver på svenska, ...") is covered by the third-person rule.

## Real-app first word on v2.2 with the leak guard (perfcheck, not the blind harness)

`perfcheck -ChatOnly -ChatWarm 10 -ChatCold 5`, one run each:

| Chat model | Real flow median / p90 / max | Cold median | Thinking first | Filler shown |
| --- | --- | --- | --- | --- |
| Sonnet (default, D-076) | 3 016 / 4 246 / 6 314 ms | 3 977 ms | 15 of 15 | 14 of 15 |
| Haiku `--effort low` | 937 / 2 274 / 2 627 ms | 2 799 ms | 6 of 15 | 7 of 15 |
| Sonnet `--effort low` (`COMPANION_CHAT_EFFORT=low`) | 1 931 / 3 719 / 3 833 ms | 2 873 ms | 11 of 15 | 13 of 15 |

Sonnet at its default effort is over the ~2 s first-word budget in the app; the blind harness (1 630 ms median, 7 of 10 thinking first on v2.1) understated it. Sonnet with `--effort low` came to 1 931 ms median, just under the budget (PASS by 69 ms, one run of 10, p90 3 719 ms), with a thinking block first on 11 of 15 turns instead of 15 of 15, and a filler on 13 of 15. Cold turns 2 873 ms against 3 977 ms.

## Run 3 (v2.2): Sonnet default effort against Sonnet `--effort low`

`ModelCompare compare3`, same blind format, new sealed key. Five new messages (a morning greeting, a 40-file merge conflict, reaching diamond in ranked, "Rust or C#?", feeling lonely at 00:40), two setups, letters A and B shuffled per message (seed 3004). The personality is the v2.2 default at flirt level 4. Blind file: `out/run3/blind.md`; key: `out/run3/sealed/key.json` (not opened when this section was written).

| Setup | First word median / p90 / max | Thinking block first | Mood tag at the end | Median reply chars | Fresh in / cache write / cache read / out tokens | API-equivalent cost |
| --- | --- | --- | --- | --- | --- | --- |
| Sonnet default | 3 104 / 4 613 / 4 613 ms | 4 of 5 | 5 of 5 | 383 | 10 / 3 891 / 16 423 / 1 774 | $0.0359 |
| Sonnet low | 2 899 / 3 499 / 3 499 ms | 4 of 5 | 5 of 5 | 338 | 10 / 4 310 / 16 001 / 1 635 | $0.0361 |

No errors, no leaked tool markup. Both resolved to `claude-sonnet-5-5`. Five calls each: the timing difference (200 ms) is inside the noise; in this harness `--effort low` did not change the thinking-first count, while in the app's real flow it went from 15 of 15 to 11 of 15. Quota proxy is the same for both.

### Run 3: Philip's picks and what they were

| # | Message | Pick | Setup behind the pick | Philip's note |
| --- | --- | --- | --- | --- |
| 1 | greeting 09:30 | A | Sonnet default | close |
| 2 | merge conflict | B | Sonnet default | |
| 3 | diamond in ranked | B | Sonnet low | |
| 4 | Rust or C# | A | Sonnet low | |
| 5 | lonely at 00:40 | A | Sonnet low | |

Tally: Sonnet low 3, Sonnet default 2 (by letter A won 3 to 2 as well). Philip: nearly a tie; B had the most character-specific lines. Decision: Sonnet `--effort low` is the chat default (D-076).

Philip's note for the next personality pass: her most in-character lines are about living in his machine ("I live in there"), dry self-awareness about being an AI, pushing back on his motives, and never coddling; strengthen those in the examples. Copied to the checklist.

## Final check with the default (Sonnet low) as the code default

`perfcheck -ChatOnly` with no override variables, final code: first word real flow 2 291 ms median / 3 322 p90 / 4 666 max, cold 3 325 ms, reply complete 2 891 / 4 001, thinking first 11 of 15, filler 11 of 15, mood tag 15 of 15, app 95.4 MB after chat, tree at rest 95.4 MB, 0 claude at rest. The first Sonnet-low run was 1 931 ms, so one run passes and one misses the 2 s limit by 291 ms.
