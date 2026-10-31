using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DHud.Services;

namespace DHud;

/// <summary>Fan kontrol penceresi: her fan için Otomatik / Manuel / Eğri, tüm fanlara hazır eğri, profiller ve fan testi.</summary>
public partial class FanControlWindow : Window
{
    private readonly SettingsService _settings = App.Current.Settings;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, TextBlock> _liveTexts = [];
    private readonly Dictionary<string, TextBlock> _curveLabels = [];
    private readonly Dictionary<string, TextBlock> _testLabels = [];
    private string _builtFor = string.Empty;
    private bool _testAllActive;
    private bool _testAllSawRunning;

    public FanControlWindow()
    {
        InitializeComponent();
        // Geniş ve alçak: fan kartları ekran genişliğine göre 2-4 sütun olur, böylece kaydırma gerekmez.
        var screenWidth = SystemParameters.WorkArea.Width;
        var columns = screenWidth >= 1700 ? 4 : screenWidth >= 1250 ? 3 : 2;
        FanList.Columns = columns;
        Width = Math.Min(columns * 316 + 64, screenWidth - 24);

        // Yine de çok küçük ekranda taşmasın: içerik kaydırılır (başlık için ~70 px ayrılır).
        MainScroll.MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 24 - 70);
        EnableCheckBox.IsChecked = _settings.Current.FanControlEnabled;
        ProfileStatusText.Text = Loc.T("F.ProfileHint");

        // Pencere açıkken anakart sensörleri taranır; böylece kontrol kapalıyken de fanlar listelenir.
        App.Current.FanWindowOpen = true;
        App.Current.ApplyMonitorFlags();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        RefreshProfiles();
        Refresh();

