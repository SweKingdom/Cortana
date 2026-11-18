# CPU investigation, 2026-11-18

Question: idle CPU swung between 0.83% and 2.55% on the same code. Is the measurement noisy, or did a step add cost?

## Setup

- One session, one PC: Intel Core Ultra 9 185H (22 logical processors), Windows 11, **on AC power, Acer power plan**.
- 3 rounds x 6 variants = 18 runs, **rotated** (every variant once per round, the starting variant shifts each round), 30 s settle + 60 s measured, 10 fps, software rendering.
- Variants:
  - `baseline-bare`: `tools/perfbaseline --bare`, an empty window with the app's window setup (layered, transparent, topmost, 168x168, software rendering).
  - `baseline-static`: `--static`, the same window with the orb drawn once by the real `OrbVisual`, no Storyboard.
  - step 1 `f17aba1`, step 2 `10eb16e`, step 3 `e487da3`, main `a2ef1a0`: Release builds from git worktrees.
- Only the process's own CPU is measured (no dwm, no GPU). The rest of the PC used 6 to 51% of a core during the runs, except one run (step 2, round 3) at 92%.
- Raw record: `run1.log` (the console output, every run). The run's JSON was lost by mistake (deleted by a broad cleanup in the untracked `perf-results/`); thread counts and working set are the only things the log lacks.

## Results (idle CPU, % of one core)

| Variant | Runs | Median | Min-max | Median minus bare | Median minus static orb | Private MB |
| --- | --- | --- | --- | --- | --- | --- |
| baseline-bare | 0.00 / 0.00 / 0.00 | 0.00 | 0.00-0.00 | - | - | 64.6 |
| baseline-static orb | 0.08 / 0.05 / 0.00 | 0.05 | 0.00-0.08 | 0.05 | - | 64.8 |
| step 1 (f17aba1) | 1.71 / 1.87 / 1.77 | 1.77 | 1.71-1.87 | 1.77 | 1.77 | 76.3 |
| step 2 (10eb16e) | 2.29 / 2.18 / 1.77 | 2.18 | 1.77-2.29 | 2.18 | 2.13 | 76.8 |
| step 3 (e487da3) | 2.13 / 2.08 / 1.92 | 2.08 | 1.92-2.13 | 2.08 | 2.03 | 76.5 |
| main (a2ef1a0) | 1.72 / 1.87 / 2.63 | 1.87 | 1.72-2.63 | 1.87 | 1.82 | 76.9 |

(Runs are in round order; start to orb was 429 to 613 ms in all runs.)

## What it shows

- **The window and the orb drawing cost about nothing** (bare 0.00%, static orb 0.05%). Idle cost is the breathing animation (plus whatever else the app does while idle, which was not separated from it).
- **No step is clearly more expensive.** The ranges overlap completely. Step 2 read 1.77 in round 3 (disturbed: the rest of the PC was at 92%) and main read 2.63 on a quiet PC. The same code fell on both sides of 2%.
- **The baseline cannot correct the noise**: it sits at ~0, so raw and above-baseline differ by about 0.05 points. The spread is in the animating app (one build: 1.72 to 2.63).
- After round 2 the table looked like step 2 and step 3 were 0.3 to 0.4 points above step 1 and main. Round 3 broke that, so it was noise.
- The trace planned for a clearly more expensive step (timers, personality reload, mood hold) was not run. A read of the source found no polling and no file watcher; the timers are one-shot or run only while text is shown.

## Consequences

D-078 (measurement method) and D-079 (an idle-CPU check fails only if the median is more than 0.5 points above `main`'s in a paired, rotated run of 5 rounds; 2% stays a reported target). Tools: `tools/perfhistory.ps1 -Subject <label> -Reference <main label>`, `tools/perfcheck.ps1 -Baseline`.

## Not done

- Animation cost vs the rest of what the app does while idle (a hidden-window run would show it; no dev hook exists).
- A longer run (more rounds).
- The D-079 verdict was smoke-tested only with 2-round, 3-second runs.
