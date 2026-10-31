using System.Windows;
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
        MinSlider.Value = config.MinPercent;
        MinValueText.Text = $"{config.MinPercent}%";

        // Eğri sürüklenirken ayar anında güncellenir (fan servisi her saniye okur); bırakınca diske yazılır.
        Editor.PointsChanged += points => _config.Curve = points;
        Editor.Committed += () => _settings.NotifyChanged();

        // Seçili kaynağın canlı sıcaklığı ve fanın şu anki yüzdesi eğri üzerinde görünür.
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += (_, _) => RefreshLive();
        _liveTimer.Start();
        RefreshLive();
        Closed += (_, _) => _liveTimer.Stop();

        _loading = false;
    }

    private void RefreshLive()
    {
        var snapshot = App.Current.Monitor.Latest;
        var temp = (_config.Source == FanSource.Gpu ? snapshot?.Gpu?.Temperature : snapshot?.Cpu?.Temperature);
        var percent = App.Current.FanControl.Status.FirstOrDefault(s => s.Id == _fanId)?.Percent;
        Editor.Live = (temp is > 0 ? temp : null, percent);
    }

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _config.Source = GpuSourceRadio.IsChecked == true ? FanSource.Gpu : FanSource.Cpu;
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
