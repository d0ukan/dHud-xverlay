using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Kronometre: başlat/duraklat, tur al, sıfırla. Ekran yalnızca çalışırken saniyede 10 kez yenilenir; durunca hiçbir iş yapmaz.</summary>
public sealed class StopwatchWidgetWindow : WidgetWindowBase
{
    private const int VisibleLaps = 6;

    private int _shownLaps = -1;

    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly TextBlock _time = new() { FontSize = 34, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
    private readonly StackPanel _laps = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _startPause;
    private readonly Button _lap;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    protected override WidgetOptions Options => App.Current.Settings.Current.StopwatchOptions;

    public StopwatchWidgetWindow()
    {
        Card.Width = 240;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 14) };

        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(new TextBlock { Text = Loc.T("W.Stopwatch"), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = IconButton("✕", Loc.T("W.Hide"), (_, _) =>
        {
            App.Current.Settings.Current.WidgetStopwatch = false;
            App.Current.Settings.NotifyChanged();
        });
        Grid.SetColumn(close, 1);
        title.Children.Add(close);
        root.Children.Add(title);
        root.Children.Add(_time);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        _startPause = SmallButton("", () => { Toggle(); });
        _lap = SmallButton(Loc.T("W.Lap"), () => { _widgets.WatchLap(); Redraw(); });
        buttons.Children.Add(_startPause);
        buttons.Children.Add(_lap);
        buttons.Children.Add(SmallButton(Loc.T("W.Reset"), () => { _widgets.WatchReset(); _timer.Stop(); Redraw(); }));
        root.Children.Add(buttons);
        root.Children.Add(_laps);
        Card.Child = root;

        var pos = _widgets.Data.StopwatchPos;
        var def = DefaultSpot(1, 256);
        Place(pos?.Left ?? double.NaN, pos?.Top ?? double.NaN, def.Left, def.Top);
        Moved += p =>
        {
            _widgets.Data.StopwatchPos = new WidgetPos { Left = p.X, Top = p.Y };
            _widgets.ScheduleSave();
        };

        _timer.Tick += (_, _) => Redraw();
        Closed += (_, _) => _timer.Stop();
        if (_widgets.Watch.IsRunning)
        {
            _timer.Start();
        }

        Redraw();
        ApplyOptions();
    }

    private void Toggle()
    {
        if (_widgets.Watch.IsRunning)
        {
            _widgets.Watch.Stop();
            _timer.Stop();
        }
        else
        {
            _widgets.Watch.Start();
            _timer.Start();
        }

        Redraw();
    }

    private void Redraw()
    {
        var running = _widgets.Watch.IsRunning;
        _time.Text = Format(_widgets.Watch.Elapsed);
        _startPause.Content = running ? Loc.T("W.Pause") : Loc.T("W.Start");
        _lap.IsEnabled = running;

        // Tur listesi yalnızca tur sayısı değişince yeniden kurulur.
        var laps = _widgets.Laps;
        if (_shownLaps != laps.Count)
        {
            _shownLaps = laps.Count;
            _laps.Children.Clear();
            for (var i = laps.Count - 1; i >= 0 && _laps.Children.Count < VisibleLaps; i--)
            {
                var split = laps[i] - (i > 0 ? laps[i - 1] : TimeSpan.Zero);
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var number = new TextBlock { Text = $"#{i + 1}", MinWidth = 34 };
                number.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
                var splitText = new TextBlock { Text = "+" + Format(split), HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 0, 10, 0) };
                var total = new TextBlock { Text = Format(laps[i]), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
                Grid.SetColumn(splitText, 1);
                Grid.SetColumn(total, 2);
                row.Children.Add(number);
                row.Children.Add(splitText);
                row.Children.Add(total);
                _laps.Children.Add(row);
            }
        }
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}"
            : $"{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";

    private Button SmallButton(string text, Action onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(12, 4, 12, 4), MinWidth = 64 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "DarkButton");
        button.Click += (_, _) => onClick();
        return button;
    }
}
