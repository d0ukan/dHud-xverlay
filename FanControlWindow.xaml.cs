using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DHud.Services;

namespace DHud;

/// <summary>Fan kontrol penceresi: her fan için Otomatik / Manuel / Eğri seçimi.</summary>
public partial class FanControlWindow : Window
{
    private readonly SettingsService _settings = App.Current.Settings;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, TextBlock> _liveTexts = [];
    private readonly Dictionary<string, TextBlock> _curveLabels = [];
    private string _builtFor = string.Empty;

    public FanControlWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 24; // küçük ekranda taşmasın
        EnableCheckBox.IsChecked = _settings.Current.FanControlEnabled;

        // Pencere açıkken anakart sensörleri taranır; böylece kontrol kapalıyken de fanlar listelenir.
        App.Current.FanWindowOpen = true;
        App.Current.ApplyMonitorFlags();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();

        Closed += (_, _) =>
        {
            _timer.Stop();
            App.Current.FanWindowOpen = false;
            App.Current.ApplyMonitorFlags();
        };
    }

    private void EnableCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var enable = EnableCheckBox.IsChecked == true;
        if (enable && MessageBox.Show(this, Loc.T("F.Confirm"), Loc.T("F.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            EnableCheckBox.IsChecked = false;
            return;
        }

        _settings.Current.FanControlEnabled = enable;
        _settings.NotifyChanged();
    }

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

        // Eğri modundaki fanın yanında eğrinin adı (hazır ayar / kayıtlı ad / Özel). Eğri penceresinde değişince güncellenir.
        foreach (var (id, label) in _curveLabels)
        {
            var curve = _settings.Current.Fans.TryGetValue(id, out var cfg) ? cfg.Curve : FanCurve.Default();
            label.Text = Loc.T("F.CurveIs", FanCurve.Describe(curve, _settings.Current.CurvePresets));
        }
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

        foreach (var channel in channels)
        {
            FanList.Children.Add(BuildRow(channel));
        }
    }

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

        // Eğri: düzenleme düğmesi
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

        void ApplyModeVisibility(FanMode m)
        {
            manualPanel.Visibility = m == FanMode.Manual ? Visibility.Visible : Visibility.Collapsed;
            curvePanel.Visibility = m == FanMode.Curve ? Visibility.Visible : Visibility.Collapsed;
        }

        ApplyModeVisibility(mode);

        var radios = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        radios.Children.Add(MakeRadio(id, Loc.T("F.Auto"), mode == FanMode.Auto, () => SetMode(id, FanMode.Auto, ApplyModeVisibility)));
        radios.Children.Add(MakeRadio(id, Loc.T("F.Manual"), mode == FanMode.Manual, () => SetMode(id, FanMode.Manual, ApplyModeVisibility)));
        radios.Children.Add(MakeRadio(id, Loc.T("F.Curve"), mode == FanMode.Curve, () => SetMode(id, FanMode.Curve, ApplyModeVisibility)));

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(radios);
        content.Children.Add(manualPanel);
        content.Children.Add(curvePanel);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
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

    private void SetMode(string id, FanMode mode, Action<FanMode> applyVisibility)
    {
        GetOrCreate(id).Mode = mode;
        applyVisibility(mode);
        _settings.NotifyChanged();
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
