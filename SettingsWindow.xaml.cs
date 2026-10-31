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
        BuildWidgetsTab();

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
        ShowDiskCheckBox.IsChecked = s.ShowDisk;
        ShowCoresCheckBox.IsChecked = s.ShowCores;
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
        GameSummaryCheckBox.IsChecked = s.GameSummary;
        GameSummaryCheckBox.IsEnabled = s.FpsEnabled;
        RefreshWidgetsTab();
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
        s.ShowDisk = ShowDiskCheckBox.IsChecked == true;
        s.ShowCores = ShowCoresCheckBox.IsChecked == true;
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
        s.GameSummary = GameSummaryCheckBox.IsChecked == true;
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

    // ---- Widget'lar sekmesi: her widget'ın ayrı bölümü (kodda kurulur) ----

    private readonly List<Action> _widgetRefresh = [];

    private void BuildWidgetsTab()
    {
        var s = _settings;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);

        left.Children.Add(WidgetSection("clock", "S.WidgetClock", () => s.Current.WidgetClock, v => s.Current.WidgetClock = v,
            () => s.Current.ClockOptions,
            extras: panel => panel.Children.Add(WidgetCheck("W.Seconds", () => s.Current.ClockSeconds, v => s.Current.ClockSeconds = v))));

        left.Children.Add(WidgetSection("stopwatch", "S.WidgetStopwatch", () => s.Current.WidgetStopwatch, v => s.Current.WidgetStopwatch = v,
            () => s.Current.StopwatchOptions));

        right.Children.Add(WidgetSection("tools", "S.WidgetTools", () => s.Current.WidgetTools, v => s.Current.WidgetTools = v,
            () => s.Current.ToolsOptions,
            extras: panel => panel.Children.Add(WidgetCheck("S.RemindersPhone", () => s.Current.RemindersToPhone, v => s.Current.RemindersToPhone = v))));

        right.Children.Add(WidgetSection("notes", "S.WidgetNotesTitle", () => s.Current.NotesVisible, v => s.Current.NotesVisible = v,
            () => s.Current.NotesOptions,
            extras: panel =>
            {
                var button = new Button { Content = Loc.T("W.NewNote"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                button.SetResourceReference(StyleProperty, "DarkButton");
                button.Click += (_, _) => App.Current.WidgetManager.NewNote();
                panel.Children.Add(button);
            },
            onTopChanged: pinned => App.Current.WidgetManager.PinAllNotes(pinned)));

        var hint = new TextBlock { Text = Loc.T("S.WidgetsHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 8) };
        hint.SetResourceReference(StyleProperty, "HintText");
        Grid.SetColumnSpan(hint, 3);
        Grid.SetRow(hint, 1);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(left, 0);
        Grid.SetRow(right, 0);
        grid.Children.Add(hint);

        WidgetsTabContent.Children.Add(grid);
    }

    private void RefreshWidgetsTab()
    {
        foreach (var refresh in _widgetRefresh)
        {
            refresh();
        }
    }

    private CheckBox WidgetCheck(string textKey, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = Loc.T(textKey) };
        box.SetResourceReference(StyleProperty, "DarkCheckBox");
        box.Click += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            set(box.IsChecked == true);
            _settings.NotifyChanged();
        };
        _widgetRefresh.Add(() => box.IsChecked = get());
        return box;
    }

    /// <summary>Bir widget'ın bölümü: aç/kapat, (isteğe bağlı) ek seçenekler, görünürlük, hep üstte, kilit, kenara yapış ve konum.</summary>
    private StackPanel WidgetSection(string key, string titleKey, Func<bool> getOn, Action<bool> setOn, Func<WidgetOptions> options,
        Action<StackPanel>? extras = null, Action<bool>? onTopChanged = null)
    {
        var panel = new StackPanel();
        var header = new TextBlock { Text = Loc.T(titleKey) };
        header.SetResourceReference(StyleProperty, "SectionHeader");
        panel.Children.Add(header);

        var dependents = new List<UIElement>();

        panel.Children.Add(WidgetCheck("S.WidgetShow", getOn, setOn));
        if (extras is not null)
        {
            var extraPanel = new StackPanel();
            extras(extraPanel);
            dependents.Add(extraPanel);
            panel.Children.Add(extraPanel);
        }

        // Görünürlük
        var opacityLabel = new TextBlock { Text = Loc.T("S.Opacity"), Margin = new Thickness(0, 8, 0, 0) };
        var opacityValue = new TextBlock { TextAlignment = TextAlignment.Right, MinWidth = 44 };
        var opacityRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(opacityValue, Dock.Right);
        opacityRow.Children.Add(opacityValue);
        opacityRow.Children.Add(new TextBlock { Text = Loc.T("S.Opacity") });
        _ = opacityLabel;
        var slider = new Slider { Minimum = 0.2, Maximum = 1, SmallChange = 0.05, LargeChange = 0.1, TickFrequency = 0.05, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 4) };
        slider.ValueChanged += (_, _) =>
        {
            opacityValue.Text = $"{slider.Value:P0}";
            if (_loading)
            {
                return;
            }

            options().Opacity = slider.Value;
            _settings.NotifyChanged();
        };
        panel.Children.Add(opacityRow);
        panel.Children.Add(slider);
        dependents.Add(opacityRow);
        dependents.Add(slider);

        // Davranış
        var top = WidgetCheck("S.AlwaysOnTop", () => options().AlwaysOnTop, v =>
        {
            options().AlwaysOnTop = v;
            onTopChanged?.Invoke(v);
        });
        var lockBox = WidgetCheck("S.Lock", () => options().LockPosition, v => options().LockPosition = v);
        var snap = WidgetCheck("S.Snap", () => options().SnapToEdges, v => options().SnapToEdges = v);
        foreach (var check in new[] { top, lockBox, snap })
        {
            panel.Children.Add(check);
            dependents.Add(check);
        }

        // Konum
        var placeLabel = new TextBlock { Text = Loc.T("S.WidgetPlace"), Margin = new Thickness(0, 8, 0, 4) };
        placeLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        var placeRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (glyph, corner, tip) in new (string, WidgetCorner, string)[]
                 {
                     ("↖", WidgetCorner.TopLeft, "S.PlaceTL"), ("↗", WidgetCorner.TopRight, "S.PlaceTR"),
                     ("↙", WidgetCorner.BottomLeft, "S.PlaceBL"), ("↘", WidgetCorner.BottomRight, "S.PlaceBR"),
                     ("◎", WidgetCorner.Center, "S.PlaceC")
                 })
        {
            var button = new Button { Content = glyph, ToolTip = Loc.T(tip), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3), FontSize = 14 };
            button.SetResourceReference(StyleProperty, "DarkButton");
            var target = corner;
            button.Click += (_, _) => App.Current.WidgetManager.PlaceAt(key, target);
            placeRow.Children.Add(button);
        }

        panel.Children.Add(placeLabel);
        panel.Children.Add(placeRow);
        dependents.Add(placeLabel);
        dependents.Add(placeRow);

        _widgetRefresh.Add(() =>
        {
            var o = options();
            slider.Value = o.Opacity;
            opacityValue.Text = $"{o.Opacity:P0}";
            // Widget kapalıyken ayrıntı ayarları soluk ve kullanılamaz.
            foreach (var element in dependents)
            {
                element.IsEnabled = getOn();
                element.Opacity = getOn() ? 1 : 0.5;
            }
        });

        panel.Margin = new Thickness(0, 0, 0, 14);
        return panel;
    }

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
