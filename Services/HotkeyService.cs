using System.Windows.Input;
using System.Windows.Interop;
using CpuGpuOverlay.Helpers;

namespace CpuGpuOverlay.Services;

/// <summary>
/// Sistem genelinde çalışan kısayol tuşları (RegisterHotKey). Mesajları görünmez,
/// yalnızca mesaj alan (message-only) bir pencereyle dinler.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("CpuGpuOverlayHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = HwndMessage
        });
        _source.AddHook(WndProc);
    }

    /// <summary>Kısayolu kaydeder. Başka bir uygulama aynı kısayolu kullanıyorsa false döner.</summary>
    public bool Register(ModifierKeys modifiers, Key key, Action handler)
    {
        var mods = NativeMethods.MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;

        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            return false;
        }

        _handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _handlers.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
