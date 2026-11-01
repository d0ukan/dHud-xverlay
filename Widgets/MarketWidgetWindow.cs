using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Borsa: kripto, hisse ve döviz fiyatları, günlük değişim ve küçük gün grafiği (Yahoo Finance sembolleriyle: BTC-USD, AAPL, USDTRY=X, GC=F).
/// Yalnızca görünürken dakikada bir internetten çeker. Semboller widgets.json'da saklanır.
/// </summary>
public sealed class MarketWidgetWindow : WidgetWindowBase
{
    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly StackPanel _list = new();
    private readonly TextBox _input;
    private readonly TextBlock _status;
    private Dictionary<string, MarketQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _updated;
    private bool _failed;
    private bool _loading;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Market);

    protected override bool ZoomByWidth => true;

    public MarketWidgetWindow()
    {
        Card.Width = 300;
        _widgets.Data.Markets ??= [.. MarketService.DefaultSymbols];

        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        var refresh = IconButton("⟳", Loc.T("W.MarketRefresh"), (_, _) => _ = RefreshAsync());
        root.Children.Add(TitleBar(Loc.T("W.Market"), WidgetIds.Market, refresh));
        _list.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_list);

        _input = Field(20);
        _input.ToolTip = Loc.T("W.MarketAddHint");
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Add();
                e.Handled = true;
            }
        };
        var add = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var addButton = SmallButton(Loc.T("W.Add"), Add);
        addButton.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(addButton, 1);
        add.Children.Add(_input);
        add.Children.Add(addButton);
        root.Children.Add(add);

        _status = Subtle("");
        _status.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(_status);
        Card.Child = root;

        RestorePosition(WidgetIds.Market, 12, 316);
        Rebuild();

        _timer.Tick += (_, _) => _ = RefreshAsync();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _timer.Start();
                _ = RefreshAsync();
            }
            else
            {
                _timer.Stop();
            }
        };
        Closed += (_, _) => _timer.Stop();
        ApplyOptions();
    }

    /// <summary>Ekran görüntüsü üretimi için: internete çıkmadan örnek fiyatları çizer.</summary>
    internal void ShowDemo(IEnumerable<MarketQuote> quotes)
    {
        _quotes = quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
        _updated = DateTime.Now;
        Rebuild();
    }

    private List<string> Symbols => _widgets.Data.Markets ??= [];

    private void Add()
    {
        var symbol = MarketService.CleanSymbol(_input.Text);
        if (symbol.Length == 0)
        {
            return;
        }

        if (Symbols.Any(s => string.Equals(s, symbol, StringComparison.OrdinalIgnoreCase)))
        {
            _status.Text = Loc.T("W.MarketDuplicate");
            return;
        }

        if (Symbols.Count >= WidgetService.MaxMarkets)
        {
            _status.Text = Loc.T("W.TooMany");
            return;
        }

        Symbols.Add(symbol);
        _widgets.ScheduleSave();
        _input.Clear();
        _status.Text = string.Empty;
        Rebuild();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_loading || Symbols.Count == 0)
        {
            return;
        }

        _loading = true;
        try
        {
            var quotes = await MarketService.FetchAsync(Symbols);
            // Bir sembol bu turda alınamadıysa eski fiyatı kalır; hiçbiri alınamadıysa hata gösterilir.
            _failed = quotes.Count == 0;
            foreach (var (symbol, quote) in quotes)
            {
                _quotes[symbol] = quote;
            }

            if (quotes.Count > 0)
            {
                _updated = DateTime.Now;
            }

            Rebuild();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        if (Symbols.Count == 0)
        {
            _list.Children.Add(Subtle(Loc.T("W.MarketEmpty")));
        }

        foreach (var symbol in Symbols.ToList())
        {
            _quotes.TryGetValue(symbol, out var quote);
            _list.Children.Add(Row(symbol, quote));
        }

        _status.Text = _failed
            ? Loc.T("W.MarketError")
            : _updated is { } time ? Loc.T("W.MarketUpdated", time.ToString("t", CultureInfo.CurrentCulture)) : Loc.T("W.MarketLoading");
    }

    private FrameworkElement Row(string symbol, MarketQuote? quote)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 70 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 78 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = Display(symbol),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = quote is null ? symbol : $"{quote.Name} ({symbol}) · {quote.Currency}"
        };
        row.Children.Add(name);

        var change = quote?.ChangePercent;
        var brush = change is null ? "SubtleTextBrush" : change >= 0 ? "GoodBrush" : "HotBrush";
        if (quote is not null)
        {
            if (quote.Closes.Count >= 2)
            {
                var spark = Spark(quote.Closes, brush);
                Grid.SetColumn(spark, 1);
                row.Children.Add(spark);
            }

            var price = new TextBlock { Text = MarketService.FormatPrice(quote.Price), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var delta = new TextBlock
            {
                Text = change is double c ? $"{(c >= 0 ? "▲" : "▼")} {Math.Abs(c):0.00}%" : "--",
                FontSize = 10.5,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            delta.SetResourceReference(TextBlock.ForegroundProperty, brush);
            var pair = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            pair.Children.Add(price);
            pair.Children.Add(delta);
            Grid.SetColumn(pair, 2);
            row.Children.Add(pair);
        }
        else
        {
            var wait = Subtle(_failed ? "--" : "…");
            wait.HorizontalAlignment = HorizontalAlignment.Right;
            wait.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(wait, 2);
            row.Children.Add(wait);
        }

        var remove = IconButton("✕", Loc.T("W.Cancel"), (_, _) =>
        {
            Symbols.RemoveAll(s => string.Equals(s, symbol, StringComparison.OrdinalIgnoreCase));
            _quotes.Remove(symbol);
            _widgets.ScheduleSave();
            Rebuild();
        });
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);
        return row;
    }

    /// <summary>Okunur ad: "BTC-USD" → "BTC/USD", "USDTRY=X" → "USD/TRY", diğerleri olduğu gibi.</summary>
    internal static string Display(string symbol)
    {
        if (symbol.EndsWith("=X", StringComparison.OrdinalIgnoreCase) && symbol.Length == 8)
        {
            return $"{symbol[..3]}/{symbol[3..6]}";
        }

        return symbol.Contains('-') && !symbol.StartsWith('^') ? symbol.Replace('-', '/') : symbol;
    }

    private static FrameworkElement Spark(IReadOnlyList<double> values, string brushKey)
    {
        const double Width = 56, Height = 16;
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        var points = new PointCollection(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var x = Width * i / (values.Count - 1);
            var y = range <= 0 ? Height / 2 : Height - (values[i] - min) / range * Height;
            points.Add(new Point(x, y));
        }

        var line = new Polyline { Points = points, StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Round, Width = Width, Height = Height, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        line.SetResourceReference(Shape.StrokeProperty, brushKey);
        return line;
    }
}
