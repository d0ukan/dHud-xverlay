using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DHud.Services;

namespace DHud;

/// <summary>Oyun bitince sağ altta beliren, tıklanınca (veya 30 saniye sonra) kapanan özet kartı.</summary>
public partial class GameSummaryWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };

    public GameSummaryWindow(GameSummary s)
    {
        InitializeComponent();

        TitleText.Text = Loc.T("G.Title");
        var minutes = (int)Math.Round(s.Duration.TotalMinutes);
        SubtitleText.Text = Loc.T("G.Duration", minutes);
        CloseHint.Text = Loc.T("G.Close");

        AddRow(Loc.T("G.AvgFps"), $"{s.AvgFps:0}");
        AddRow(Loc.T("G.Low1"), $"{s.OnePercentLow:0}");
        AddRow(Loc.T("G.MinMax"), $"{s.MinFps:0} / {s.MaxFps:0}");
        if (s.MaxCpuTemp is float cpuMax)
        {
            AddRow(Loc.T("G.Cpu"), $"{cpuMax:0}°C  ({Loc.T("G.AvgShort")} {s.AvgCpuTemp:0})");
        }

        if (s.MaxGpuTemp is float gpuMax)
        {
            AddRow(Loc.T("G.Gpu"), $"{gpuMax:0}°C  ({Loc.T("G.AvgShort")} {s.AvgGpuTemp:0})");
        }

        // Sağ alt köşe (görev çubuğunun üstü).
        var area = SystemParameters.WorkArea;
        Loaded += (_, _) =>
        {
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
        };

        _timer.Tick += (_, _) => Close();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void AddRow(string label, string value)
    {
        var row = Rows.RowDefinitions.Count;
        Rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock { Text = label, Foreground = (System.Windows.Media.Brush)FindResource("SubtleTextBrush"), Margin = new Thickness(0, 2, 12, 2) };
        var text = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };
        Grid.SetRow(name, row);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        Rows.Children.Add(name);
        Rows.Children.Add(text);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Close();
}
