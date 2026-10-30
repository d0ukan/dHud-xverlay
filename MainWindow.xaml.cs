using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DHud.Helpers;
using DHud.Services;

namespace DHud;

public partial class MainWindow : Window
{
    private const double ScreenEdgeMargin = 16;
    private const double SnapDistance = 24;
    private const int SparklineSamples = 60;
    private const float SparklineMinSpan = 10;
    private const double MinLabelFontSize = 9.5;
    private const double MinSmallFontSize = 10.5;
    private const double MinValueFontSize = 12.5;

    private readonly SettingsService _settings = App.Current.Settings;
    private readonly Queue<float> _cpuHistory = new();
    private readonly Queue<float> _gpuHistory = new();
    private SensorSnapshot? _lastSnapshot;
    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();

        RestorePosition();
        ApplySettings();

        _settings.Changed += (_, _) => ApplySettings();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => EnsureOnScreen();
        SizeChanged += (_, _) => KeepInsideWorkArea();
    }

    // ---- Değerler ----

    /// <summary>Yeni sensör değerlerini ekrana yansıtır. UI thread'inden çağrılmalıdır.</summary>
    public void UpdateReadings(SensorSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        AddHistory(_cpuHistory, snapshot.Cpu?.Temperature);
        AddHistory(_gpuHistory, snapshot.Gpu?.Temperature);

        // Gizliyken çizim yapma (CPU tasarrufu); tekrar gösterilince bir sonraki ölçümde güncellenir.
        if (IsVisible)
        {
            Render(snapshot);
        }
    }

    private void Render(SensorSnapshot snapshot)
    {
        var compact = _settings.Current.Compact;
        var cpu = snapshot.Cpu;
        var gpu = snapshot.Gpu;
        var ram = snapshot.Memory;

        CpuTempText.Text = Format.Temperature(cpu?.Temperature, compact);
        CpuTempText.Foreground = BrushFor(cpu?.Temperature, gpu: false);
        CpuLoadText.Text = Format.Load(cpu?.Load);
        CpuClockText.Text = Format.Clock(cpu?.ClockMhz, compact);
        CpuPowerText.Text = Format.Power(cpu?.PowerWatts, compact);
        DrawSparkline(CpuSpark, _cpuHistory, CpuTempText.Foreground);

        GpuTempText.Text = Format.Temperature(gpu?.Temperature, compact);
        GpuTempText.Foreground = BrushFor(gpu?.Temperature, gpu: true);
        GpuLoadText.Text = Format.Load(gpu?.Load);
        GpuClockText.Text = Format.Clock(gpu?.ClockMhz, compact);
        GpuPowerText.Text = Format.Power(gpu?.PowerWatts, compact);
        GpuMemText.Text = Format.Memory(gpu?.MemoryUsedGb, gpu?.MemoryTotalGb, compact);
        DrawSparkline(GpuSpark, _gpuHistory, GpuTempText.Foreground);

        RamLoadText.Text = Format.Load(ram?.Load);
        RamMemText.Text = Format.Memory(ram?.MemoryUsedGb, ram?.MemoryTotalGb, compact);

        var net = snapshot.Network;
        var bits = _settings.Current.NetworkInBits;
        NetDownText.Text = "↓ " + Format.Speed(net?.DownloadBytesPerSecond, bits, compact);
        NetUpText.Text = "↑ " + Format.Speed(net?.UploadBytesPerSecond, bits, compact);
        NetPingText.Text = Format.Ping(net?.PingMs, compact);
        NetPingText.Foreground = net?.PingMs switch
        {
            null => (Brush)FindResource("HotBrush"),
            > 150 => (Brush)FindResource("WarnBrush"),
            _ => (Brush)FindResource("SubtleTextBrush")
        };

        CpuGroup.ToolTip = cpu is null
            ? Loc.T("Tip.CpuNotFound")
            : cpu.Temperature is null ? $"{cpu.Name}\n{CpuUnavailableReason()}" : cpu.Name;
        GpuGroup.ToolTip = gpu?.Name ?? Loc.T("Tip.GpuNotFound");
        GpuMemText.ToolTip = Loc.T("Tip.Vram");
        NetDownText.ToolTip = Loc.T("Tip.Download");
        NetUpText.ToolTip = Loc.T("Tip.Upload");
        RamGroup.ToolTip = Loc.T("Tip.Ram");
        NetGroup.ToolTip = net?.AdapterName is { } adapter
            ? $"{adapter}\nPing: {_settings.Current.PingHost}"
            : Loc.T("Tip.NoNetwork");
    }

    private static string CpuUnavailableReason()
    {
        if (!App.Current.IsElevated)
        {
            return Loc.T("Tip.CpuNeedsAdmin");
        }

        return App.Current.IsPawnIoInstalled
            ? Loc.T("Tip.CpuNoSensor")
            : Loc.T("Tip.CpuNeedsPawnIo");
    }

    private Brush BrushFor(float? temperature, bool gpu)
    {
        var s = _settings.Current;
        if (!s.ColorByTemperature || temperature is null)
        {
            return (Brush)FindResource("TextBrush");
        }

        var (warn, hot) = s.Thresholds(gpu);
        var key = temperature >= hot ? "HotBrush"
            : temperature >= warn ? "WarnBrush"
            : "GoodBrush";
        return (Brush)FindResource(key);
    }

    private static void AddHistory(Queue<float> history, float? value)
    {
        if (value is not float v)
        {
            return;
        }

        history.Enqueue(v);
        while (history.Count > SparklineSamples)
        {
            history.Dequeue();
        }
    }

    private void DrawSparkline(Polyline line, Queue<float> history, Brush stroke)
    {
        line.Stroke = stroke;
        if (line.Visibility != Visibility.Visible || history.Count < 2)
        {
            line.Points = [];
            return;
        }

        var width = (double)FindResource("SparkWidth");
        var height = (double)FindResource("SparkHeight") - 2; // kalın çizgi kenardan taşmasın

        // Ölçek son değerlere göre ayarlanır; küçük dalgalanmalar abartılmasın diye en az 10 derecelik aralık.
        var min = history.Min();
        var max = history.Max();
        if (max - min < SparklineMinSpan)
        {
            var mid = (max + min) / 2;
            min = mid - SparklineMinSpan / 2;
            max = mid + SparklineMinSpan / 2;
        }

        var step = width / (SparklineSamples - 1);
        var offset = (SparklineSamples - history.Count) * step; // yeni değerler hep sağda
        var points = new PointCollection(history.Count);
        var i = 0;
        foreach (var value in history)
        {
            var y = 1 + height - (value - min) / (max - min) * height;
            points.Add(new Point(offset + i++ * step, y));
        }

        points.Freeze();
        line.Points = points;
    }

    // ---- Ayarlar ----

    private void ApplySettings()
    {
        var s = _settings.Current;
        var horizontal = s.Layout == OverlayLayout.Horizontal;

        // Yalnızca zemin saydamlaşır; yazılar her zaman net kalır.
        var background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(s.BackgroundOpacity * 255), 0x14, 0x14, 0x18));
        background.Freeze();
        GlassBackground.Background = background;

        Topmost = s.AlwaysOnTop;

        ApplyMetrics(s.Compact, s.Scale);
        var radius = new CornerRadius((s.Compact ? 8 : 12) * s.Scale);
        GlassBackground.CornerRadius = radius;
        GlassHighlight.CornerRadius = radius;
        GroupsPanel.Margin = Scaled(s.Compact ? new Thickness(9, 4, 9, 4) : new Thickness(14, 9, 14, 9), s.Scale);

        // Dikey düzende sütunlar gruplar arasında hizalanır; yatayda her grup kendi genişliğinde kalır.
        GroupsPanel.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        Grid.SetIsSharedSizeScope(GroupsPanel, !horizontal);

        SetVisible(s.ShowUsage, CpuLoadText, GpuLoadText);
        SetVisible(s.ShowSparkline, CpuSpark, GpuSpark);
        SetVisible(s.ShowClock, CpuClockText, GpuClockText);
        SetVisible(s.ShowPower, CpuPowerText, GpuPowerText);
        SetVisible(s.ShowVram, GpuMemText);
        SetVisible(s.ShowRam, RamGroup);
        SetVisible(s.ShowNetwork, NetGroup);
        SetVisible(s.ShowPing, NetPingText);
        SetVisible(horizontal, CpuGpuSeparator);
        SetVisible(horizontal && s.ShowRam, GpuRamSeparator);
        SetVisible(horizontal && s.ShowNetwork, RamNetSeparator);

        SettingsMenuItem.Header = Loc.T("M.Settings");
        LayoutMenuItem.Header = Loc.T(horizontal ? "M.ToVertical" : "M.ToHorizontal");
        CompactMenuItem.Header = Loc.T("M.Compact");
        LockMenuItem.Header = Loc.T("M.Lock");
        ClickThroughMenuItem.Header = Loc.T("M.ClickThrough");
        ResetPositionMenuItem.Header = Loc.T("M.ResetPosition");
        HideMenuItem.Header = Loc.T("M.Hide");
        ExitMenuItem.Header = Loc.T("M.Exit");
        CompactMenuItem.IsChecked = s.Compact;
        LockMenuItem.IsChecked = s.LockPosition;
        Cursor = s.LockPosition ? null : Cursors.SizeAll;

        ApplyClickThrough(s.ClickThrough);

        if (_lastSnapshot is not null)
        {
            Render(_lastSnapshot);
        }
    }

    /// <summary>
    /// Yazı boyutlarını ve boşlukları ölçek çarpanıyla doğrudan ayarlar. Bir ScaleTransform
    /// yazıyı küçük boyutta çizip büyütür ve bulanık gösterir; burada metin hedef boyutta çizilir.
    /// </summary>
    private void ApplyMetrics(bool compact, double scale)
    {
        // Segoe UI bu boyutların altında okunaksızlaşır; en küçük ayarda bile bu sınırların altına inilmez.
        var smallFont = Math.Max((compact ? 11.0 : 13.0) * scale, MinSmallFontSize);
        Resources["LabelFontSize"] = Math.Max((compact ? 10.0 : 12.0) * scale, MinLabelFontSize);
        Resources["ValueFontSize"] = Math.Max((compact ? 13.0 : 18.0) * scale, MinValueFontSize);
        Resources["SmallFontSize"] = smallFont;

        // Bulanık gölge küçük harflerin içini doldurur; küçük yazıda keskin 1 piksellik gölge kullan.
        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Direction = 315,
            ShadowDepth = 1,
            BlurRadius = smallFont < 12 ? 0 : 4,
            Opacity = smallFont < 12 ? 0.75 : 0.9
        };
        shadow.Freeze();
        Resources["OverlayTextShadow"] = shadow;

        Resources["TempMinWidth"] = (compact ? 30.0 : 58.0) * scale;
        Resources["NetMinWidth"] = (compact ? 52.0 : 78.0) * scale;
        Resources["SparkWidth"] = Math.Round((compact ? 28.0 : 40.0) * scale);
        Resources["SparkHeight"] = Math.Round((compact ? 10.0 : 14.0) * scale);
        Resources["SparkStroke"] = 1.3 * Math.Max(1, scale * 0.85);
        Resources["LabelMargin"] = Scaled(compact ? new Thickness(0, 0, 5, 0) : new Thickness(0, 0, 12, 0), scale);
        Resources["CellMargin"] = Scaled(compact ? new Thickness(6, 0, 0, 0) : new Thickness(10, 0, 0, 0), scale);
        Resources["SeparatorMargin"] = Scaled(compact ? new Thickness(7, 2, 7, 2) : new Thickness(12, 3, 12, 3), scale);
    }

    private static Thickness Scaled(Thickness t, double scale) =>
        new(t.Left * scale, t.Top * scale, t.Right * scale, t.Bottom * scale);

    private static void SetVisible(bool visible, params UIElement[] elements)
    {
        foreach (var element in elements)
        {
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Tıklama geçirme: pencere fare olaylarını almaz, tıklamalar alttaki pencereye gider.</summary>
    private void ApplyClickThrough(bool enabled)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return; // SourceInitialized'da tekrar uygulanır.
        }

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle = enabled
            ? exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
            : exStyle & ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    // ---- Konum ----

    private void RestorePosition()
    {
        var s = _settings.Current;
        if (s.Left is double left && s.Top is double top && IsPointOnVirtualScreen(left + 20, top + 20))
        {
            Left = left;
            Top = top;
        }
        else
        {
            // Konum henüz bilinmiyor: Loaded'da boyut belli olunca sağ üst köşeye yerleşecek.
            Left = double.NaN;
            Top = double.NaN;
        }
    }

    private void EnsureOnScreen()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top) || !IsPointOnVirtualScreen(Left + 20, Top + 20))
        {
            MoveToDefaultPosition();
        }
    }

    private void MoveToDefaultPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenEdgeMargin;
        Top = workArea.Top + ScreenEdgeMargin;
    }

    /// <summary>Kayıtlı konum, artık bağlı olmayan bir monitörde kalmışsa false döner.</summary>
    private static bool IsPointOnVirtualScreen(double x, double y) =>
        x >= SystemParameters.VirtualScreenLeft
        && y >= SystemParameters.VirtualScreenTop
        && x <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
        && y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    /// <summary>Pencerenin bulunduğu monitörün çalışma alanı (görev çubuğu hariç), DIP cinsinden.</summary>
    private Rect? GetCurrentWorkArea()
    {
        if (_hwnd == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return null;
        }

        var monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var fromDevice = target.TransformFromDevice;
        return new Rect(
            fromDevice.Transform(new Point(info.rcWork.Left, info.rcWork.Top)),
            fromDevice.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom)));
    }

    /// <summary>Kenara yakın bırakılan overlay'i ekran kenarına/köşesine yapıştırır.</summary>
    private void SnapToEdges()
    {
        if (GetCurrentWorkArea() is not Rect work)
        {
            return;
        }

        if (Math.Abs(Left - work.Left) < SnapDistance) Left = work.Left;
        else if (Math.Abs(work.Right - (Left + ActualWidth)) < SnapDistance) Left = work.Right - ActualWidth;

        if (Math.Abs(Top - work.Top) < SnapDistance) Top = work.Top;
        else if (Math.Abs(work.Bottom - (Top + ActualHeight)) < SnapDistance) Top = work.Bottom - ActualHeight;
    }

    /// <summary>Boyut büyüdüğünde (ör. düzen değişince) overlay ekranın dışına taşmasın.</summary>
    private void KeepInsideWorkArea()
    {
        if (!IsLoaded || GetCurrentWorkArea() is not Rect work)
        {
            return;
        }

        if (Left + ActualWidth > work.Right) Left = Math.Max(work.Left, work.Right - ActualWidth);
        if (Top + ActualHeight > work.Bottom) Top = Math.Max(work.Top, work.Bottom - ActualHeight);
        if (Left < work.Left) Left = work.Left;
        if (Top < work.Top) Top = work.Top;
    }

    public void SavePosition()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        _settings.Current.Left = Left;
        _settings.Current.Top = Top;
        _settings.Save();
    }

    // ---- Pencere davranışı ----

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Araç penceresi stili: Alt+Tab listesinde görünmez.
        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle = (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        ApplyClickThrough(_settings.Current.ClickThrough);
    }

    /// <summary>
    /// Sürüklenirken (WM_MOVING) önerilen konumu, pencerenin en yakın monitörünün çalışma alanı içinde tutar;
    /// overlay ekranın dışına çıkarılamaz. Monitörler arası geçiş (kenarı aşan taraf diğer monitöre değince) serbesttir.
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_MOVING)
        {
            return IntPtr.Zero;
        }

        var rect = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
        var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var work = info.rcWork;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        rect.Left = Math.Max(work.Left, Math.Min(rect.Left, work.Right - width));
        rect.Top = Math.Max(work.Top, Math.Min(rect.Top, work.Bottom - height));
        rect.Right = rect.Left + width;
        rect.Bottom = rect.Top + height;
        Marshal.StructureToPtr(rect, lParam, false);

        handled = true;
        return (IntPtr)1;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || _settings.Current.LockPosition)
        {
            return;
        }

        // DragMove, fare bırakılana kadar bloklar; ardından kenara yapıştır ve konumu kaydet.
        DragMove();
        if (_settings.Current.SnapToEdges)
        {
            SnapToEdges();
        }

        SavePosition();
    }

    private void GearButton_Click(object sender, RoutedEventArgs e) => App.Current.OpenSettings();

    private void SettingsMenu_Click(object sender, RoutedEventArgs e) => App.Current.OpenSettings();

    private void LayoutMenu_Click(object sender, RoutedEventArgs e)
    {
        var s = _settings.Current;
        s.Layout = s.Layout == OverlayLayout.Horizontal ? OverlayLayout.Vertical : OverlayLayout.Horizontal;
        _settings.NotifyChanged();
    }

    private void CompactMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.Current.Compact = CompactMenuItem.IsChecked;
        _settings.NotifyChanged();
    }

    private void LockMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.Current.LockPosition = LockMenuItem.IsChecked;
        _settings.NotifyChanged();
    }

    private void ClickThroughMenu_Click(object sender, RoutedEventArgs e) => App.Current.ToggleClickThrough();

    private void HideMenu_Click(object sender, RoutedEventArgs e) => App.Current.ToggleOverlay();

    private void ResetPositionMenu_Click(object sender, RoutedEventArgs e)
    {
        MoveToDefaultPosition();
        SavePosition();
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e) => App.Current.ExitApplication();
}
