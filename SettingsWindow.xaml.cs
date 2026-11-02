using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;
using System.Windows.Media.Imaging;
using DHud.Services;
using DHud.Widgets;
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
        BuildProfilesTab();

        // Küçük ekranda pencere ekrandan taşmasın: sekme içeriği kalan yüksekliğe sığar, gerekirse kaydırılır.
        // (başlık, sekmeler, durum kutusu ve düğmeler için yaklaşık 330 px ayrılır)
        // Pencere ekranın üstüne yakın açılır (uzun sekmelerde alt kısım ekrandan taşmasın); tepede ~32 px boşluk için 40 px ayrılır.
        var work = SystemParameters.WorkArea;
        TabScroll.MaxHeight = Math.Max(260, work.Height - 330 - 40);
        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + 8;

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
        ShowOverlayCheckBox.IsChecked = s.ShowOverlay;
        GameSummaryCheckBox.IsChecked = s.GameSummary;
        GameSummaryCheckBox.IsEnabled = s.FpsEnabled;
        HideWidgetsFsCheckBox.IsChecked = s.HideWidgetsOnFullscreen;
        HideBarFsCheckBox.IsChecked = s.HideBarOnFullscreen;
        RefreshWidgetsTab();
        MobileCheckBox.IsChecked = s.MobileEnabled;
        MobileFanCheckBox.IsChecked = s.MobileFanControl;
        MobileTasksCheckBox.IsChecked = s.MobileTasks;
        MobileWidgetsCheckBox.IsChecked = s.MobileWidgets;
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
        s.ShowOverlay = ShowOverlayCheckBox.IsChecked == true;
        s.GameSummary = GameSummaryCheckBox.IsChecked == true;
        s.HideWidgetsOnFullscreen = HideWidgetsFsCheckBox.IsChecked == true;
        s.HideBarOnFullscreen = HideBarFsCheckBox.IsChecked == true;
        s.MobileEnabled = MobileCheckBox.IsChecked == true;
        s.MobileFanControl = MobileFanCheckBox.IsChecked == true;
        s.MobileTasks = MobileTasksCheckBox.IsChecked == true;
        s.MobileWidgets = MobileWidgetsCheckBox.IsChecked == true;
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
        // Kartlar üç gruba ayrılır; her grup yatay akan (WrapPanel) bir kart sırasıdır.
        var timePanel = new WrapPanel();
        var notesPanel = new WrapPanel();
        var systemPanel = new WrapPanel();
        void Add(FrameworkElement card)
        {
            var panel = (string)card.Tag switch
            {
                "clock" or "stopwatch" or "tools" or "weather" or WidgetIds.Countdown or WidgetIds.Calendar => timePanel,
                "notes" or WidgetIds.Todo or WidgetIds.Clipboard or WidgetIds.Launcher or WidgetIds.Notepad or WidgetIds.Market => notesPanel,
                _ => systemPanel
            };
            panel.Children.Add(card);
        }

        Add(WidgetSection("clock", "S.WidgetClock", () => s.Current.WidgetClock, v => s.Current.WidgetClock = v,
            () => s.Current.ClockOptions,
            extras: panel => panel.Children.Add(WidgetCheck("W.Seconds", () => s.Current.ClockSeconds, v => s.Current.ClockSeconds = v))));

        Add(WidgetSection("stopwatch", "S.WidgetStopwatch", () => s.Current.WidgetStopwatch, v => s.Current.WidgetStopwatch = v,
            () => s.Current.StopwatchOptions));

        Add(WidgetSection("tools", "S.WidgetTools", () => s.Current.WidgetTools, v => s.Current.WidgetTools = v,
            () => s.Current.ToolsOptions,
            extras: panel => panel.Children.Add(WidgetCheck("S.RemindersPhone", () => s.Current.RemindersToPhone, v => s.Current.RemindersToPhone = v))));

        Add(WidgetSection("weather", "S.WidgetWeather", () => s.Current.WidgetWeather, v => s.Current.WidgetWeather = v,
            () => s.Current.WeatherOptions,
            extras: panel =>
            {
                panel.Children.Add(WidgetCheck("W.Fahrenheit", () => s.Current.WeatherFahrenheit, v => s.Current.WeatherFahrenheit = v));
                var city = new Button { Content = Loc.T("W.ChangeCity"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                city.SetResourceReference(StyleProperty, "DarkButton");
                city.Click += (_, _) =>
                {
                    s.Current.WeatherLat = s.Current.WeatherLon = null;
                    s.Current.WeatherPlace = string.Empty;
                    s.NotifyChanged();
                };
                panel.Children.Add(city);
                var privacy = new TextBlock { Text = Loc.T("S.WeatherPrivacy"), TextWrapping = TextWrapping.Wrap };
                privacy.SetResourceReference(StyleProperty, "HintText");
                panel.Children.Add(privacy);
            }));

        Add(WidgetSection("notes", "S.WidgetNotesTitle", () => s.Current.NotesVisible, v => s.Current.NotesVisible = v,
            () => s.Current.NotesOptions,
            extras: panel =>
            {
                panel.Children.Add(WidgetCheck("S.NotesStacked", () => s.Current.NotesStacked, v => s.Current.NotesStacked = v));
                var button = new Button { Content = Loc.T("W.NewNote"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                button.SetResourceReference(StyleProperty, "DarkButton");
                button.Click += (_, _) => App.Current.WidgetManager.NewNote();
                panel.Children.Add(button);
            },
            onTopChanged: pinned => App.Current.WidgetManager.PinAllNotes(pinned)));

        // Genel widget'lar: açma/kapama WidgetOptions.Enabled üzerinden.
        WidgetOptions Extra(string id) => s.Current.Extra(id);
        FrameworkElement Generic(string id, string titleKey, Action<StackPanel>? extras = null) =>
            WidgetSection(id, titleKey, () => Extra(id).Enabled, v => Extra(id).Enabled = v, () => Extra(id), extras);

        Add(Generic(WidgetIds.SysInfo, "S.WidgetSysInfo"));
        Add(Generic(WidgetIds.Todo, "S.WidgetTodo"));
        Add(Generic(WidgetIds.Countdown, "S.WidgetCountdown"));
        Add(Generic(WidgetIds.Launcher, "S.WidgetLauncher"));
        Add(Generic(WidgetIds.Clipboard, "S.WidgetClipboard", panel =>
        {
            var images = WidgetCheck("S.ClipboardImages", () => s.Current.ClipboardImages, v => s.Current.ClipboardImages = v);
            images.ToolTip = Loc.T("S.ClipboardImagesTip");
            panel.Children.Add(images);
            var clear = new Button { Content = Loc.T("W.ClipClear"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            clear.SetResourceReference(StyleProperty, "DarkButton");
            clear.Click += (_, _) => ClipboardWidgetWindow.ClearHistory();
            panel.Children.Add(clear);
            var note = new TextBlock { Text = Loc.T("S.ClipboardPrivacy"), TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(StyleProperty, "HintText");
            panel.Children.Add(note);
        }));
        Add(Generic(WidgetIds.Audio, "S.WidgetAudio"));
        Add(Generic(WidgetIds.Media, "S.WidgetMedia"));
        Add(Generic(WidgetIds.TaskMan, "S.WidgetTaskMan", panel =>
        {
            var note = new TextBlock { Text = Loc.T("S.TaskManNote"), TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(StyleProperty, "HintText");
            panel.Children.Add(note);
        }));
        Add(Generic(WidgetIds.Battery, "S.WidgetBattery"));
        Add(Generic(WidgetIds.Notepad, "S.WidgetNotepad"));
        Add(Generic(WidgetIds.Market, "S.WidgetMarket", panel =>
        {
            var reset = new Button { Content = Loc.T("S.MarketReset"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            reset.SetResourceReference(StyleProperty, "DarkButton");
            reset.Click += (_, _) =>
            {
                App.Current.Widgets.Data.Markets = [.. MarketService.DefaultSymbols];
                App.Current.Widgets.ScheduleSave();
                s.NotifyChanged();
            };
            panel.Children.Add(reset);
            var privacy = new TextBlock { Text = Loc.T("S.MarketPrivacy"), TextWrapping = TextWrapping.Wrap };
            privacy.SetResourceReference(StyleProperty, "HintText");
            panel.Children.Add(privacy);
        }));
        Add(Generic(WidgetIds.Calendar, "S.WidgetCalendar", panel =>
        {
            var label = new TextBlock { Text = Loc.T("S.CalendarSource"), Margin = new Thickness(0, 6, 0, 2) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            var box = new TextBox { Padding = new Thickness(6, 3, 6, 3), Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1), CaretBrush = Brushes.White, ToolTip = Loc.T("S.CalendarHint") };
            box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            void Commit()
            {
                if (_loading || s.Current.CalendarSource == box.Text.Trim())
                {
                    return;
                }

                s.Current.CalendarSource = box.Text.Trim();
                s.NotifyChanged();
            }

            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); } };
            _widgetRefresh.Add(() => { if (!box.IsKeyboardFocusWithin) { box.Text = s.Current.CalendarSource; } });

            var browse = new Button { Content = Loc.T("S.CalendarBrowse"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            browse.SetResourceReference(StyleProperty, "DarkButton");
            browse.Click += (_, _) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "iCalendar (*.ics)|*.ics|*.*|*.*", CheckFileExists = true };
                if (dialog.ShowDialog() == true)
                {
                    box.Text = dialog.FileName;
                    Commit();
                }
            };
            var hintText = new TextBlock { Text = Loc.T("S.CalendarHint"), TextWrapping = TextWrapping.Wrap };
            hintText.SetResourceReference(StyleProperty, "HintText");
            panel.Children.Add(label);
            panel.Children.Add(box);
            panel.Children.Add(browse);
            panel.Children.Add(hintText);
        }));
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(BuildThemePicker());
        root.Children.Add(BuildScalePicker());
        var gaps = WidgetCheck("S.CloseGaps", () => s.Current.CloseWidgetGaps, v => s.Current.CloseWidgetGaps = v);
        gaps.Margin = new Thickness(0, 4, 0, 4);
        gaps.ToolTip = Loc.T("S.CloseGapsTip");
        root.Children.Add(gaps);
        foreach (var (titleKey, panel) in new[] { ("S.GroupTime", timePanel), ("S.GroupNotes", notesPanel), ("S.GroupSystem", systemPanel) })
        {
            var title = new TextBlock { Text = Loc.T(titleKey) };
            title.SetResourceReference(StyleProperty, "SectionHeader");
            root.Children.Add(title);
            root.Children.Add(panel);
        }

        var hint = new TextBlock { Text = Loc.T("S.WidgetsHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
        hint.SetResourceReference(StyleProperty, "HintText");
        root.Children.Add(hint);
        WidgetsTabContent.Children.Add(root);
    }

    /// <summary>Widget renk teması seçici: her tema bir renk topu (kart rengi + ortada vurgu noktası); seçili olanın çevresi parlar.</summary>
    private FrameworkElement BuildThemePicker()
    {
        var panel = new StackPanel();
        var header = new TextBlock { Text = Loc.T("S.WidgetTheme") };
        header.SetResourceReference(StyleProperty, "SectionHeader");
        panel.Children.Add(header);

        var row = new WrapPanel();
        var swatches = new List<(WidgetTheme Theme, Border Ring)>();
        foreach (var theme in WidgetThemes.All)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(theme.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ball = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromRgb(theme.Card.R, theme.Card.G, theme.Card.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = dot
            };
            var ring = new Border
            {
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = Loc.T(theme.NameKey),
                Child = ball,
                Background = Brushes.Transparent
            };
            var id = theme.Id;
            ring.MouseLeftButtonUp += (_, _) =>
            {
                if (_settings.Current.WidgetTheme != id)
                {
                    _settings.Current.WidgetTheme = id;
                    _settings.NotifyChanged();
                }
            };
            swatches.Add((theme, ring));
            row.Children.Add(ring);
        }

        void Refresh()
        {
            var current = WidgetThemes.Get(_settings.Current.WidgetTheme).Id;
            foreach (var (theme, ring) in swatches)
            {
                ring.BorderBrush = theme.Id == current ? new SolidColorBrush(theme.Accent) : Brushes.Transparent;
            }
        }

        _widgetRefresh.Add(Refresh);
        Refresh();
        panel.Children.Add(row);
        var hint = new TextBlock { Text = Loc.T("S.WidgetThemeHint"), TextWrapping = TextWrapping.Wrap };
        hint.SetResourceReference(StyleProperty, "HintText");
        hint.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(hint);
        var transparent = WidgetCheck("S.WidgetTransparent", () => _settings.Current.WidgetTransparent, v => _settings.Current.WidgetTransparent = v);
        transparent.Margin = new Thickness(0, 8, 0, 2);
        transparent.ToolTip = Loc.T("S.WidgetTransparentHint");
        panel.Children.Add(transparent);
        return panel;
    }

    /// <summary>Tüm widget'ların ortak boyutu (%50–100). Kaydırırken her adımda yeniden yerleşmesin diye kısa bir bekleme sonrası uygulanır.</summary>
    private FrameworkElement BuildScalePicker()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var header = new TextBlock { Text = Loc.T("S.WidgetScale") };
        header.SetResourceReference(StyleProperty, "SectionHeader");
        panel.Children.Add(header);

        var label = new TextBlock { Width = 44, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = 50, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10, Width = 220, VerticalAlignment = VerticalAlignment.Center };
        var debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            var scale = Math.Round(slider.Value) / 100;
            if (Math.Abs(_settings.Current.WidgetScale - scale) > 0.001)
            {
                _settings.Current.WidgetScale = scale;
                _settings.NotifyChanged();
            }
        };
        slider.ValueChanged += (_, _) =>
        {
            label.Text = $"{Math.Round(slider.Value):0}%";
            if (!_loading)
            {
                debounce.Stop();
                debounce.Start();
            }
        };
        _widgetRefresh.Add(() =>
        {
            slider.Value = Math.Clamp(Math.Round(_settings.Current.WidgetScale * 100), 50, 100);
            label.Text = $"{slider.Value:0}%";
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(label);
        panel.Children.Add(row);
        var hint = new TextBlock { Text = Loc.T("S.WidgetScaleHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        hint.SetResourceReference(StyleProperty, "HintText");
        panel.Children.Add(hint);
        return panel;
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
    private FrameworkElement WidgetSection(string key, string titleKey, Func<bool> getOn, Action<bool> setOn, Func<WidgetOptions> options,
        Action<StackPanel>? extras = null, Action<bool>? onTopChanged = null)
    {
        var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) }; // ayrıntılar (kartta "Ayarlar" ile açılır)
        var dependents = new List<UIElement>();
        var enable = WidgetCheck("S.WidgetShow", getOn, setOn);
        enable.Content = null;
        enable.ToolTip = Loc.T("S.WidgetShow");
        enable.HorizontalAlignment = HorizontalAlignment.Right;
        enable.VerticalAlignment = VerticalAlignment.Center;
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
        var snap = WidgetCheck("S.SnapGrid", () => options().SnapToEdges, v => options().SnapToEdges = v);
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

        // Kart: başlık + aç/kapat anahtarı, altında "Ayarlar" düğmesi ve açılır ayrıntılar.
        var title = new TextBlock { Text = Loc.T(titleKey), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(enable, 1);
        headerRow.Children.Add(title);
        headerRow.Children.Add(enable);

        var toggle = new Button { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), FontSize = 12 };
        toggle.SetResourceReference(StyleProperty, "DarkButton");
        void SetToggleText() => toggle.Content = (panel.Visibility == Visibility.Visible ? "⚙  ▴  " : "⚙  ▾  ") + Loc.T("S.WidgetOptions");
        SetToggleText();
        panel.Tag = (Action)SetToggleText; // arama sonucu bu ayrıntıları açarken düğme yazısını da günceller
        title.Tag = "card";                // arama bu başlığı bulur
        toggle.Click += (_, _) =>
        {
            panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            SetToggleText();
        };

        // Kısa açıklama: bu widget ne işe yarar (yeni kullanıcı için).
        var description = new TextBlock { Text = Loc.T("S.Desc." + key), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        var body = new StackPanel();
        body.Children.Add(headerRow);
        body.Children.Add(description);
        body.Children.Add(toggle);
        body.Children.Add(panel);
        return new Border
        {
            Tag = key,
            Width = 236,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = body
        };
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
                : Loc.T("S.MobileStatus", $"{link.GetLocalAddresses().First()}:{link.Port}", link.ClientCount);
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
