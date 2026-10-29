using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CpuGpuOverlay.Services;

namespace CpuGpuOverlay;

public partial class SettingsWindow : Window
{
    private const int MinThresholdGap = 1;

    private readonly SettingsService _settings = App.Current.Settings;
    private readonly DispatcherTimer _statusTimer;

    // InitializeComponent ve programatik değişiklikler sırasında olay işleyicileri çalışmasın.
    private bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();

        LoadValues();
        RefreshStartupState();
        RefreshStatus();

        // Ayar başka yerden değişirse (tepsi menüsü, kısayol, overlay sağ tık) pencere de güncel kalsın.
        _settings.Changed += OnSettingsChanged;

        // Donanım adları ilk okumadan sonra belli olur; pencere açıkken durumu tazele.
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            _settings.Changed -= OnSettingsChanged;
        };

        _loading = false;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var wasLoading = _loading;
        _loading = true;
        LoadValues();
        _loading = wasLoading;
    }

    private void LoadValues()
    {
        var s = _settings.Current;

        VerticalLayoutRadio.IsChecked = s.Layout == OverlayLayout.Vertical;
        HorizontalLayoutRadio.IsChecked = s.Layout == OverlayLayout.Horizontal;
        CompactCheckBox.IsChecked = s.Compact;
        AlwaysOnTopCheckBox.IsChecked = s.AlwaysOnTop;
        OpacitySlider.Value = s.BackgroundOpacity;
        ScaleSlider.Value = s.Scale;

        ShowUsageCheckBox.IsChecked = s.ShowUsage;
        ShowSparklineCheckBox.IsChecked = s.ShowSparkline;
        ShowClockCheckBox.IsChecked = s.ShowClock;
        ShowPowerCheckBox.IsChecked = s.ShowPower;
        ShowVramCheckBox.IsChecked = s.ShowVram;
        ShowRamCheckBox.IsChecked = s.ShowRam;
        ShowNetworkCheckBox.IsChecked = s.ShowNetwork;
        NetworkInBitsCheckBox.IsChecked = s.NetworkInBits;
        ShowPingCheckBox.IsChecked = s.ShowPing;
        if (!PingHostTextBox.IsKeyboardFocusWithin)
        {
            PingHostTextBox.Text = s.PingHost;
        }

        NetworkOptionsPanel.IsEnabled = s.ShowNetwork;
        PingHostTextBox.IsEnabled = s.ShowPing;
        IntervalSlider.Value = s.UpdateIntervalMs / 1000.0;

        ClickThroughCheckBox.IsChecked = s.ClickThrough;
        LockPositionCheckBox.IsChecked = s.LockPosition;
        SnapCheckBox.IsChecked = s.SnapToEdges;
        HotkeysCheckBox.IsChecked = s.HotkeysEnabled;

        ColorCheckBox.IsChecked = s.ColorByTemperature;
        WarnSlider.Value = s.WarnTemperature;
        HotSlider.Value = s.HotTemperature;
        AlertCheckBox.IsChecked = s.AlertEnabled;
        AlertSecondsSlider.Value = s.AlertSeconds;

        WarnSlider.IsEnabled = HotSlider.IsEnabled = s.ColorByTemperature;
        AlertSecondsSlider.IsEnabled = s.AlertEnabled;
    }

    /// <summary>Ekrandaki tüm kontrollerin değerlerini ayarlara yazar ve değişikliği yayınlar.</summary>
    private void SaveValues()
    {
        if (_loading)
        {
            return;
        }

        var s = _settings.Current;

        s.Layout = HorizontalLayoutRadio.IsChecked == true ? OverlayLayout.Horizontal : OverlayLayout.Vertical;
        s.Compact = CompactCheckBox.IsChecked == true;
        s.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        s.BackgroundOpacity = OpacitySlider.Value;
        s.Scale = ScaleSlider.Value;

        s.ShowUsage = ShowUsageCheckBox.IsChecked == true;
        s.ShowSparkline = ShowSparklineCheckBox.IsChecked == true;
        s.ShowClock = ShowClockCheckBox.IsChecked == true;
        s.ShowPower = ShowPowerCheckBox.IsChecked == true;
        s.ShowVram = ShowVramCheckBox.IsChecked == true;
        s.ShowRam = ShowRamCheckBox.IsChecked == true;
        s.ShowNetwork = ShowNetworkCheckBox.IsChecked == true;
        s.NetworkInBits = NetworkInBitsCheckBox.IsChecked == true;
        s.ShowPing = ShowPingCheckBox.IsChecked == true;
        s.PingHost = PingHostTextBox.Text;
        s.UpdateIntervalMs = (int)Math.Round(IntervalSlider.Value * 1000);

        s.ClickThrough = ClickThroughCheckBox.IsChecked == true;
        s.LockPosition = LockPositionCheckBox.IsChecked == true;
        s.SnapToEdges = SnapCheckBox.IsChecked == true;
        s.HotkeysEnabled = HotkeysCheckBox.IsChecked == true;

        s.ColorByTemperature = ColorCheckBox.IsChecked == true;
        s.WarnTemperature = (int)WarnSlider.Value;
        s.HotTemperature = (int)HotSlider.Value;
        s.AlertEnabled = AlertCheckBox.IsChecked == true;
        s.AlertSeconds = (int)AlertSecondsSlider.Value;

        _settings.NotifyChanged();
    }

    private void Option_Click(object sender, RoutedEventArgs e) => SaveValues();

    private void PingHostTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveValues();
        PingHostTextBox.Text = _settings.Current.PingHost; // boş bırakıldıysa varsayılanı göster
    }

    private void PingHostTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveValues();
            e.Handled = true;
        }
    }

    private void AppearanceRadio_Checked(object sender, RoutedEventArgs e) => SaveValues();

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveValues();

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        var warn = (int)WarnSlider.Value;
        var hot = (int)HotSlider.Value;

        // Kritik eşik her zaman uyarı eşiğinin üzerinde kalsın; hangi kaydırıcı oynatıldıysa diğeri itilir.
        if (hot < warn + MinThresholdGap)
        {
            _loading = true;
            if (ReferenceEquals(sender, WarnSlider))
            {
                HotSlider.Value = Math.Min(warn + MinThresholdGap, (int)HotSlider.Maximum);
                WarnSlider.Value = HotSlider.Value - MinThresholdGap;
            }
            else
            {
                WarnSlider.Value = Math.Max(hot - MinThresholdGap, (int)WarnSlider.Minimum);
                HotSlider.Value = WarnSlider.Value + MinThresholdGap;
            }

            _loading = false;
        }

        SaveValues();
    }

    // ---- Başlangıç ----

    private void RefreshStartupState()
    {
        var wasLoading = _loading;
        _loading = true;

        var active = StartupManager.GetActiveMethod();
        StartupCheckBox.IsChecked = active is not null;

        var method = active ?? _settings.Current.PreferredStartupMethod;
        TaskSchedulerRadio.IsChecked = method == StartupMethod.TaskScheduler;
        RegistryRadio.IsChecked = method == StartupMethod.Registry;

        StartupStatusText.Text = active switch
        {
            StartupMethod.TaskScheduler => "✓ Etkin: Görev Zamanlayıcı (\"CpuGpuOverlay\" görevi)",
            StartupMethod.Registry => "✓ Etkin: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            _ => "Başlangıçta çalıştırma kapalı."
        };

        _loading = wasLoading;
    }

    private void StartupCheckBox_Click(object sender, RoutedEventArgs e) => ApplyStartup();

    private void StartupMethod_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.Current.PreferredStartupMethod = SelectedMethod;
        _settings.Save();

        // Başlangıç zaten açıksa yeni yönteme geçir.
        if (StartupCheckBox.IsChecked == true)
        {
            ApplyStartup();
        }
    }

    private StartupMethod SelectedMethod =>
        RegistryRadio.IsChecked == true ? StartupMethod.Registry : StartupMethod.TaskScheduler;

    private void ApplyStartup()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            if (StartupCheckBox.IsChecked == true)
            {
                StartupManager.Enable(SelectedMethod);
            }
            else
            {
                StartupManager.Disable();
            }
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, ex.Message, "Başlangıç ayarı", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        // Ekrandaki durum her zaman sistemdeki gerçek durumu yansıtsın.
        RefreshStartupState();
    }

    // ---- Durum ----

    private void RefreshStatus()
    {
        var app = App.Current;

        ElevationStatusText.Text = app.IsElevated
            ? "✓ Yönetici olarak çalışıyor"
            : "⚠ Yönetici yetkisi yok — CPU sıcaklığı okunamayabilir";
        ElevationStatusText.Foreground = (Brush)FindResource(app.IsElevated ? "GoodBrush" : "WarnBrush");

        DriverStatusText.Text = app.IsPawnIoInstalled
            ? "✓ PawnIO sürücüsü kurulu"
            : "⚠ PawnIO sürücüsü yok — https://pawnio.eu adresinden kurun";
        DriverStatusText.Foreground = (Brush)FindResource(app.IsPawnIoInstalled ? "GoodBrush" : "WarnBrush");

        var latest = app.Monitor.Latest;
        CpuNameText.Text = "CPU: " + (latest is null ? "taranıyor..." : latest.Cpu?.Name ?? "bulunamadı");
        GpuNameText.Text = "GPU: " + (latest is null ? "taranıyor..." : latest.Gpu?.Name ?? "bulunamadı");

        HotkeyStatusText.Text = app.HotkeyError ?? string.Empty;
        HotkeyStatusText.Visibility = app.HotkeyError is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Pencere ----

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ExitAppButton_Click(object sender, RoutedEventArgs e) => App.Current.ExitApplication();
}
