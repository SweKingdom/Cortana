# Session memory

Owns where we are right now. Decisions live in decisions.md, next steps in
checklist.md; this file only points to them.

Rules:
- One dated entry per session, newest at the bottom. Never rewrite old entries.
- Each entry: date, who, branch/commit (if code exists), what changed,
  what's uncertain, last confirmed action.
- An unchanged commit doesn't mean an unchanged working tree: note uncommitted changes.

---

## 2026-11-17 — design phase (Philip + Claude)

- Repo: SweKingdom/Cortana (private). No code yet.
- Changed: architecture doc complete through drift guards; decisions D-001 to D-027 recorded.
- Uncertain: hotkey, voice provider, local model (see decisions.md "Open").
- Last confirmed action: Philip approved adding governance/drift guards.
- Next: see checklist.md.

## 2026-11-17 — counter-review applied (Philip + Claude)

- Repo: still no code.
- Changed: external review and counter-review (https://claude.ai/artifact/Pz1uV2EmpShErqMDnYdVqk); Philip approved its conclusions. Added D-036 to D-047; superseded D-001, D-002, D-005, D-028, D-034. Architecture doc updated to match.
- Uncertain: integration mode for Claude Code (P1), text tool protocol reliability (P2).
- Last confirmed action: Philip approved applying the counter-review's minimal changes.
- Next: step 0, then step 1 with P3/P4.

## 2026-11-17 — starter package (Claude, approved by Philip)

- Repo: SweKingdom/Cortana cloned (empty, branch main, no commits) into C:\Users\phili\VSCode\Cortana.
- Added, uncommitted: CLAUDE.md, README.md, NOTICE.md, .gitignore, docs/architecture.md, dev-memory/, prompts/next-step.md. Added D-048 (brain process runs in its own folder).
- Uncertain: nothing new.
- Last confirmed action: Philip approved creating the folder here and using this repo.
- Next: Philip makes the first commit; then step 0 (WPF project on net10.0-windows) with Claude Code.
