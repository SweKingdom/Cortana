# P4: click-through, DragMove, game-mode window styles

Throwaway notes. The window-style code lives in the real app
(`Character/CharacterWindow.xaml.cs`: `SetClickThrough`, `SetNoActivate`); the tray menu
has two temporary toggles for this test (**Click-through**, **No-focus (game test)**).
Step 4's GameMode will own them (D-014); then the tray toggle for No-focus goes.

Build: `dotnet build -c Release`, run `src\Companion\bin\Release\net10.0-windows\Companion.exe`.

## Checked by Claude (scripted)

See `results.md` once filled in.

## Checked by Philip (needs a real game)

Use **Borderless Windowed** (exclusive fullscreen never shows other windows; known limit).

1. Start Overwatch (or Minecraft) in Borderless Windowed. Start Companion.
   - [ ] The orb stays visible on top of the game.
2. Tray -> **Click-through** on. Click and drag over the orb's position.
   - [ ] The click reaches the game; the orb is not grabbed.
   - [ ] Ctrl+Alt+H hides the orb, again shows it. Tray -> Click-through off restores grabbing.
3. Tray -> **No-focus (game test)** on. Click the orb, drag it, then press a game key.
   - [ ] The game keeps keyboard focus the whole time (no flicker to the desktop, no Alt+Tab entry for Companion).
4. Anti-cheat sanity: the app only changes its own window styles; it does not read the game's memory or send keys.
   - [ ] Overwatch starts and plays normally with Companion running.
5. Note anything odd (orb dropping behind the game after Alt+Tab, focus steals).
