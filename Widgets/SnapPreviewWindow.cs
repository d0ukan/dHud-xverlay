using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DHud.Helpers;

namespace DHud.Widgets;

/// <summary>Sürüklerken widget'ın oturacağı ızgara hücrelerini gösteren yarı saydam, tıklamayı geçiren çerçeve.</summary>
public sealed class SnapPreviewWindow : Window
{
    public SnapPreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;

        var accent = (Color)(Application.Current.TryFindResource("AccentColor") ?? Color.FromRgb(0x3B, 0x82, 0xF6));
        Content = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2)
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        };
    }

    public void ShowAt(Rect rect)
    {
        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
        if (!IsVisible)
        {
            Show();
        }
    }
}
