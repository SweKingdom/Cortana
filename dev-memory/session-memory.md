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
