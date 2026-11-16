# P4 results (scripted part, 2026-11-16)

Checked on the running Release build with Win32 calls from PowerShell:

- Extended style of the orb window: `0x80088` = TOOLWINDOW + TOPMOST + LAYERED. Not TRANSPARENT, not NOACTIVATE by default (correct).
- DPI awareness context of the window: Per Monitor V2 (D-047).

Not verified by me (needs clicks on the tray menu or a real game):

- Tray Quit (I only killed the process), the single-instance mutex, tray icon appearance.
- Tray toggles for Click-through / No-focus changing the style bits, and clicks passing through.
- Ctrl+Alt+H hide/show, DragMove and position restore after restart.
- Behaviour over Overwatch/Minecraft in Borderless Windowed. See README.md for the steps.
