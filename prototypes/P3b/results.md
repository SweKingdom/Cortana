# P3b: pre-rendered breathing frames vs Storyboard

Throwaway notes, 2026-11-16. Both variants with the shrunk glow and 168x168 window, 10 fps, software
rendering, `tools/perfcheck.ps1` (30 s warm-up, 60 s window), median of 3.

| Variant | Idle CPU (runs) | Private bytes | Start to orb |
| --- | --- | --- | --- |
| Storyboard scale-only (kept) | 1.77 % (1.77, 1.61, 2.27) | 75.4 MB | 479 ms |
| 40 cached bitmaps + DispatcherTimer (`PrerenderedAnimator.cs.txt`) | 1.64 % (1.54, 1.64, 2.16) | 85.6 MB | 488 ms |

The 0.13-point CPU gap is smaller than the spread between runs, so neither is cheaper on CPU. The bitmap
variant costs ~10 MB more, needs a timer (against the no-timer rule), and its bitmaps are fixed to one DPI.
Decision: keep the Storyboard. The code is kept as a text file for reference; it is not built.