        Closed += (_, _) =>
        {
            _timer.Stop();
            App.Current.FanWindowOpen = false;
            App.Current.ApplyMonitorFlags();
        };
    }

    // ---- Etkinleştirme ----

    private void EnableCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var enable = EnableCheckBox.IsChecked == true;
        if (enable && !Confirm(Loc.T("F.Confirm")))
        {
            EnableCheckBox.IsChecked = false;
            return;
        }

        _settings.Current.FanControlEnabled = enable;
        _settings.NotifyChanged();
    }

    private bool Confirm(string text) =>
        DarkMessageBox.Show(this, text, Loc.T("F.Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    // ---- Yenileme ----

    private void Refresh()
    {
        var channels = App.Current.Monitor.FanChannels;
        var key = string.Join("|", channels.Select(c => c.Id));
        if (key != _builtFor)
        {
            _builtFor = key;
            Rebuild(channels);
        }

        NoFansText.Visibility = channels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmergencyText.Visibility = App.Current.FanControl.Emergency ? Visibility.Visible : Visibility.Collapsed;

        var status = App.Current.FanControl.Status;
        foreach (var (id, text) in _liveTexts)
        {
            var s = status.FirstOrDefault(x => x.Id == id);
            text.Text = s is null
                ? string.Empty
                : Loc.T("F.Live", s.Rpm is float rpm ? $"{rpm:0}" : "--", s.Percent is float p ? $"{p:0}" : "--");
        }

        // Eğri modundaki fanın yanında eğrinin adı (hazır ayar / kayıtlı ad / Özel).
        foreach (var (id, label) in _curveLabels)
        {
            var curve = _settings.Current.Fans.TryGetValue(id, out var cfg) ? cfg.Curve : FanCurve.Default();
            label.Text = Loc.T("F.CurveIs", FanCurve.Describe(curve, _settings.Current.CurvePresets));
        }

        foreach (var (id, label) in _testLabels)
        {
            ShowTestState(label, status.FirstOrDefault(x => x.Id == id));
        }

        UpdateTestAll(status);
    }

    // ---- Tüm fanları test et ----

    private void TestAll_Click(object sender, RoutedEventArgs e)
    {
        var count = App.Current.Monitor.FanChannels.Count;
        if (count == 0 || !Confirm(Loc.T("F.ConfirmTestAll", count, FanControlService.TestSeconds)))
        {
            return;
        }

        _testAllActive = true;
        _testAllSawRunning = false;
        TestAllStatusText.Foreground = (Brush)FindResource("SubtleTextBrush");
        TestAllStatusText.Text = Loc.T("F.TestAllRunning");
        TestAllStatusText.Visibility = Visibility.Visible;
        App.Current.FanControl.StartTestAll();
    }

    /// <summary>Tüm testler bitince özet: kaç fan yanıt verdi, hangileri vermedi veya doğrulanamadı.</summary>
    private void UpdateTestAll(IReadOnlyList<FanStatus> status)
    {
        if (!_testAllActive || status.Count == 0)
        {
            return;
        }

        // Eski sonuçlar yeni testle karışmasın: önce en az bir fanın çalıştığı görülmeli.
        _testAllSawRunning |= status.Any(s => s.TestRunning);
        if (!_testAllSawRunning || status.Any(s => s.TestRunning) || !status.All(s => s.TestDone))
        {
            return;
        }

        _testAllActive = false;
        var verifiable = status.Where(s => s.HasRpm).ToList();
        var noResponse = verifiable.Where(s => !s.TestOk).Select(s => s.Name).ToList();
        var noSensor = status.Where(s => !s.HasRpm).Select(s => s.Name).ToList();

        var lines = new List<string> { Loc.T("F.TestAllResult", verifiable.Count - noResponse.Count, verifiable.Count) };
        if (noResponse.Count > 0)
        {
            lines.Add(Loc.T("F.TestAllNoResponse", string.Join(", ", noResponse)));
        }

        if (noSensor.Count > 0)
        {
            lines.Add(Loc.T("F.TestAllNoSensor", string.Join(", ", noSensor)));
        }

        TestAllStatusText.Text = string.Join("\n", lines);
        TestAllStatusText.Foreground = (Brush)FindResource(noResponse.Count == 0 ? "GoodBrush" : "WarnBrush");
    }

    private void ShowTestState(TextBlock label, FanStatus? s)
    {
        string? text = null;
        var brush = (Brush)FindResource("SubtleTextBrush");

        if (s is { TestRunning: true })
        {
            text = Loc.T("F.Testing");
        }
        else if (s is { TestDone: true })
        {
            if (!s.HasRpm)
            {
                text = Loc.T("F.TestNoRpm");
            }
            else if (s.TestOk)
            {
                text = Loc.T("F.TestOk", $"{s.TestBefore:0}", $"{s.TestPeak:0}");
                brush = (Brush)FindResource("GoodBrush");
            }
            else
            {
                text = Loc.T("F.TestFail", $"{s.TestBefore:0}", $"{s.TestPeak:0}");
                brush = (Brush)FindResource("WarnBrush");
            }
        }
        else if (s is { Stalled: true })
        {
            text = Loc.T("F.StalledShort");
            brush = (Brush)FindResource("HotBrush");
        }

        label.Text = text ?? string.Empty;
        label.Foreground = brush;
        label.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private FanSetting GetOrCreate(string id)
    {
        if (!_settings.Current.Fans.TryGetValue(id, out var cfg))
        {
            cfg = new FanSetting();
            _settings.Current.Fans[id] = cfg;
        }

        return cfg;
    }

    private void Rebuild(IReadOnlyList<FanChannel> channels)
    {
        FanList.Children.Clear();
        _liveTexts.Clear();
        _curveLabels.Clear();
        _testLabels.Clear();

        foreach (var channel in channels)
        {
            FanList.Children.Add(BuildRow(channel));
        }
    }

    private static bool IsPump(string name) =>
        name.Contains("pump", StringComparison.OrdinalIgnoreCase) || name.Contains("AIO", StringComparison.OrdinalIgnoreCase);

    // ---- Fan satırı ----

    private UIElement BuildRow(FanChannel channel)
    {
        var id = channel.Id;
        _settings.Current.Fans.TryGetValue(id, out var existing);
        var mode = existing?.Mode ?? FanMode.Auto;

        var live = new TextBlock { Foreground = (Brush)FindResource("SubtleTextBrush"), HorizontalAlignment = HorizontalAlignment.Right };
        _liveTexts[id] = live;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = channel.Name, FontWeight = FontWeights.SemiBold });
        Grid.SetColumn(live, 1);
        header.Children.Add(live);

        // Manuel: kaydırıcı
        var slider = new Slider
        {
            Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 5, TickFrequency = 1,
            IsSnapToTickEnabled = true, Value = existing?.ManualPercent ?? 50, Margin = new Thickness(0, 4, 0, 0)
        };
        var sliderText = new TextBlock { Text = $"{(int)slider.Value}%", MinWidth = 44, TextAlignment = TextAlignment.Right };
        var manualPanel = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(sliderText, Dock.Right);
        manualPanel.Children.Add(sliderText);
        manualPanel.Children.Add(slider);
        slider.ValueChanged += (_, _) =>
        {
            sliderText.Text = $"{(int)slider.Value}%";
            GetOrCreate(id).ManualPercent = (int)slider.Value;
            _settings.NotifyChanged();
        };

        // Eğri: düzenleme düğmesi ve eğrinin adı
        var curveButton = new Button
        {
            Content = Loc.T("F.EditCurve"), Style = (Style)FindResource("DarkButton"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var curveLabel = new TextBlock
        {
            Foreground = (Brush)FindResource("SubtleTextBrush"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        _curveLabels[id] = curveLabel;
        var curvePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        curvePanel.Children.Add(curveButton);
        curvePanel.Children.Add(curveLabel);
        curveButton.Click += (_, _) =>
        {
            var cfg = GetOrCreate(id);
            new FanCurveWindow(id, channel.Name, cfg) { Owner = this }.Show();
        };

        // Satır içi uyarı (seçilen dilde)
        var warn = new TextBlock
        {
            Foreground = (Brush)FindResource("WarnBrush"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed
        };

        // Fan testi sonucu
        var testLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        _testLabels[id] = testLabel;

        void ApplyModeUi(FanMode m)
        {
            manualPanel.Visibility = m == FanMode.Manual ? Visibility.Visible : Visibility.Collapsed;
            curvePanel.Visibility = m == FanMode.Curve ? Visibility.Visible : Visibility.Collapsed;

            var text = m != FanMode.Auto && IsPump(channel.Name) ? Loc.T("F.WarnPump")
                : m == FanMode.Manual ? Loc.T("F.WarnManual")
                : null;
            warn.Text = text ?? string.Empty;
            warn.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        }

        ApplyModeUi(mode);

        var radios = new StackPanel { Orientation = Orientation.Horizontal };
        radios.Children.Add(MakeRadio(id, Loc.T("F.Auto"), mode == FanMode.Auto, () => SetMode(id, FanMode.Auto, ApplyModeUi)));
        radios.Children.Add(MakeRadio(id, Loc.T("F.Manual"), mode == FanMode.Manual, () => SetMode(id, FanMode.Manual, ApplyModeUi)));
        radios.Children.Add(MakeRadio(id, Loc.T("F.Curve"), mode == FanMode.Curve, () => SetMode(id, FanMode.Curve, ApplyModeUi)));

        var testButton = new Button { Content = Loc.T("F.Test"), Style = (Style)FindResource("DarkButton") };
        testButton.Click += (_, _) =>
        {
            if (Confirm(Loc.T("F.ConfirmTest", channel.Name, FanControlService.TestSeconds)))
            {
                App.Current.FanControl.StartTest(id);
            }
        };

        // Dar kartta yer kazanmak için Test düğmesi başlık satırına, adın ve devrin yanına konur.
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        testButton.Margin = new Thickness(10, 0, 0, 0);
        testButton.Padding = new Thickness(10, 3, 10, 3);
        Grid.SetColumn(testButton, 2);
        header.Children.Add(testButton);
        radios.Margin = new Thickness(0, 6, 0, 0);
        var topLine = radios;

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(topLine);
        content.Children.Add(manualPanel);
        content.Children.Add(curvePanel);
        content.Children.Add(warn);
        content.Children.Add(testLabel);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 8, 8),
            Child = content
        };
    }

    private RadioButton MakeRadio(string group, string label, bool isChecked, Action onChecked)
    {
        var radio = new RadioButton
        {
            Content = label, GroupName = group, IsChecked = isChecked,
            Style = (Style)FindResource("DarkRadioButton"), Margin = new Thickness(0, 0, 16, 0)
        };
        radio.Checked += (_, _) => onChecked();
        return radio;
    }

    private void SetMode(string id, FanMode mode, Action<FanMode> applyUi)
    {
        GetOrCreate(id).Mode = mode;
        applyUi(mode);
        _settings.NotifyChanged();
    }

    // ---- Tüm fanlara hazır eğri ----

    private void AllQuiet_Click(object sender, RoutedEventArgs e) => ApplyPresetToAll(FanPreset.Quiet);

    private void AllOptimal_Click(object sender, RoutedEventArgs e) => ApplyPresetToAll(FanPreset.Optimal);

    private void AllMax_Click(object sender, RoutedEventArgs e) => ApplyPresetToAll(FanPreset.MaxCold);

    /// <summary>
    /// Hazır eğriyi tüm fanlara uygular: GPU fanları GPU'yu, "CPU Fan" CPU'yu, diğerleri CPU ve GPU'nun yüksek olanını izler.
    /// Pompalar, kutu işaretli değilse atlanır (yavaş pompa CPU'yu ısıtır).
    /// </summary>
    private void ApplyPresetToAll(FanPreset preset)
    {
        var includePump = IncludePumpCheckBox.IsChecked == true;
        var targets = App.Current.Monitor.FanChannels.Where(c => includePump || !IsPump(c.Name)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var notes = includePump ? Loc.T("F.NotePumpIn") : Loc.T("F.NotePumpOut");
        if (!_settings.Current.FanControlEnabled)
        {
            notes += "\n" + Loc.T("F.NoteOff");
        }

        if (!Confirm(Loc.T("F.ConfirmAll", FanCurve.PresetName(preset), targets.Count, notes)))
        {
            return;
        }

        var (curve, min) = FanCurve.Preset(preset);
        foreach (var channel in targets)
        {
            var cfg = GetOrCreate(channel.Id);
            cfg.Mode = FanMode.Curve;
            cfg.Curve = curve.Select(p => new CurvePoint { Temp = p.Temp, Percent = p.Percent }).ToList();
            cfg.MinPercent = min;
            cfg.Source = channel.IsGpu ? FanSource.Gpu
                : channel.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ? FanSource.Cpu
                : FanSource.Max;
        }

        _settings.NotifyChanged();
        ForceRebuild();
    }

    private void ForceRebuild()
    {
        _builtFor = string.Empty;
        Refresh();
    }

    // ---- Profiller ----

    private void ProfileNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveProfile_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (name.Length == 0)
        {
            ProfileStatusText.Text = Loc.T("F.ProfileInvalid");
            return;
        }

        var profiles = _settings.Current.FanProfiles;
        var existing = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null && profiles.Count >= FanCurve.MaxProfiles)
        {
            ProfileStatusText.Text = Loc.T("F.ProfileTooMany", FanCurve.MaxProfiles);
            return;
        }

        // Profil, o anki ayarların kopyasıdır; sonradan fanları değiştirmek profili etkilemez.
        var copy = _settings.Current.Fans.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        if (existing is null)
        {
            profiles.Add(new FanProfile { Name = name, Fans = copy });
        }
        else
        {
            existing.Fans = copy;
        }

        _settings.NotifyChanged();
        ProfileNameBox.Clear();
        ProfileStatusText.Text = Loc.T("F.ProfileSaved", name);
        RefreshProfiles();
    }

    private void LoadProfile(FanProfile profile)
    {
        if (!Confirm(Loc.T("F.ConfirmProfile", profile.Name)))
        {
            return;
        }

        _settings.Current.Fans = profile.Fans.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        _settings.NotifyChanged();
        ForceRebuild();
    }

    private void RefreshProfiles()
    {
        ProfilePanel.Children.Clear();
        foreach (var profile in _settings.Current.FanProfiles.ToList())
        {
            var load = new Button
            {
                Content = profile.Name, Style = (Style)FindResource("DarkButton"), Margin = new Thickness(0, 0, 2, 0)
            };
            load.Click += (_, _) => LoadProfile(profile);

            var delete = new Button
            {
                Content = "✕", Style = (Style)FindResource("DarkButton"), ToolTip = Loc.T("F.DeleteProfile"),
                Padding = new Thickness(8, 4, 8, 4)
            };
            delete.Click += (_, _) =>
            {
                _settings.Current.FanProfiles.Remove(profile);
                _settings.NotifyChanged();
                RefreshProfiles();
            };

            var pair = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 6) };
            pair.Children.Add(load);
            pair.Children.Add(delete);
            ProfilePanel.Children.Add(pair);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
