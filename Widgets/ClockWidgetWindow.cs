using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Saat widget'ının görünümleri; sıra oklarla gezilen sıradır.</summary>
public static class ClockStyles
{
    public const string Default = "digital";

    public static readonly IReadOnlyList<string> All = ["digital", "minimal", "stacked", "glow", "analog", "analogMinimal"];

    /// <summary>Bilinmeyen/bozuk değer varsayılana döner.</summary>
    public static string Normalize(string? id) => All.Contains(id ?? string.Empty) ? id! : Default;

    /// <summary><paramref name="step"/> kadar ileri/geri (başa/sona sararak) komşu stil.</summary>
    public static string Step(string? id, int step)
    {
        var i = All.ToList().IndexOf(Normalize(id));
        return All[((i + step) % All.Count + All.Count) % All.Count];
    }

    public static bool IsAnalog(string id) => id is "analog" or "analogMinimal";

    /// <summary>Akrep (saat) kolunun derecesi: dakika ilerledikçe kayar.</summary>
    public static double HourAngle(DateTime t) => t.Hour % 12 * 30 + t.Minute * 0.5;

    public static double MinuteAngle(DateTime t) => t.Minute * 6 + t.Second * 0.1;

    public static double SecondAngle(DateTime t) => t.Second * 6;
}

/// <summary>Saat ve tarih kartı; birkaç farklı görünümü (dijital, analog…) vardır ve istenirse ikinci bir saat diliminin saatini de gösterir.</summary>
public sealed class ClockWidgetWindow : WidgetWindowBase
{
    protected override WidgetOptions Options => App.Current.Settings.Current.ClockOptions;

    private static readonly (string Label, string Id)[] Zones =
    [
        ("UTC", "UTC"),
        ("London", "GMT Standard Time"),
        ("Berlin", "W. Europe Standard Time"),
        ("İstanbul", "Turkey Standard Time"),
        ("New York", "Eastern Standard Time"),
        ("Los Angeles", "Pacific Standard Time"),
        ("Tokyo", "Tokyo Standard Time"),
        ("Sydney", "AUS Eastern Standard Time")
    ];

    private const double Face = 120;   // analog kadran boyutu

    private readonly SettingsService _settings = App.Current.Settings;
    private readonly TextBlock _time = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _hours = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _minutes = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _date = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _zone = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Border _faceHost = new();
    private Button? _prev, _next;
    private readonly DispatcherTimer _timer = new();

    private string _style = ClockStyles.Default;
    private RotateTransform? _hourHand, _minuteHand, _secondHand;
    private readonly DropShadowEffect _glow = new() { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9, Color = Colors.White };

    public ClockWidgetWindow()
    {
        _date.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _zone.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        // Oklar saatin sağında ve solunda, kartın iç boşluğunda durur (kart büyümez); yalnızca fare kartın üstündeyken görünür.
        _prev = IconButton("‹", Loc.T("W.ClockPrev"), (_, _) => SetStyle(ClockStyles.Step(_style, -1)));
        _next = IconButton("›", Loc.T("W.ClockNext"), (_, _) => SetStyle(ClockStyles.Step(_style, +1)));
        foreach (var arrow in new[] { _prev, _next })
        {
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Opacity = 0;
        }

        _prev.HorizontalAlignment = HorizontalAlignment.Left;
        _prev.Margin = new Thickness(-20, 0, 0, 0);
        _next.HorizontalAlignment = HorizontalAlignment.Right;
        _next.Margin = new Thickness(0, 0, -20, 0);

        var faceRow = new Grid();
        faceRow.Children.Add(_faceHost);
        faceRow.Children.Add(_prev);
        faceRow.Children.Add(_next);

        var stack = new StackPanel { Margin = new Thickness(22, 12, 22, 12), MinWidth = 140 };
        stack.Children.Add(faceRow);
        stack.Children.Add(_date);
        stack.Children.Add(_zone);
        Card.Child = stack;

        MouseEnter += (_, _) => _prev.Opacity = _next.Opacity = 1;
        MouseLeave += (_, _) => _prev.Opacity = _next.Opacity = 0;

        var place = App.Current.Widgets.Data.ClockPos;
        var def = DefaultSpot(0, 220);
        Place(place?.Left ?? double.NaN, place?.Top ?? double.NaN, def.Left, def.Top);
        Moved += p =>
        {
            App.Current.Widgets.Data.ClockPos = new WidgetPos { Left = p.X, Top = p.Y };
            App.Current.Widgets.ScheduleSave();
        };

        ContextMenu = BuildMenu();
        // Zamanlayıcı bir sonraki saniyede (saniye gösteriliyorsa) ya da dakikada bir uyanır; arada hiçbir iş yapılmaz.
        _timer.Tick += (_, _) => { _timer.Stop(); Refresh(); ScheduleNext(); };
        Closed += (_, _) => _timer.Stop();
        BuildFace();
        Refresh();
        ScheduleNext();
        ApplyOptions();
    }

