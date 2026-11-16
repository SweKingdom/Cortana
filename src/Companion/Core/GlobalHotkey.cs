using System.Windows.Interop;
using Companion.Native;

namespace Companion.Core;

/// <summary>Registers global hotkeys on a hidden message-only window owned by the app.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public GlobalHotkey()
    {
        var p = new HwndSourceParameters("CompanionHotkeys") { ParentWindow = NativeMethods.HWND_MESSAGE, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Returns false if the combination is already taken.</summary>
    public bool Register(uint modifiers, uint vk, Action handler)
    {
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, vk))
            return false;
        _handlers[id] = handler;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue((int)wParam, out var h))
        {
            handled = true;
            h();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _source.Dispose();
    }
}
