using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DHud.Services;

namespace DHud;

/// <summary>Tek bir fanın eğrisini düzenleme penceresi. Değişiklikler anında etkili olur ve kaydedilir.</summary>
public partial class FanCurveWindow : Window
{
    private readonly SettingsService _settings = App.Current.Settings;
    private readonly string _fanId;
    private readonly FanSetting _config;
    private readonly DispatcherTimer _liveTimer;
    private bool _loading = true;

    public FanCurveWindow(string fanId, string fanName, FanSetting config)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 24; // küçük ekranda taşmasın
        _fanId = fanId;
        _config = config;
        TitleText.Text = $"{Loc.T("F.CurveTitle")} – {fanName}";

        Editor.Points = config.Curve;
        CpuSourceRadio.IsChecked = config.Source == FanSource.Cpu;
        GpuSourceRadio.IsChecked = config.Source == FanSource.Gpu;
        MaxSourceRadio.IsChecked = config.Source == FanSource.Max;
        MinSlider.Value = config.MinPercent;
        MinValueText.Text = $"{config.MinPercent}%";

        // Eğri sürüklenirken ayar anında güncellenir (fan servisi her saniye okur); bırakınca diske yazılır.
        Editor.PointsChanged += points =>
        {
            _config.Curve = points;
            UpdateName();
        };
        Editor.Committed += () => _settings.NotifyChanged();

        // Seçili kaynağın canlı sıcaklığı ve fanın şu anki yüzdesi eğri üzerinde görünür.
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += (_, _) => RefreshLive();
        _liveTimer.Start();
        RefreshLive();
        Closed += (_, _) => _liveTimer.Stop();

        UpdateName();
        RefreshSaved();
        SaveStatusText.Text = Loc.T("F.SaveHint");

        _loading = false;
    }

    private void RefreshLive()
    {
        var snapshot = App.Current.Monitor.Latest;
        double? cpu = snapshot?.Cpu?.Temperature is > 0 ? snapshot.Cpu.Temperature : null;
        double? gpu = snapshot?.Gpu?.Temperature is > 0 ? snapshot.Gpu.Temperature : null;
        var temp = FanControlService.SourceTemp(_config.Source, cpu, gpu);
        var percent = App.Current.FanControl.Status.FirstOrDefault(s => s.Id == _fanId)?.Percent;
        Editor.Live = (temp, percent);
    }

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _config.Source = GpuSourceRadio.IsChecked == true ? FanSource.Gpu
            : MaxSourceRadio.IsChecked == true ? FanSource.Max
            : FanSource.Cpu;
        _settings.NotifyChanged();
        RefreshLive();
    }

    private void MinSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MinValueText.Text = $"{(int)MinSlider.Value}%";
        if (_loading)
        {
            return;
        }

        _config.MinPercent = (int)MinSlider.Value;
        _settings.NotifyChanged();
    }

    private void PresetQuiet_Click(object sender, RoutedEventArgs e) => ApplyPreset(FanPreset.Quiet);

    private void PresetOptimal_Click(object sender, RoutedEventArgs e) => ApplyPreset(FanPreset.Optimal);

    private void PresetMax_Click(object sender, RoutedEventArgs e) => ApplyPreset(FanPreset.MaxCold);

    /// <summary>Hazır eğriyi ve o eğriye uygun minimum hızı uygular; sonra elle ince ayar yapılabilir.</summary>
    private void ApplyPreset(FanPreset preset)
    {
        var (curve, min) = FanCurve.Preset(preset);
        _config.Curve = curve;
        _config.MinPercent = min;
        Editor.Points = curve;

        _loading = true;
        MinSlider.Value = min;
        MinValueText.Text = $"{min}%";
        _loading = false;

        _settings.NotifyChanged();
        UpdateName();
    }

    /// <summary>Üstte eğrinin adı: hazır ayar, kayıtlı eğri veya "Özel".</summary>
    private void UpdateName() =>
        CurveNameText.Text = Loc.T("F.CurveIs", FanCurve.Describe(_config.Curve, _settings.Current.CurvePresets));

    private void CurveNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveCurve_Click(sender, e);
            e.Handled = true;
        }
    }

    private void SaveCurve_Click(object sender, RoutedEventArgs e)
    {
        var name = CurveNameBox.Text.Trim();
        if (name.Length == 0 || FanCurve.IsReservedName(name))
        {
            SaveStatusText.Text = Loc.T("F.NameInvalid");
            return;
        }

        var saved = _settings.Current.CurvePresets;
        var existing = saved.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null && saved.Count >= FanCurve.MaxSavedCurves)
        {
            SaveStatusText.Text = Loc.T("F.TooMany", FanCurve.MaxSavedCurves);
            return;
        }

        // Kayıt, o anki noktaların kopyasıdır; sonradan eğriyi değiştirmek kaydı etkilemez.
        var copy = _config.Curve.Select(p => new CurvePoint { Temp = p.Temp, Percent = p.Percent }).ToList();
        if (existing is null)
        {
            saved.Add(new SavedCurve { Name = name, Curve = copy, MinPercent = _config.MinPercent });
        }
        else
        {
            existing.Curve = copy;
            existing.MinPercent = _config.MinPercent;
        }

        _settings.NotifyChanged();
        CurveNameBox.Clear();
        SaveStatusText.Text = Loc.T("F.Saved", name);
        UpdateName();
        RefreshSaved();
    }

    private void LoadSaved(SavedCurve saved)
    {
        var curve = saved.Curve.Select(p => new CurvePoint { Temp = p.Temp, Percent = p.Percent }).ToList();
        _config.Curve = curve;
        _config.MinPercent = saved.MinPercent;
        Editor.Points = curve;

        _loading = true;
        MinSlider.Value = saved.MinPercent;
        MinValueText.Text = $"{saved.MinPercent}%";
        _loading = false;

        _settings.NotifyChanged();
        UpdateName();
    }

    private void RefreshSaved()
    {
        SavedPanel.Children.Clear();
        foreach (var saved in _settings.Current.CurvePresets.ToList())
        {
            var load = new Button
            {
                Content = saved.Name, Style = (Style)FindResource("DarkButton"), Margin = new Thickness(0, 0, 2, 0)
            };
            load.Click += (_, _) => LoadSaved(saved);

            var delete = new Button
            {
                Content = "\u2715", Style = (Style)FindResource("DarkButton"), ToolTip = Loc.T("F.DeleteCurve"),
                Padding = new Thickness(8, 4, 8, 4)
            };
            delete.Click += (_, _) =>
            {
                _settings.Current.CurvePresets.Remove(saved);
                _settings.NotifyChanged();
                UpdateName();
                RefreshSaved();
            };

            var pair = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 6) };
            pair.Children.Add(load);
            pair.Children.Add(delete);
            SavedPanel.Children.Add(pair);
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
