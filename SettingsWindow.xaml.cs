using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using System.IO;
using System.Windows.Media.Imaging;
using DHud.Services;
using QRCoder;

namespace DHud;

public partial class SettingsWindow : Window
{
    private const int MinThresholdGap = 1;

    private readonly SettingsService _settings = App.Current.Settings;
    private readonly DispatcherTimer _statusTimer;
    private string? _shownQrUri;

    // InitializeComponent ve programatik değişiklikler sırasında olay işleyicileri çalışmasın.
    private bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();
        Language = XmlLanguage.GetLanguage(Loc.CultureName);

        // Küçük ekranda pencere ekrandan taşmasın: sekme içeriği kalan yüksekliğe sığar, gerekirse kaydırılır.
        // (başlık, sekmeler, durum kutusu ve düğmeler için yaklaşık 330 px ayrılır)
        TabScroll.MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 330);

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
        EnglishRadio.IsChecked = s.Language == AppLanguage.English;
        TurkishRadio.IsChecked = s.Language == AppLanguage.Turkish;
        CompactCheckBox.IsChecked = s.Compact;
        AlwaysOnTopCheckBox.IsChecked = s.AlwaysOnTop;
        OpacitySlider.Value = s.BackgroundOpacity;
        ScaleSlider.Value = s.Scale;

        ShowCpuCheckBox.IsChecked = s.ShowCpu;
        ShowGpuCheckBox.IsChecked = s.ShowGpu;
        ShowUsageCheckBox.IsChecked = s.ShowUsage;
        ShowSparklineCheckBox.IsChecked = s.ShowSparkline;
        ShowClockCheckBox.IsChecked = s.ShowClock;
        ShowPowerCheckBox.IsChecked = s.ShowPower;
        ShowFanCheckBox.IsChecked = s.ShowFan;
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
        AutoUpdateCheckBox.IsChecked = s.AutoUpdateCheck;
        foreach (var radio in UpdateHoursPanel.Children.OfType<RadioButton>())
        {
            radio.IsChecked = (string)radio.Tag == s.UpdateCheckHours.ToString();
        }

        UpdateHoursPanel.IsEnabled = s.AutoUpdateCheck;
        FpsCheckBox.IsChecked = s.FpsEnabled;
        FpsOverlayCheckBox.IsChecked = s.FpsOverlay;
        FpsOverlayCheckBox.IsEnabled = s.FpsEnabled;
        MobileCheckBox.IsChecked = s.MobileEnabled;
        MobileFanCheckBox.IsChecked = s.MobileFanControl;
        MobilePanel.Visibility = s.MobileEnabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshMobile();
        VersionText.Text = UpdateService.CurrentVersionText;

        ColorCheckBox.IsChecked = s.ColorByTemperature;
        CpuWarnSlider.Value = s.CpuWarnTemperature;
        CpuHotSlider.Value = s.CpuHotTemperature;
        GpuWarnSlider.Value = s.GpuWarnTemperature;
        GpuHotSlider.Value = s.GpuHotTemperature;
        AlertCheckBox.IsChecked = s.AlertEnabled;
        AlertSecondsSlider.Value = s.AlertSeconds;

        CpuWarnSlider.IsEnabled = CpuHotSlider.IsEnabled = GpuWarnSlider.IsEnabled = GpuHotSlider.IsEnabled = s.ColorByTemperature;
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
        s.Language = TurkishRadio.IsChecked == true ? AppLanguage.Turkish : AppLanguage.English;
        s.Compact = CompactCheckBox.IsChecked == true;
        s.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        s.BackgroundOpacity = OpacitySlider.Value;
        s.Scale = ScaleSlider.Value;

        s.ShowCpu = ShowCpuCheckBox.IsChecked == true;
        s.ShowGpu = ShowGpuCheckBox.IsChecked == true;
        s.ShowUsage = ShowUsageCheckBox.IsChecked == true;
        s.ShowSparkline = ShowSparklineCheckBox.IsChecked == true;
        s.ShowClock = ShowClockCheckBox.IsChecked == true;
        s.ShowPower = ShowPowerCheckBox.IsChecked == true;
        s.ShowFan = ShowFanCheckBox.IsChecked == true;
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
        s.AutoUpdateCheck = AutoUpdateCheckBox.IsChecked == true;
        s.FpsEnabled = FpsCheckBox.IsChecked == true;
        s.FpsOverlay = FpsOverlayCheckBox.IsChecked == true;
        s.MobileEnabled = MobileCheckBox.IsChecked == true;
        s.MobileFanControl = MobileFanCheckBox.IsChecked == true;
        if (UpdateHoursPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) is { } hours)
        {
            s.UpdateCheckHours = int.Parse((string)hours.Tag);
        }

        s.ColorByTemperature = ColorCheckBox.IsChecked == true;
        s.CpuWarnTemperature = (int)CpuWarnSlider.Value;
        s.CpuHotTemperature = (int)CpuHotSlider.Value;
        s.GpuWarnTemperature = (int)GpuWarnSlider.Value;
        s.GpuHotTemperature = (int)GpuHotSlider.Value;
        s.AlertEnabled = AlertCheckBox.IsChecked == true;
        s.AlertSeconds = (int)AlertSecondsSlider.Value;

        _settings.NotifyChanged();
    }

    private void Option_Click(object sender, RoutedEventArgs e) => SaveValues();

    private void OpenFanButton_Click(object sender, RoutedEventArgs e) => App.Current.OpenFanControl();

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await App.Current.CheckForUpdatesAsync(manual: true);

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

    private void LanguageRadio_Checked(object sender, RoutedEventArgs e) => SaveValues();

    private void UpdateHoursRadio_Checked(object sender, RoutedEventArgs e) => SaveValues();

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveValues();

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        // Hangi çiftin (CPU / GPU) kaydırıcısı oynatıldıysa yalnızca o çift düzeltilir.
        var (warnSlider, hotSlider) = ReferenceEquals(sender, CpuWarnSlider) || ReferenceEquals(sender, CpuHotSlider)
            ? (CpuWarnSlider, CpuHotSlider)
            : (GpuWarnSlider, GpuHotSlider);
        var warn = (int)warnSlider.Value;
        var hot = (int)hotSlider.Value;

        // Kritik eşik her zaman uyarı eşiğinin üzerinde kalsın; hangi kaydırıcı oynatıldıysa diğeri itilir.
        if (hot < warn + MinThresholdGap)
        {
            _loading = true;
            if (ReferenceEquals(sender, warnSlider))
            {
                hotSlider.Value = Math.Min(warn + MinThresholdGap, (int)hotSlider.Maximum);
                warnSlider.Value = hotSlider.Value - MinThresholdGap;
            }
            else
            {
                warnSlider.Value = Math.Max(hot - MinThresholdGap, (int)warnSlider.Minimum);
                hotSlider.Value = warnSlider.Value + MinThresholdGap;
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
            StartupMethod.TaskScheduler => Loc.T("S.Startup.Task"),
            StartupMethod.Registry => Loc.T("S.Startup.Registry"),
            _ => Loc.T("S.Startup.Off")
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
            DarkMessageBox.Show(this, ex.Message, Loc.T("S.Startup.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        // Ekrandaki durum her zaman sistemdeki gerçek durumu yansıtsın.
        RefreshStartupState();
    }

    // ---- Mobil bağlantı ----

    private void MobileResetButton_Click(object sender, RoutedEventArgs e)
    {
        App.Current.MobileLink.ResetPairing();
        _shownQrUri = null;
        RefreshMobile();
    }

    /// <summary>QR kodu ve durum satırını günceller. QR yalnızca adres/anahtar değişince yeniden çizilir.</summary>
    private void RefreshMobile()
    {
        if (!_settings.Current.MobileEnabled)
        {
            _shownQrUri = null;
            MobileQrImage.Source = null;
            return;
        }

        var link = App.Current.MobileLink;
        var uri = link.BuildPairingUri();
        if (uri != _shownQrUri)
        {
            _shownQrUri = uri;
            MobileQrImage.Source = uri is null ? null : CreateQrImage(uri);
        }

        MobileQrBorder.Visibility = uri is null ? Visibility.Collapsed : Visibility.Visible;
        MobileStatusText.Text = uri is null
            ? Loc.T("S.MobileNoNetwork")
            : link.LastError is { } error
                ? Loc.T("S.MobileError", error)
                : Loc.T("S.MobileStatus", $"{link.GetLocalAddresses().First()}:{MobileLinkService.Port}", link.ClientCount);
    }

    private static BitmapImage CreateQrImage(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);

        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // ---- Durum ----

    private void RefreshStatus()
    {
        var app = App.Current;

        ElevationStatusText.Text = app.IsElevated
            ? Loc.T("S.Elevated")
            : Loc.T("S.NotElevated");
        ElevationStatusText.Foreground = (Brush)FindResource(app.IsElevated ? "GoodBrush" : "WarnBrush");

        DriverStatusText.Text = app.IsPawnIoInstalled
            ? Loc.T("S.PawnIo.Ok")
            : Loc.T("S.PawnIo.Missing");
        DriverStatusText.Foreground = (Brush)FindResource(app.IsPawnIoInstalled ? "GoodBrush" : "WarnBrush");

        var latest = app.Monitor.Latest;
        CpuNameText.Text = "CPU: " + (latest is null ? Loc.T("S.Scanning") : latest.Cpu?.Name ?? Loc.T("S.NotFound"));
        GpuNameText.Text = "GPU: " + (latest is null ? Loc.T("S.Scanning") : latest.Gpu?.Name ?? Loc.T("S.NotFound"));

        RefreshMobile();

        var fps = app.Fps;
        FpsStatusText.Text = !_settings.Current.FpsEnabled ? string.Empty
            : fps.Error == "admin" ? Loc.T("S.FpsNeedsAdmin")
            : fps.Error is { } fpsError ? Loc.T("S.FpsError", fpsError)
            : fps.Fps is float live ? Loc.T("S.FpsLive", $"{live:0}")
            : Loc.T("S.FpsWaiting");
        FpsStatusText.Visibility = FpsStatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

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
