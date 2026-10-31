using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Saat ve tarih kartı; istenirse ikinci bir saat diliminin saatini de gösterir.</summary>
public sealed class ClockWidgetWindow : WidgetWindowBase
{
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

    private readonly SettingsService _settings = App.Current.Settings;
    private readonly TextBlock _time = new() { FontSize = 36, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _date = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
    private readonly TextBlock _zone = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public ClockWidgetWindow()
    {
        _date.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _zone.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        var stack = new StackPanel { Margin = new Thickness(22, 12, 22, 12) };
        stack.Children.Add(_time);
        stack.Children.Add(_date);
        stack.Children.Add(_zone);
        Card.Child = stack;

        var place = App.Current.Widgets.Data.ClockPos;
        var def = DefaultSpot(0, 220);
        Place(place?.Left ?? double.NaN, place?.Top ?? double.NaN, def.Left, def.Top);
        Moved += p =>
        {
            App.Current.Widgets.Data.ClockPos = new WidgetPos { Left = p.X, Top = p.Y };
            App.Current.Widgets.ScheduleSave();
        };

        ContextMenu = BuildMenu();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Refresh();
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        _time.Text = now.ToString(_settings.Current.ClockSeconds ? "T" : "t", culture);
        _date.Text = now.ToString("dddd, d MMMM", culture);

        var zone = ResolveZone(_settings.Current.ClockZone);
        if (zone is null)
        {
            _zone.Visibility = Visibility.Collapsed;
            return;
        }

        var there = TimeZoneInfo.ConvertTime(now, zone);
        var label = Zones.FirstOrDefault(z => z.Id == zone.Id).Label ?? zone.StandardName;
        _zone.Text = $"{label}  {there.ToString(_settings.Current.ClockSeconds ? "T" : "t", culture)}";
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

        var seconds = new MenuItem { Header = Loc.T("W.Seconds"), IsCheckable = true, IsChecked = _settings.Current.ClockSeconds };
        seconds.Click += (_, _) =>
        {
            _settings.Current.ClockSeconds = seconds.IsChecked;
            _settings.NotifyChanged();
            Refresh();
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
