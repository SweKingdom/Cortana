# Rules for Claude Code sessions in this repo

This repo is Cortana Desktop: a private, lightweight Windows desktop AI companion
(C# + WPF, `net10.0-windows`). Only Philip uses it.

## Sources of truth

- `docs/architecture.md`: the design (exported from the claude.ai doc; this copy is
  what you work from).
- `dev-memory/decisions.md`: every decision with ID, status and reason. Binding.
- `dev-memory/checklist.md`: the build steps and done criteria.
- `dev-memory/session-memory.md`: where the last session left off.

## Start of session

1. Read the four files above before doing anything else.
2. Check the latest session entry against the actual repo: branch, last commit,
   uncommitted changes. Say if they disagree. Don't change Git state to check.
3. Give Philip a short summary: where we are, what's uncertain, the next checklist step.
4. Work on **one checklist step per session**. Plan first and wait for approval
   before writing code.

## During work

- Accepted decisions are binding. If a request or your own idea conflicts with one,
  name it (D-NNN) and ask. Never change a decision silently, and never reintroduce a
  superseded one (e.g. Gemini as default brain, .NET 8, one persistent claude process).
- Respect the performance budget (D-040). Say what any new dependency costs before adding it.
- Never send anything from explicit mode to a non-loopback host (D-039).
- The app's own `claude` brain process must run in `%APPDATA%\Companion\brain\`, never in
  this repo (D-048).
- Keep keys out of code, logs and commits. Never ask Philip to paste keys into the chat.
- Capri code: port only, keep its Required Notice lines in NOTICE.md (D-024). Never copy its art.
- Prototypes (P1–P5) live in `prototypes/` and are throwaway code, not part of the app.
- Build and run on Windows (PowerShell). Report done criteria with numbers from
  `tools/perfcheck.ps1`. Philip judges anything visual himself.

## End of session

- Propose updates to the three dev-memory files: new or superseded decisions,
  a dated session entry, checklist changes.
- Write them only after Philip approves. If he declines, leave the gap visible.
- Past approval never counts as approval for a new action.
- If the design changed, say so: Philip updates the claude.ai doc to match `docs/architecture.md`.