    private void SetStyle(string id)
    {
        id = ClockStyles.Normalize(id);
        if (id == _style)
        {
            return;
        }

        _settings.Current.ClockStyle = id;
        _settings.NotifyChanged();
        ContextMenu = BuildMenu();   // işaretler güncellensin
        BuildFace();
        Refresh();
        _timer.Stop();
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        var now = DateTime.Now;
        var next = _settings.Current.ClockSeconds
            ? now.AddSeconds(1).AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond))
            : now.AddMinutes(1).AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
        _timer.Interval = next - now + TimeSpan.FromMilliseconds(30);
        _timer.Start();
    }

    // ---- Görünümler ----

    private void BuildFace()
    {
        _style = ClockStyles.Normalize(_settings.Current.ClockStyle);
        var name = Loc.T("Clock." + _style);
        if (_prev is not null && _next is not null)
        {
            _prev.ToolTip = Loc.T("W.ClockPrev");
            _next.ToolTip = $"{Loc.T("W.ClockNext")} ({name})";
        }
        _hourHand = _minuteHand = _secondHand = null;
        _time.Effect = null;
        _time.ClearValue(TextBlock.ForegroundProperty);
        _minutes.ClearValue(TextBlock.ForegroundProperty);
        _date.Margin = new Thickness(0);

        switch (_style)
        {
            case "minimal":
                Style(_time, 44, FontWeights.Thin);
                _faceHost.Child = _time;
                break;
            case "stacked":
                Style(_hours, 46, FontWeights.Bold);
                Style(_minutes, 46, FontWeights.Bold);
                _minutes.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                var pair = new StackPanel();
                pair.Children.Add(_hours);
                pair.Children.Add(_minutes);
                _faceHost.Child = pair;
                break;
            case "glow":
                Style(_time, 36, FontWeights.Bold);
                _time.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                _time.Effect = _glow;
                _faceHost.Child = _time;
                break;
            case "analog":
            case "analogMinimal":
                _faceHost.Child = BuildAnalog(_style == "analog");
                _date.Margin = new Thickness(0, 6, 0, 0);
                break;
            default:
                Style(_time, 36, FontWeights.SemiBold);
                _faceHost.Child = _time;
                break;
        }
    }

    private static void Style(TextBlock block, double size, FontWeight weight)
    {
        block.FontSize = size;
        block.FontWeight = weight;
        block.LineHeight = size * 1.05;
        block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
    }

    /// <summary>Analog kadran: <paramref name="classic"/> çizgili/halkalı, değilse yalnızca dört nokta ve ince kollar.</summary>
    private FrameworkElement BuildAnalog(bool classic)
    {
        var canvas = new Canvas { Width = Face, Height = Face, HorizontalAlignment = HorizontalAlignment.Center };
        var c = Face / 2;

        if (classic)
        {
            var ring = new Ellipse { Width = Face - 4, Height = Face - 4, StrokeThickness = 2 };
            ring.SetResourceReference(Shape.StrokeProperty, "SubtleTextBrush");
            Canvas.SetLeft(ring, 2);
            Canvas.SetTop(ring, 2);
            canvas.Children.Add(ring);

            for (var i = 0; i < 12; i++)
            {
                var major = i % 3 == 0;
                var tick = new Line { X1 = c, X2 = c, Y1 = 6, Y2 = major ? 16 : 11, StrokeThickness = major ? 2.5 : 1.2, RenderTransform = new RotateTransform(i * 30, c, c) };
                tick.SetResourceReference(Shape.StrokeProperty, major ? "TextBrush" : "SubtleTextBrush");
                canvas.Children.Add(tick);
            }
        }
        else
        {
            for (var i = 0; i < 4; i++)
            {
                var dot = new Ellipse { Width = 4, Height = 4 };
                dot.SetResourceReference(Shape.FillProperty, "SubtleTextBrush");
                Canvas.SetLeft(dot, c - 2);
                Canvas.SetTop(dot, 4);
                dot.RenderTransform = new RotateTransform(i * 90, 2, c - 4);
                canvas.Children.Add(dot);
            }
        }

        _hourHand = AddHand(canvas, c, classic ? 30 : 34, classic ? 4 : 2.5, "TextBrush");
        _minuteHand = AddHand(canvas, c, classic ? 44 : 46, classic ? 2.5 : 1.8, classic ? "TextBrush" : "AccentBrush");
        if (_settings.Current.ClockSeconds)
        {
            _secondHand = AddHand(canvas, c, classic ? 48 : 50, 1, classic ? "AccentBrush" : "SubtleTextBrush");
        }

        var hub = new Ellipse { Width = 8, Height = 8 };
        hub.SetResourceReference(Shape.FillProperty, "AccentBrush");
        Canvas.SetLeft(hub, c - 4);
        Canvas.SetTop(hub, c - 4);
        canvas.Children.Add(hub);
        return canvas;
    }

    private static RotateTransform AddHand(Canvas canvas, double c, double length, double thickness, string brushKey)
    {
        var rotate = new RotateTransform(0, c, c);
        var hand = new Line { X1 = c, Y1 = c + length * 0.12, X2 = c, Y2 = c - length, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = rotate };
        hand.SetResourceReference(Shape.StrokeProperty, brushKey);
        canvas.Children.Add(hand);
        return rotate;
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var seconds = _settings.Current.ClockSeconds;

        switch (_style)
        {
            case "stacked":
                _hours.Text = now.ToString(culture.DateTimeFormat.ShortTimePattern.Contains('H') ? "HH" : "hh", culture);
                _minutes.Text = now.ToString("mm", culture);
                break;
            case "analog":
            case "analogMinimal":
                if (_hourHand is null || (_secondHand is null) == seconds)
                {
                    BuildFace();   // saniye ayarı Ayarlar'dan değişti: kol eklenir/kalkar
                }

                if (_hourHand is not null && _minuteHand is not null)
                {
                    _hourHand.Angle = ClockStyles.HourAngle(now);
                    _minuteHand.Angle = ClockStyles.MinuteAngle(now);
                    if (_secondHand is not null)
                    {
                        _secondHand.Angle = ClockStyles.SecondAngle(now);
                    }
                }

                break;
            default:
                _time.Text = now.ToString(seconds ? "T" : "t", culture);
                if (_style == "glow" && TryFindResource("AccentColor") is Color accent)
                {
                    _glow.Color = accent;
                }

                break;
        }

        _date.Text = now.ToString("dddd, d MMMM", culture);

        var zone = ResolveZone(_settings.Current.ClockZone);
        if (zone is null)
        {
            _zone.Visibility = Visibility.Collapsed;
            return;
        }

        var there = TimeZoneInfo.ConvertTime(now, zone);
        var label = Zones.FirstOrDefault(z => z.Id == zone.Id).Label ?? zone.StandardName;
        _zone.Text = $"{label}  {there.ToString(seconds ? "T" : "t", culture)}";
        _zone.Visibility = Visibility.Visible;
    }

    private static TimeZoneInfo? ResolveZone(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var styleMenu = new MenuItem { Header = Loc.T("W.ClockStyle") };
        foreach (var id in ClockStyles.All)
        {
            var item = new MenuItem { Header = Loc.T("Clock." + id), IsCheckable = true, IsChecked = ClockStyles.Normalize(_settings.Current.ClockStyle) == id };
            var styleId = id;
            item.Click += (_, _) => SetStyle(styleId);
            styleMenu.Items.Add(item);
        }

        menu.Items.Add(styleMenu);

        var seconds = new MenuItem { Header = Loc.T("W.Seconds"), IsCheckable = true, IsChecked = _settings.Current.ClockSeconds };
        seconds.Click += (_, _) =>
        {
            _settings.Current.ClockSeconds = seconds.IsChecked;
            _settings.NotifyChanged();
            BuildFace();   // analogda saniye kolu eklenir/kalkar
            Refresh();
            _timer.Stop();
            ScheduleNext();
        };
        menu.Items.Add(seconds);

        var zoneMenu = new MenuItem { Header = Loc.T("W.SecondZone") };
        var none = new MenuItem { Header = Loc.T("W.None"), IsCheckable = true, IsChecked = string.IsNullOrEmpty(_settings.Current.ClockZone) };
        none.Click += (_, _) => SetZone(string.Empty);
        zoneMenu.Items.Add(none);
        foreach (var (label, id) in Zones)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _settings.Current.ClockZone == id };
            var zoneId = id;
            item.Click += (_, _) => SetZone(zoneId);
            zoneMenu.Items.Add(item);
        }

        menu.Items.Add(zoneMenu);
        menu.Items.Add(new Separator());

        var close = new MenuItem { Header = Loc.T("W.Hide") };
        close.Click += (_, _) =>
        {
            _settings.Current.WidgetClock = false;
            _settings.NotifyChanged();
        };
        menu.Items.Add(close);
        return menu;
    }

    private void SetZone(string id)
    {
        _settings.Current.ClockZone = id;
        _settings.NotifyChanged();
        ContextMenu = BuildMenu(); // işaretler güncellensin
        Refresh();
    }
}
