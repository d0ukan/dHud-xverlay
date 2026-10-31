using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Sistem özeti: CPU, GPU, VRAM, RAM, disk ve ağ değerleri ince çubuklarla, overlay'den bağımsız büyük bir kartta.</summary>
public sealed class SysInfoWidgetWindow : WidgetWindowBase
{
    private const double BarWidth = 120;

    private sealed record Row(Border Fill, TextBlock Value);

    private readonly Dictionary<string, Row> _rows = [];
    private readonly TextBlock _net = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private SensorSnapshot? _latest;
    private bool _pending;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.SysInfo);

    public SysInfoWidgetWindow()
    {
        Card.Width = 300;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.SysInfo"), WidgetIds.SysInfo));

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth + 10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var key in new[] { "CPU", "GPU", "VRAM", "RAM", "DISK" })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = grid.RowDefinitions.Count - 1;

            var label = new TextBlock { Text = key, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            var track = new Border { Width = BarWidth, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border { Width = 0, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            var trackHost = new Grid { Width = BarWidth, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            trackHost.Children.Add(track);
            trackHost.Children.Add(fill);
            var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            Grid.SetRow(label, row);
            Grid.SetRow(trackHost, row);
            Grid.SetColumn(trackHost, 1);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 2);
            grid.Children.Add(label);
            grid.Children.Add(trackHost);
            grid.Children.Add(value);
            _rows[key] = new Row(fill, value);
        }

        root.Children.Add(grid);
        var netRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        var netLabel = new TextBlock { Text = "NET", FontWeight = FontWeights.Bold };
        netLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        netRow.Children.Add(netLabel);
        netRow.Children.Add(_net);
        root.Children.Add(netRow);
        Card.Child = root;

        RestorePosition(WidgetIds.SysInfo, 6, 316);

        var monitor = App.Current.Monitor;
        void OnSnapshot(SensorSnapshot s)
        {
            _latest = s;
            if (_pending)
            {
                return;
            }

            _pending = true;
            Dispatcher.BeginInvoke(() =>
            {
                _pending = false;
                if (IsVisible && _latest is { } snapshot)
                {
                    Render(snapshot);
                }
            }, DispatcherPriority.Background);
        }

        monitor.SnapshotUpdated += OnSnapshot;
        Closed += (_, _) => monitor.SnapshotUpdated -= OnSnapshot;

        if (monitor.Latest is { } first)
        {
            Render(first);
        }

        ApplyOptions();
    }

    /// <summary>Ekran görüntüsü üretimi için: verilen ölçümü çizer.</summary>
    internal void RenderDemo(SensorSnapshot s) => Render(s);

    private void Render(SensorSnapshot s)
    {
        var settings = App.Current.Settings.Current;

        Set("CPU", s.Cpu?.Load, $"{Format.Temperature(s.Cpu?.Temperature, false)}  {Format.Load(s.Cpu?.Load)}", s.Cpu?.Temperature, gpu: false, settings);
        Set("GPU", s.Gpu?.Load, $"{Format.Temperature(s.Gpu?.Temperature, false)}  {Format.Load(s.Gpu?.Load)}", s.Gpu?.Temperature, gpu: true, settings);

        var vramFraction = s.Gpu is { MemoryUsedGb: float vu, MemoryTotalGb: float vt } && vt > 0 ? vu / vt * 100 : (float?)null;
        Set("VRAM", vramFraction, Format.Memory(s.Gpu?.MemoryUsedGb, s.Gpu?.MemoryTotalGb, false), null, false, settings);
        Set("RAM", s.Memory?.Load, $"{Format.Load(s.Memory?.Load)}  {Format.Memory(s.Memory?.MemoryUsedGb, s.Memory?.MemoryTotalGb, true)}", null, false, settings);
        Set("DISK", s.Disk?.UsedPercent, s.Disk is null ? "--" : $"{s.Disk.UsedPercent:0}%  {s.Disk.UsedGb:0}/{s.Disk.TotalGb:0}G", null, false, settings);

        var bits = settings.NetworkInBits;
        _net.Text = $"↓ {Format.Speed(s.Network?.DownloadBytesPerSecond, bits, false)}    ↑ {Format.Speed(s.Network?.UploadBytesPerSecond, bits, false)}";
    }

    private void Set(string key, float? percent, string text, float? temperature, bool gpu, AppSettings settings)
    {
        var row = _rows[key];
        row.Value.Text = text;
        var fraction = Math.Clamp((percent ?? 0) / 100.0, 0, 1);
        row.Fill.Width = BarWidth * fraction;

        // Renk: sıcaklık varsa eşiklere göre, yoksa doluluğa göre.
        string brush;
        if (temperature is float t && settings.ColorByTemperature)
        {
            var (warn, hot) = settings.Thresholds(gpu);
            brush = t >= hot ? "HotBrush" : t >= warn ? "WarnBrush" : "GoodBrush";
        }
        else
        {
            brush = fraction >= 0.9 ? "HotBrush" : fraction >= 0.75 ? "WarnBrush" : "AccentBrush";
        }

        row.Fill.SetResourceReference(Border.BackgroundProperty, brush);
    }
}
