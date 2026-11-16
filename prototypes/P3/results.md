# P3: idle CPU at 30 / 15 / 10 fps (and a render-mode finding)

Throwaway notes. Measured with `tools/perfcheck.ps1 -Fps N -Render hw|software -Runs 1`
(30 s warm-up, 60 s window), Release build, 2026-11-16, RTX 4070 Laptop + Intel Arc hybrid.
One run per cell, so treat differences of a few tenths of a percent as noise.

| Render | fps | Private bytes | Idle CPU (one core) | Start to orb |
| --- | --- | --- | --- | --- |
| hardware (WPF default) | 30 | 445 MB | 4.30 % | 673 ms |
| hardware | 15 | 445 MB | 2.60 % | 592 ms |
| hardware | 10 | 438 MB | 1.28 % | 631 ms |
| software (`COMPANION_RENDER=software`) | 30 | 82 MB | 5.13 % | 445 ms |
| software | 15 | 82 MB | 3.75 % | 457 ms |
| software | 10 | 75 MB | 1.95 % | 473 ms |

Budget (D-040): private bytes <150 MB, idle CPU <0.5 %, start <1 s.

## Findings

1. **Memory:** a bare WPF window (no code of ours) is ~395 MB private on this PC with hardware
   rendering and ~65 MB with `RenderMode.SoftwareOnly`. The ~330 MB is the GPU driver path, not
   our code. GC settings (gen0 budget, concurrent, heap limit, conserve memory) changed nothing.
2. **CPU:** no animation = ~0 % in both modes, so the breathing animation is what costs CPU.
   No fps value passes <0.5 %; even 10 fps is 1.3-2 %.
3. Exploratory (20 s windows, software, 30 fps, not in the table): animating only the scale ~1.8 %,
   only opacity ~4.6 %, both ~3.6 %. `BitmapCache` on the orb made it worse (4-5 %).
4. Start to orb passes comfortably in every cell (<700 ms).

## Not decided here

Meeting the budget needs a decision from Philip (software rendering for memory; a cheaper or
slower breathing, or a changed CPU budget, for CPU). No decision rows were changed.

## Outcome (added at the end of step 1)

Philip decided D-050 (software rendering default), D-051 (idle = scale-only at 10 fps, full animation
only while thinking/talking, none when sleeping/hidden/locked/game mode) and D-052 (idle CPU <2%).
Final build (smaller window, glow shrunk to the grab ring), median of 3, 10 fps, software:
private bytes 75.4 MB, idle CPU 1.30%, start to orb 507 ms.
