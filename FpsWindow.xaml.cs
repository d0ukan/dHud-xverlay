using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DHud.Helpers;

namespace DHud;

/// <summary>Oyun açıkken görünen küçük FPS sayacı. Oyun yokken (değer null) gizlenir.</summary>
public partial class FpsWindow : Window
{
    private const int EdgeMargin = 16;

    private static readonly Brush Good = Frozen(0x4A, 0xDE, 0x80);
    private static readonly Brush Warn = Frozen(0xFA, 0xCC, 0x15);
    private static readonly Brush Hot = Frozen(0xF8, 0x71, 0x71);

    private IntPtr _hwnd;

    public FpsWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Tıklamaları geçir, odak alma, Alt+Tab listesinde görünme.
            var style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
                      | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        };
    }

    /// <summary>Değeri günceller; null ise gizler. Gösterirken oyunun bulunduğu monitörün sol üst köşesine yerleşir.</summary>
    public void Update(float? fps)
    {
        if (fps is not float value)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        ValueText.Text = $"{value:0}";
        ValueText.Foreground = value >= 60 ? Good : value >= 30 ? Warn : Hot;

        if (!IsVisible)
        {
            Show();
        }

        PlaceOnForegroundMonitor();
    }

    private void PlaceOnForegroundMonitor()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // Fiziksel piksel koordinatlarıyla yerleştirilir (DPI dönüşümü gerekmez); her güncellemede en üstte tutulur.
        var monitor = NativeMethods.MonitorFromWindow(NativeMethods.GetForegroundWindow(), NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            info.rcMonitor.Left + EdgeMargin, info.rcMonitor.Top + EdgeMargin, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
