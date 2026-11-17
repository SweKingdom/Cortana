# Session memory

Owns where we are right now. Decisions live in decisions.md, next steps in
checklist.md; this file only points to them.

Rules:
- One dated entry per session, newest at the bottom. Never rewrite old entries.
- Each entry: date, who, branch/commit (if code exists), what changed,
  what's uncertain, last confirmed action.
- An unchanged commit doesn't mean an unchanged working tree: note uncommitted changes.

---

## 2026-11-16 — design phase (Philip + Claude)

- Repo: SweKingdom/Cortana (private). No code yet.
- Changed: architecture doc complete through drift guards; decisions D-001 to D-027 recorded.
- Uncertain: hotkey, voice provider, local model (see decisions.md "Open").
- Last confirmed action: Philip approved adding governance/drift guards.
- Next: see checklist.md.

## 2026-11-16 — counter-review applied (Philip + Claude)

- Repo: still no code.
- Changed: external review and counter-review (https://claude.ai/artifact/Pz1uV2EmpShErqMDnYdVqk); Philip approved its conclusions. Added D-036 to D-047; superseded D-001, D-002, D-005, D-028, D-034. Architecture doc updated to match.
- Uncertain: integration mode for Claude Code (P1), text tool protocol reliability (P2).
- Last confirmed action: Philip approved applying the counter-review's minimal changes.
- Next: step 0, then step 1 with P3/P4.

## 2026-11-16 — starter package (Claude, approved by Philip)

- Repo: SweKingdom/Cortana cloned (empty, branch main, no commits) into C:\Users\phili\VSCode\Cortana.
- Added, uncommitted: CLAUDE.md, README.md, NOTICE.md, .gitignore, docs/architecture.md, dev-memory/, prompts/next-step.md. Added D-048 (brain process runs in its own folder).
- Uncertain: nothing new.
- Last confirmed action: Philip approved creating the folder here and using this repo.
- Next: Philip makes the first commit; then step 0 (WPF project on net10.0-windows) with Claude Code.

## 2026-11-16 — step 0, repo setup (Claude, approved by Philip)

- Branch: step-0-repo-setup, merged into main and pushed (see git log for the commit).
- Added: Cortana.sln, src/Companion/ (Companion.csproj on net10.0-windows with H.NotifyIcon.Wpf 2.4.1 and System.Speech 10.0.15, App, placeholder MainWindow that step 1 replaces).
- Measured: Release build 0 warnings/0 errors; app starts and exits cleanly; tray icon created; System.Speech lists en-US and sv-SE voices and synthesizes silently; framework-dependent publish 1.42 MB (packages add about 1.3 MB). perfcheck.ps1 does not exist yet (step 1).
- Found: the previous entry said the starter files were uncommitted, but commit 79e1128 already existed and the tree was clean.
- Doc fixes approved by Philip: docs/architecture.md line 173 now defers process mode to P1 (D-037); line 567 now says D-001 to D-048. Philip has updated the claude.ai doc to match.
- Date correction, approved by Philip: earlier entries and files used 2026-11-17 by mistake; today is 2026-11-16. Every ISO 2026-11-17 in dev-memory/ and docs/ was changed to 2026-11-16 (this includes past entries and decision rows, by his instruction). The two non-ISO spellings ("Nov 17, 2026" at architecture.md line 5, "17 nov 2026" at line 455) were changed to 16 in a follow-up commit, also by his instruction.
- Uncertain: nothing new. D-049 (package versions, .sln) skipped by Philip; versions live in the .csproj.
- Last confirmed action: Philip approved the dev-memory updates, doc fixes and date correction, and asked for commit, merge to main and push.
- Next: step 1 (window and orb, P3/P4).

## 2026-11-16 — step 1, window and orb (Claude, approved by Philip)

- Branch: step-1-window-orb, committed, merged into main and pushed at Philip's instruction (see git log for the commit).
- Added: Character/ (CharacterWindow, OrbControl, OrbVisual, OrbAnimator), Core/ (Settings, GlobalHotkey), Native/NativeMethods.cs, app.manifest (Per Monitor V2), tray icon, tools/perfcheck.ps1, prototypes/P3, P3b, P4. Removed the step 0 placeholder MainWindow. Window is 168x168 with a 152 px invisible grab ring; the glow ends at the ring so nothing outside it swallows clicks.
- Decisions: D-049 grab ring; D-050 software rendering default; D-051 animation by activity (supersedes D-006); D-052 budget with idle CPU <2% (supersedes D-040); D-053 panic hotkey Ctrl+Alt+H. The ID D-049 was earlier mentioned as skipped (package versions, .sln) but never written, so it is now the grab ring.
- Measured (perfcheck, median of 3, 10 fps, software rendering): private bytes 75.4 MB (limit 150), idle CPU 1.30% (limit 2%), start to orb 507 ms (limit 1 s), tree 75.4 MB. First word n/a until step 2.
- Findings: a bare WPF window is ~395 MB private with hardware rendering on this PC (GPU driver) and ~65 MB in software. No frame rate gets idle CPU under 0.5% with a continuously breathing orb (P3). Pre-rendered bitmaps with a timer (P3b) were no cheaper (1.64% vs 1.77%, noise) and used ~10 MB more, so the Storyboard was kept. First breathing bug: animating the ScaleTransform object directly did nothing; animating via the element's property path works.
- Confirmed by Philip by hand: drag, Ctrl+Alt+H, tray menu, position restore, single instance. Not done: P4 game test (checklist item left open).
- Uncertain: behaviour over a borderless game (P4); thinking/talking 30 fps and sleeping/game-mode stop are only decided, not built (steps 3 and 4); the memory result depends on this PC's GPU driver; GPU/dwm cost is not measured by D-052; saved window positions from before the resize may sit slightly off.
- Design changed: docs/architecture.md was updated (CPU budget, window size, software rendering, animation by activity, grab ring, P3b note, project tree, D-053 reference). Philip updates the claude.ai doc to match.
- Last confirmed action: Philip decided D-050 to D-053 and asked for the doc updates, commit, merge and push.
- Next: P4 game test (Philip), then prototypes P1 and P2, then step 2.

## 2026-11-16 — step 1 follow-up, smoother breathing (Claude, approved by Philip)

- Branch: step-1-orb-smoothing, committed, merged into main and pushed at Philip's instruction (see git log for the commit).
- Problem: Philip saw only ~4 steps per half cycle at 10, 15 and 20 fps. Verified: COMPANION_FPS does change the rendered rate (10/15/20 distinct frames per second); the core radius moved only 4 px per half cycle and layout rounding/pixel snapping made the edge jump in whole pixels (4 distinct widths). Fix: rounding and snapping off, bigger amplitude, glow brightness pulse in sync (sine ease in-out was already in place).
- Decision: D-054 refines D-051 (10 fps, amplitude 1.14, 4 s, glow opacity 0.55-0.90). Values are named constants in Character/OrbTuning.cs. Temporary switches COMPANION_AMP, COMPANION_GLOWPULSE, COMPANION_TICKS_FILE removed; COMPANION_FPS, COMPANION_RENDER and COMPANION_PERF_FILE stay (README).
- Measured (perfcheck, median of 3, 10 fps, software): private bytes 75.7 MB, idle CPU 1.38% (limit 2%), start to orb 511 ms. Comparison table in prototypes/P3c/results.md. 15 fps cost 3.1% with the pulse (over budget) and its animation clock ticks ~54 times a second.
- Uncertain: run-to-run CPU noise is ~0.4 points, so 1.38% leaves little margin under 2% when thinking/talking and other work is added later; P4 game test still open; whether 1.14 still looks right on other displays/DPI.
- Design changed: docs/architecture.md updated (idle animation description, window size note, OrbTuning in the project tree, D-054 reference). Philip updates the claude.ai doc to match.
- Last confirmed action: Philip chose option C and asked for the default, the decision, the constants file, removal of the temporary switches, a perfcheck run, then commit, merge and push.
- Next: P4 game test (Philip), then prototypes P1 and P2, then step 2.

## 2026-11-17 — prototype P1, Claude Code process mode (Claude, approved by Philip)

- Branch: p1-claude-process-modes, pushed to origin with a pull request; Philip merges on GitHub (new rule in CLAUDE.md, replaces local merges). Deleted the merged local branches step-1-window-orb and step-1-orb-smoothing. Repo at start matched the last entry (main, f17aba1, clean).
- Added: prototypes/P1/ (console harness, README, results.md, raw CSVs from two runs and the pre-warm run). Updated Claude Code 2.1.49 -> 2.1.318 (winget) before measuring; `--safe-mode`, `--system-prompt-file`, `--append-system-prompt-file`, `--disable-slash-commands`, `interrupt` and `set_model` all exist and work there.
- Measured (Haiku, 20 turns each, two runs, 0 failures): first word per message 1925 ms median / 2565 p90 / 3004 max; per session 500 / 1121 / 1397; pre-warmed per message with 3 s of typing 787 / 1131 (10 turns). `claude` tree 400-440 MB private bytes (207-228 MB working set) in every mode; none at rest per message, ~435 MB at rest per session. Fresh input tokens over 20 turns 32 464 (per message) vs 3 107 plus 39 398 cache reads (per session). Interrupt 5/5 and `set_model` work in both.
- Decisions: D-055 (one process per message, pre-warm, no extended thinking for Haiku, minimum version 2.1.318) and D-056 (tree limit applies at rest) accepted by Philip. D-037 stays ACCEPTED and is resolved by D-055.
- Uncertain: no tested switch disables Haiku thinking (`MAX_THINKING_TOKENS=0`, `CLAUDE_CODE_DISABLE_THINKING=1`, `--thinking disabled`, `--effort low`, `alwaysThinkingEnabled=false` each still produced a thinking block on a 400-word prompt, one sample each); ordinary chat turns had none, so it may only matter for long creative prompts. Not measured: how the plan weighs fresh vs cached tokens (per-message mode sends about 10x more fresh input), pre-warm over longer chats, P2 tool reliability. P4 game test still open.
- Design changed: docs/architecture.md updated (ClaudeCodeBrain process mode, pre-warm, flags and minimum version, interrupt, RAM at rest, P1 marked done, D-055/D-056). Philip updates the claude.ai doc to match.
- Last confirmed action: Philip accepted D-055 and D-056, asked for the pre-warm measurement, the doc updates, a commit, and (per the new rule) a pushed branch with a pull request.
- Next: P2 (tool-call reliability), P4 game test (Philip), then step 2.

## 2026-11-17 — prototype P2, text tool protocol (Claude, approved by Philip)

- Branch: p2-tool-call-reliability, pushed to origin with a pull request; Philip merges on GitHub. Repo at start matched the last entry (main, 3b7175d = merge of PR #1, clean).
- Added: prototypes/P2/ (console harness, README, results.md, raw CSV/JSONL for variants A and B, self-test log). No new dependencies.
- Variant A (50 requests, 92 processes, Haiku, Claude Code 2.1.318): 0 malformed, 0 leaked over 51 tags; judgment failures 2 (list-then-act chains cut off by the one-round harness: #17, #41); first visible word 5071 ms median / 7484 p90, final reply 6119 / 8539 (a second process for every tool call). A self-test of 11 handcrafted bad outputs confirmed the detectors flag them.
- Variant B, requested by Philip (42 tool requests, one run): 0 malformed, 0 leaked, 0 missed, 0 wrong arguments, 2 spurious (`list_reminders` before a cancel). Second process in 9 of 42 (21%), all starting with `list_reminders`. First visible word 2163 / 4274, final reply 2445 / 4617. Pre-warming on any tag wasted 37 of 46 spawned processes.
- Decision: D-057 accepted by Philip (text protocol, strict parsing, up to 3 rounds, `ReplyCleaner`) with the variant B loop design that won. One refinement is mine and goes beyond what was measured: pre-warm only on a read-back tag instead of every tag; Philip can veto it when reviewing the PR.
- Uncertain: the tool-failure path was never exercised (no call failed), so confirmation-before-failure is untested; one model (Haiku), one run per variant, single-message requests without history; run-to-run noise is several hundred ms; Sonnet not tested; the narrower pre-warm policy is derived, not measured. Haiku started 40 of 50 first passes with a thinking block (+0.7 s first word): the D-055 thinking question is still open and now matters for step 2. P4 game test still open.
- Design changed: docs/architecture.md updated (tool protocol decided and the loop design, thinking note, P2 marked done, step 5 loop, D-001 to D-057). Philip updates the claude.ai doc to match.
- Last confirmed action: Philip approved D-057 and asked for variant B, the decision with the winning loop design, the dev-memory and architecture updates, a commit, a pushed branch and a pull request.
- Next: step 2 (chat with Claude), P4 game test (Philip).

## 2026-11-17 — step 2, chat with Claude (Claude, approved by Philip)

- Branch: step-2-chat (created from main f69ff66 after Philip deleted the merged p1/p2 branches), pushed to origin with a pull request; Philip merges on GitHub. Repo at start matched the last entry.
- Added: text input (Ctrl+Alt+Space or click on the orb), speech bubble, `ClaudeCodeBrain` (one process per message, pre-warm, streaming, recovery once then "offline"), `Conversation` (TurnId on every reply event, history, quota, fillers), `ReplyCleaner`, `ModeSession`, EventBus, `Quota`, `History`, `Log`, minimal thinking orb (purple, 30 fps), `tests/Companion.Tests` (xUnit, 40 tests), perfcheck `-Chat`/`-ChatOnly`, dev hooks COMPANION_PERF_CHAT, COMPANION_PERF_TYPING_MS, COMPANION_PERF_CHAT_FILE. New dependencies: only the test project's xunit packages (not in the app).
- Measured (final code, `perfcheck -ChatOnly`, 10 real-flow + 5 cold turns): first word real flow 595 ms median / 1161 p90 / 1397 max, cold 1601 / 2122; app 92.7 MB private, tree at rest 92.7 MB (0 claude processes), peak 519 MB while claude runs; thinking block first on 5 of 15 turns, filler on 4 of 15 (0 of 10 real-flow). Idle CPU 1.8-2.0% in three sets, main 1.98% the same day.
- Decisions: D-058 fillers (rule changed from "1.2 s or a thinking block + 0.4 s" to only "no text 1.5 s after Enter", after 11 of 15 turns flashed a filler), D-059 chat hotkey, D-060 persistence files, D-061 `--effort low` for Haiku only (option a; 3-message sample, long prompts may still think).
- Lessons: the first thinking probe hung for hours and the retry failed silently because Windows PowerShell 5.1 has no `ProcessStartInfo.ArgumentList` (use an `Arguments` string there); every probe call now needs a hard timeout and per-call output. The `haiku` alias now resolves to `claude-haiku-5-5`, so P1/P2 numbers were possibly measured on another model version.
- Uncertain: Philip has not judged the bubble, input or thinking tint; I did not check click vs drag, Esc, interrupt or panic during a reply by hand (only claude-not-installed was tested automatically); idle CPU margin under 2% is gone (noise ~0.4 points); `--effort low` effect on reply quality and on long prompts; cold turns (no pre-warm) show a filler on most turns; P4 game test still open.
- Design changed: docs/architecture.md updated (project tree, IBrain Thinking/PreWarm, events, fillers paragraph, `--effort low`, hotkeys decided, step 2 marked done, D-001 to D-061). Philip updates the claude.ai doc to match.
- Last confirmed action: Philip approved D-058, D-059, D-060, D-061 (option a: `--effort low` for Haiku only), the checklist, this entry and the architecture changes, and asked for a commit, a pushed branch and a pull request with all numbers.
- Next: step 3 (personality, mood and memory): move the fillers and the temporary persona into the personality file. P4 game test (Philip).

### Follow-up on step-2-chat, same day (Claude, requested by Philip after his manual tests, which all passed)

- Added D-062 (bubble stays max(8 s, 90 ms/char) after the last character is shown; hover pauses the countdown; a click pins it until clicked again or a new message is sent) and D-063 (typewriter display: 45 cps, +0.5 cps per buffered character beyond 60 up to 200 cps, 260 ms after . ? !, 110 ms after , ; :, first character at once, timer only while text is waiting). Constants in `Character/ChatTuning.cs`; pacing is pure logic in `TypewriterPacer` with 13 new unit tests (53 total); `ReplyTypewriter` owns the timer.
- Behaviour choices I made beyond the wording (Philip can veto in the PR): on interrupt the text shown so far stays on screen with the normal countdown (before, the bubble was hidden); a pinned finished bubble stays while the input is open and goes when a message is sent; the history still stores everything the brain produced before an interrupt, which can be more than was shown.
- Measured (full `perfcheck -Chat`): idle CPU 1.93% (1.93 / 1.56 / 2.01), private 75.6 MB, start 511 ms; first word real flow 676 / 978 / 1129 ms, cold 1440 / 2053; app 91.5 MB after chat, peak 514 MB; fillers 2 of 15. The idle runs do not exercise the typewriter timer; it is stopped whenever nothing is waiting, which holds by construction and is not separately measured.
- Uncertain: how the pace, pauses, hover and pin feel (Philip judges); a pause after a full stop that ends a chunk is carried over, but a "3." / "5" split across chunks would pause once wrongly (rare); idle CPU margin under 2% still almost gone.
- Design changed: docs/architecture.md (project tree, bubble paragraph, new typewriter paragraph, pinned bubble note, D-001 to D-063). Philip updates the claude.ai doc to match.

### Second follow-up on step-2-chat, same day (Claude, requested by Philip before merging)

- D-063 changed: pace is now speech pace (16 cps; catch-up only beyond 250 buffered characters, +0.2 cps per character, cap 40 cps; pauses 400 ms after . ? !, 180 ms after , ; :), because 45 cps was too fast. New: a click on the bubble while text is still being revealed shows the rest at once (and the rest of that reply unpaced); a click on a finished bubble still pins. New rule, also in D-063 (refines D-060): on interrupt, history gets only the text actually shown in the bubble plus `[interrupted]`, never text Philip did not see (`Conversation.ShownText`, `ShownText` class). Philip then decided: a reply that finishes while the input box is open is shown in the bubble (typing interrupts it as usual), and the click behaviour stays (the rest of that reply is unpaced). Because a finished reply is saved in full before the slow reveal ends, cutting the reveal short (typing, new message, panic) now trims the history entry to the shown part plus `[interrupted]` (`Conversation.TrimLastReply`, `History.ReplaceLastReply` rewrites the last file line). While a reply runs or is revealing, opening the input keeps the bubble; clicking the orb during the reveal interrupts it. Philip added a step 4 item: a text speed slider in the Character tab for the base pace and bubble lifetime (note on D-063, checklist step 4).
- Not checked by Claude: the bubble may overlap the input box when the input opens above the orb (near the bottom of the screen); Philip judges.
- Tests: 70 (was 53): updated pace tests, Flush, `ShownText`, history-on-interrupt, `TrimLastReply` and `ReplaceLastReply`.
- Design changed: docs/architecture.md (typewriter paragraph, project tree). Philip updates the claude.ai doc to match.

## 2026-11-17 — step 3, personality, mood and memory (Claude, approved by Philip)

- Branch: step-3-personality (created from main 10eb16e, the merge of PR #3 = step 2, which the previous entry did not record; repo was clean and matched), pushed to origin with a pull request; Philip merges on GitHub.
- Added: `Brain/Personality.cs` + embedded `default.personality.normal.json`, `PromptBuilder` as an instance (flirt level, boundaries, examples, memory, token budget), `Memory/MemoryStore.cs`, `Brain/ToolTagParser.cs`, `Brain/Summarizer.cs`, `Core/Mood.cs`, `ReplyCleaner.ToolTags`, `MemoryProposal`/`MemoryFull` events and the mood on `ReplyDone`, `History.FoldOldest`, Undo/notes in `SpeechBubble`, Expression and talking in `OrbAnimator` (thinking particles, palettes in `OrbVisual`), tray "Flirt level" submenu, dev switch `COMPANION_DEV=1` (Preview submenu), `COMPANION_PERF_PINNED` + `perfcheck -Pinned`, probe fields for mood, notes, CPU while talking and the summary. No new dependencies. Docs: both stale "decided in step 2" sentences in `docs/architecture.md` fixed (lines 343 and 352; the D-055 decision row still says it and is not edited), plus tree, events, memory, look and step 3 status.
- Decisions: D-064 to D-069 accepted by Philip (`remember` in step 3, memory-full behaviour, background summary, flirt level source, mood look with the 30 fps rule, example swap).
- Measured: see the step 3 entry in checklist.md. Plain idle CPU 1.85% in the full run and 2.14% in a later run of the same code path, pinned flirty bubble 1.98%: the 2% limit has no margin on this PC.
- Changed after review: Philip asked that 30 fps apply only while text is revealed; the flirty pulse, happy bounce and teasing blink now run at 10 fps (first version held flirty at 30 fps for as long as the bubble stayed, so a pinned bubble would have held it forever).
- Lessons: bash heredocs with quotes broke twice, so multi-line patches now go through a script file in the scratchpad; a first version of the tool-tag cleaner left a double space where a tag stood mid-reply (fixed, tested); a cap test passed or failed on the size of the note, not the cap.
- Uncertain: how the new look feels (Philip judges, the 10 fps bounce and blink especially); the Undo link, the tray flirt submenu and the memory-full line were not clicked by hand; one of 16 replies lacked a mood tag (the history keeps no tags, so she may drift; revisit if it grows); a `remember` tag that fails to parse is only logged (repair is step 5); the summary only ran once, in the probe; P4 game test still open.
- Design changed: docs/architecture.md updated (personality file, flirt source, look, tree, events, memory and summary, step 3 status, D-001 to D-069). Philip updates the claude.ai doc to match.
- Last confirmed action: Philip accepted D-064 to D-069, asked for the 30 fps rule with a pinned-bubble measurement recorded in D-068, then the dev-memory updates, commit, push and pull request.
- Next: step 4 (settings and game mode); P4 game test (Philip).

### Follow-up on step-3-personality, same day (Claude, requested by Philip before merging PR #4)

- Four changes, planned first and approved with answers: **D-070** mood persists between messages (held until a different tag; thinking = purple particles over the held colour; 10 min of quiet, then a 60 s fade to idle blue at 10 fps; one one-shot timer, `Character/MoodHold.cs`); **D-071** the chat input stays open after sending (Esc = close and clear, Ctrl+Alt+Space = toggle, click outside or on the orb = hide and keep the draft; bubble clicks never close it; pre-warm re-armed on the first keystroke after a send); **D-072** the summary runs when the box is empty, open or closed, after 15 s of quiet, and pre-warm on open still wins over it (Philip's choice); **D-073** typing no longer interrupts a running reply, only Enter, a click on the orb and panic do (`Character/InputRules.cs`).
- Code: `ChatInput` is one window shown and hidden, `ChatController` (`ToggleInput`, arm and draft handling, arm when a reply ends if the box holds text), `OrbAnimator` (`FadeExpression`, thinking keeps the colour), `Conversation` (`SetDraft`, `PreWarm` idempotent), purple particles in `OrbVisual`, probe types the first key (`ProbeType`). 161 tests (146 before: `MoodHold`, the draft and summary rules, `InputRules`).
- Bug found by the chat run, fixed: the first re-arm used `long.MinValue` as "never armed", so `TickCount64 - MinValue` overflowed and no message after the first was pre-warmed (first word 1429 ms, the same as cold). Fixed with a far-back constant (now `InputRules.NotArmed`, tested at several uptimes).
- Measured: held flirty idle CPU 1.93% (1.93 / 1.64 / 2.01), plain 1.98% (1.64 / 1.98 / 2.01), private 77.0 and 76.0 MB (these idle runs were taken before the arm fix and the D-073 change, which do not touch the idle path). Final chat run: first word real flow 639 ms median (p90 1014, max 1043), cold 1573 ms; app 90.3 MB after chat, tree at rest 90.3 MB with 0 `claude`; mood tag on 16 of 16 replies; talking CPU median 6.7%; summary folded 12 messages in 2.2 s.
- Not checked by Claude (by hand): the 10 minute hold and the fade (only the timer logic is tested, with a fake clock), a click outside and a click on the orb hiding the box (the orb click is guarded against reopening for 400 ms), bubble clicks not taking focus (relies on the popup being non-activating), typing while a reply runs, the look of purple particles over each colour. Philip judges.
- Uncertain: the idle CPU margin under 2% is still within noise; a keystroke while a reply runs skips the pre-warm until that reply ends, so a message sent before it ends starts cold.
- Design changed: docs/architecture.md (look, input, summary, D-001 to D-073). Philip updates the claude.ai doc to match.
