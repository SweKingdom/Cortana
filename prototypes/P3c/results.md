# P3c: motion resolution of the idle breathing (2026-11-16)

Throwaway notes. Measured by capturing the orb window as fast as possible for one 4.2 s cycle and
comparing frames (sub-pixel edge position of the core on the centre row), plus `perfcheck.ps1`
(median of 3, software rendering) for CPU. The tick counter and capture helper were temporary.

## Is COMPANION_FPS real?
Distinct on-screen frames per second: 10.0 / 15.0 / 20.0 for requested 10 / 15 / 20. The animation clock
ticked 10.3 / 53.6 / 20.4 times per second, so 15 fps does extra clock work, which probably explains why
15 fps costs far more CPU than 10.

## Why it looked stepped
At amplitude 1.08 the core radius moves 4.0 px per half cycle (glow ~6 px), ~0.2 px per frame. With layout
rounding and pixel snapping on, the edge snapped: 4 distinct core widths per half cycle (106, 108, 110, 112 px).
With both off: 15-16 distinct 0.1 px widths per half cycle at 10 fps, 22 at 15 fps.

## Idle CPU (median of 3)
| Config | Idle CPU | Private bytes |
| --- | --- | --- |
| A: 10 fps, amp 1.08, glow pulse | 2.21 % | 75.9 MB |
| B: 15 fps, amp 1.08, glow pulse | 3.10 % | 82.9 MB |
| C: 10 fps, amp 1.14, glow pulse | 1.88 % | 76.1 MB |
| D: 10 fps, amp 1.08, no pulse | 1.82 % | 75.8 MB |

Differences of up to ~0.4 points are within run-to-run noise. Philip chose C (D-054). Final build with the
constants in OrbTuning.cs: idle CPU 1.38 %, private bytes 75.7 MB, start to orb 511 ms.
