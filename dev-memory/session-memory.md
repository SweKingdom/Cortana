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
