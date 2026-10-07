using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace GameTracker;

/// <summary>Global hotkeys and click-through, which WPF has no API for.</summary>
public sealed class Native : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20;
    const int WS_EX_LAYERED = 0x80000;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    readonly HwndSource _source;
    readonly Dictionary<int, Action> _actions = new();

    public Native(HwndSource source)
    {
        _source = source;
        _source.AddHook(Hook);
    }

    /// <summary>Registers Ctrl+Shift+key (plus Alt when asked). Returns false if another app owns it.</summary>
    public bool Hotkey(Key key, Action action, bool alt = false)
    {
        int id = _actions.Count + 1;
        uint mods = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT | (alt ? MOD_ALT : 0);
        if (!RegisterHotKey(_source.Handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
        _actions[id] = action;
        return true;
    }

    public void SetClickThrough(bool enabled)
    {
        int style = GetWindowLong(_source.Handle, GWL_EXSTYLE) | WS_EX_LAYERED;
        style = enabled ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(_source.Handle, GWL_EXSTYLE, style);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _source.RemoveHook(Hook);
    }
}
