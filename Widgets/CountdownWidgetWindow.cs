using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Gün sayacı: "Tatile 12 gün kaldı" gibi tarihler. Gece yarısı kendiliğinden yenilenir.</summary>
public sealed class CountdownWidgetWindow : WidgetWindowBase
{
    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly StackPanel _list = new();
    private readonly TextBox _name;
    private readonly TextBox _date;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _midnight = new();

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Countdown);

    public CountdownWidgetWindow()
    {
        Card.Width = 290;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.Countdown"), WidgetIds.Countdown));
        _list.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_list);

        _name = Field(40);
        _name.ToolTip = Loc.T("W.CdName");
        _date = Field(12);
        _date.Width = 96;
        _date.ToolTip = Loc.T("W.CdDate", DateTime.Today.AddDays(30).ToString("d", CultureInfo.CurrentCulture));
        _name.KeyDown += (_, e) => EnterTo(e);
        _date.KeyDown += (_, e) => EnterTo(e);
        var add = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _date.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(_date, 1);
        var addButton = SmallButton(Loc.T("W.Add"), Add);
        addButton.Margin = new Thickness(0);
        Grid.SetColumn(addButton, 2);
        add.Children.Add(_name);
        add.Children.Add(_date);
        add.Children.Add(addButton);
        root.Children.Add(add);
        _status = Subtle("");
        _status.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(_status);
        Card.Child = root;

        RestorePosition(WidgetIds.Countdown, 9, 306);
        _midnight.Tick += (_, _) => { Rebuild(); ScheduleMidnight(); };
        Closed += (_, _) => _midnight.Stop();
        Rebuild();
        ScheduleMidnight();
        ApplyOptions();
    }

    private void ScheduleMidnight()
    {
        _midnight.Stop();
        _midnight.Interval = DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(2);
        _midnight.Start();
    }

    private void EnterTo(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Add();
            e.Handled = true;
        }
    }

    private void Add()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || !TryParseDate(_date.Text, out var date))
        {
            _status.Text = Loc.T("W.CdBad");
            return;
        }

        if (_widgets.Data.Countdowns.Count >= WidgetService.MaxCountdowns)
        {
            _status.Text = Loc.T("W.TooMany");
            return;
        }

        _widgets.Data.Countdowns.Add(new Countdown { Name = name, Date = date });
        _widgets.ScheduleSave();
        _name.Clear();
        _date.Clear();
        _status.Text = string.Empty;
        Rebuild();
    }

    /// <summary>Tarih: sistem biçimi (ör. 25.12.2026) veya 2026-12-25.</summary>
    public static bool TryParseDate(string text, out DateTime date)
    {
        text = text.Trim();
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date)
               || DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Bugünden verilen güne gün farkı: pozitif = kaldı, 0 = bugün, negatif = geçti.</summary>
    public static int DaysUntil(DateTime date, DateTime today) => (int)(date.Date - today.Date).TotalDays;

    private void Rebuild()
    {
        _list.Children.Clear();
        var today = DateTime.Today;
        var items = _widgets.Data.Countdowns.OrderBy(c => Math.Abs(DaysUntil(c.Date, today))).ThenBy(c => c.Date).ToList();
        if (items.Count == 0)
        {
            _list.Children.Add(Subtle(Loc.T("W.CdEmpty")));
            return;
        }

        foreach (var item in items)
        {
            var days = DaysUntil(item.Date, today);
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = item.Date.ToString("D", CultureInfo.CurrentCulture) };
            var left = new TextBlock
            {
                Text = days == 0 ? Loc.T("W.CdToday") : days > 0 ? Loc.T("W.CdLeft", days) : Loc.T("W.CdAgo", -days),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            left.SetResourceReference(TextBlock.ForegroundProperty, days == 0 ? "GoodBrush" : days < 0 ? "SubtleTextBrush" : days <= 7 ? "WarnBrush" : "TextBrush");

            var current = item;
            var remove = IconButton("✕", Loc.T("W.Cancel"), (_, _) =>
            {
                _widgets.Data.Countdowns.Remove(current);
                _widgets.ScheduleSave();
                Rebuild();
            });
            Grid.SetColumn(left, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(name);
            row.Children.Add(left);
            row.Children.Add(remove);
            _list.Children.Add(row);
        }
    }
}
