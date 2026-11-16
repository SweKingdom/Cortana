# Checklist

Owns next steps and blockers. Done criteria are measured with tools/perfcheck.ps1 (D-040).

- [x] **0. Repo setup**: done 2026-11-16. Starter files committed (79e1128); WPF project on `net10.0-windows` (D-036) in `src/Companion/` with `Cortana.sln`. H.NotifyIcon.Wpf and System.Speech confirmed working there (tray icon created; en-US voice synthesizes). Framework-dependent publish 1.42 MB. Every JSON file gets `"version": 1` (D-044) when the first one is created.
- [x] **1. Window and orb**: done 2026-11-16 on branch step-1-window-orb. Transparent draggable always-on-top 168x168 window, scale-only breathing orb, grab ring (D-049), tray icon (Show/Hide, Click-through, No-focus test, Quit), panic hotkey Ctrl+Alt+H (D-053), perfcheck.ps1, Per Monitor V2 DPI (D-047), software rendering (D-050), first JSON file settings.json with `"version": 1` (D-044).
      Includes prototypes P3 (idle CPU at 30/15/10 fps, results in prototypes/P3/), P3b (pre-rendered frames, rejected, prototypes/P3b/) and P4 (click-through, DragMove, game-mode window styles).
      Done when (D-052): app <150 MB private bytes, idle CPU <2% of one core, start to orb <1 s.
      Measured (median of 3, 10 fps, software): private bytes 75.4 MB, idle CPU 1.30%, start to orb 507 ms; tree 75.4 MB. After the smoothing follow-up (D-054, amplitude 1.14 + glow pulse): 75.7 MB, 1.38%, 511 ms.
- [ ] **P4 game test (Philip):** orb stays on top of Overwatch/Minecraft in Borderless Windowed, click-through reaches the game, No-focus keeps game focus. Steps in prototypes/P4/README.md. Drag, panic hotkey, tray menu, position restore and single instance already confirmed by Philip.
- [ ] **Later (step 3/4):** thinking/talking at 30 fps and sleeping/game mode = no animation (D-051); only idle and hidden/locked exist so far.
- [ ] **Before step 2: prototypes P1 and P2.**
      P1: 20 turns one-process-per-message vs 20 turns one-process-per-session; measure first word, claude process RAM, interrupt, whether set_model works, token growth. Decides D-037.
      P2: 50 scripted tool requests (incl. two tools in one reply, and none); count missed, malformed and leaked calls. If more than a few percent fail, try a local MCP server instead.
- [ ] **2. Chat with Claude**: text box, speech bubble, ClaudeCodeBrain per P1, streaming, ReplyCleaner, TurnId on reply events (D-038), quota counter (D-029), recovery rules (D-047), ModeSession for normal mode (D-041).
      Done when: first word ~2 s, whole tree <300 MB.
- [ ] **3. Personality, mood and memory**: personality file with examples, flirt levels and boundaries (D-033), tag parser, Activity + Expression animation (D-043), memory lines with id/date/source and Undo (D-042), summary, memory caps (D-032), PromptBuilder token budget.
- [ ] **4. Settings and game mode**: settings window, ModelRouter (D-013), Text/Voice toggle, hotkeys, game mode (D-014), ActivityWatcher with frequency slider, Do-not-disturb and suppression rules (D-046).
- [ ] **5. Tools**: reminders/timers, open app/site, the five tool rules (D-045), yes/no confirmations showing exact args.
- [ ] **6. Voice (alpha)**: Windows voice, orb pulses on visemes (prototype P5), first-sentence-early, interrupt with SpeakAsyncCancelAll, then push-to-talk; add time-to-first-audio to perfcheck. Cloud voice later, provider undecided (D-035).
- [ ] **7. Later**: explicit twin with encrypted memory, content-free logs (D-031) and the trust boundary (D-039); cloud voice; screen glance; idle reactions; ledges; game commentary; character v2.

## Blockers

- None for steps 0–1. Step 2 waits on P1 and P2.
