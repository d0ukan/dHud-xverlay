using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DHud.Helpers;

/// <summary>
/// Win+D ("Masaüstünü göster") pencereleri küçültür/gizler; bar ve widget'lar masaüstü süsü olduğu için kaybolmamalı.
/// Kullanıcı bu pencereleri kendisi küçültemez (düğmeleri yok), bu yüzden küçültme/üst pencere kapanışı ile gizlenme
/// iletileri her zaman Show Desktop'tır: pencere sessizce (odak almadan) geri getirilir. Elle <c>Hide()</c> edilenlere dokunulmaz
/// (<see cref="UIElement.Visibility"/> Visible değilse).
/// </summary>
internal static class ShowDesktopGuard
{
    private const int WM_SIZE = 0x0005;
    private const int WM_SHOWWINDOW = 0x0018;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int SIZE_MINIMIZED = 1;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_HIDEWINDOW = 0x0080;

    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Pencerenin iletilerine kanca ekler. <c>SourceInitialized</c>'dan sonra çağrılır.</summary>
    public static void Attach(Window window, IntPtr hwnd)
    {
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (IsLost(msg, wParam, lParam) && window.Visibility == Visibility.Visible)
            {
                window.Dispatcher.BeginInvoke(() => Restore(window, h), DispatcherPriority.Background);
            }

            return IntPtr.Zero;
        });
    }

    /// <summary>Pencereyi küçültme/gizlenme iletisi mi? (WM_SIZE küçült, WM_SHOWWINDOW üst pencere nedeniyle gizle, SWP_HIDEWINDOW.)</summary>
    internal static bool IsLost(int msg, IntPtr wParam, IntPtr lParam) => msg switch
    {
        WM_SIZE => wParam.ToInt64() == SIZE_MINIMIZED,
        WM_SHOWWINDOW => wParam == IntPtr.Zero && lParam != IntPtr.Zero,
        WM_WINDOWPOSCHANGED => lParam != IntPtr.Zero && (unchecked((uint)Marshal.ReadInt32(lParam, IntPtr.Size * 2 + 16)) & SWP_HIDEWINDOW) != 0,
        _ => false
    };

    private static void Restore(Window window, IntPtr hwnd)
    {
        if (window.Visibility != Visibility.Visible || !window.IsLoaded)
        {
            return;
        }

        if (IsIconic(hwnd) || !IsWindowVisible(hwnd))
        {
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        }

        if (window.Topmost)
        {
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
