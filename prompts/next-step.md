# Start prompt for each build step

Run Claude Code from this folder in PowerShell (not WSL). Start in plan mode
(Shift+Tab until it says plan mode), paste the prompt, replace N with the step number
from dev-memory/checklist.md. Use /clear between steps.

---

Read CLAUDE.md, docs/architecture.md and everything in dev-memory/. Do step N in
dev-memory/checklist.md only. Plan first: list the files you'll create or change and
how you'll meet the done criteria, and flag any conflict with decisions.md instead of
changing it. Wait for my approval before writing code. Work on a branch named
step-N-<short-name>. Measure the done criteria with tools/perfcheck.ps1 (once it
exists) and report the numbers. At the end, propose updates to dev-memory/ and wait
for my approval before writing them.

---

## For the prototypes (before step 2)

Read CLAUDE.md, docs/architecture.md and everything in dev-memory/. Build prototype
P1 (and then P2) exactly as described in dev-memory/checklist.md, in prototypes/P1
(prototypes/P2). It is throwaway code. Report the measurements as a table and a
recommendation for D-037, then propose a decision update and wait for my approval.
