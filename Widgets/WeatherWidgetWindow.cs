using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Hava durumu kartı: şimdiki sıcaklık, hissedilen, rüzgâr ve 3 günlük tahmin. Şehir seçilene kadar internete çıkmaz;
/// seçildikten sonra 30 dakikada bir (ve ⟳ ile) güncellenir.
/// </summary>
public sealed class WeatherWidgetWindow : WidgetWindowBase
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(5);

    // Aynı şehir için son veri uygulama boyunca bellekte kalır: pencere kapanıp açılınca hemen görünür, sunucuya gereksiz istek gitmez.
    private static WeatherData? _cache;
    private static (double Lat, double Lon)? _cacheKey;

    private readonly SettingsService _settings = App.Current.Settings;
    private readonly DispatcherTimer _timer = new() { Interval = RefreshEvery };
    private readonly StackPanel _body = new();
    private readonly TextBox _query = new();
    private readonly StackPanel _results = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private (double Lat, double Lon)? _shownFor;
    private CancellationTokenSource? _cts;

    protected override WidgetOptions Options => _settings.Current.WeatherOptions;

    public WeatherWidgetWindow()
    {
        Card.Width = 260;
        Card.Child = _body;
        _body.Margin = new Thickness(16, 10, 16, 12);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _status.FontSize = 11;

        var pos = App.Current.Widgets.Data.WeatherPos;
        var def = DefaultSpot(3, 276);
        Place(pos?.Left ?? double.NaN, pos?.Top ?? double.NaN, def.Left, def.Top);
        Moved += p =>
        {
            App.Current.Widgets.Data.WeatherPos = new WidgetPos { Left = p.X, Top = p.Y };
            App.Current.Widgets.ScheduleSave();
        };

        ContextMenu = BuildMenu();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _cts?.Cancel();
        };

        Reload();
        ApplyOptions();
    }

    /// <summary>Ayar değişti (şehir, birim): ekranı yeniden kurar; şehir değiştiyse yeni veriyi ister.</summary>
    public void Reload()
    {
        var s = _settings.Current;
        if (s.WeatherLat is not double lat || s.WeatherLon is not double lon)
        {
            _timer.Stop();
            _shownFor = null;
            ShowSetup();
            return;
        }

        var key = (lat, lon);
        if (_cache is not null && _cacheKey == key)
        {
            ShowWeather(_cache);
        }
        else
        {
            ShowLoading();
        }

        if (_shownFor != key || _cacheKey != key)
        {
            _shownFor = key;
            _ = RefreshAsync();
        }
    }

    // ---- Veri ----

    private async Task RefreshAsync()
    {
        var s = _settings.Current;
        if (s.WeatherLat is not double lat || s.WeatherLon is not double lon)
        {
            return;
        }

        _timer.Stop();
        _cts?.Cancel(); // önceki (örn. eski şehir için) istek varsa iptal
        var cts = _cts = new CancellationTokenSource();
        try
        {
            var data = await WeatherService.FetchAsync(lat, lon, cts.Token);
            if (cts.IsCancellationRequested || _settings.Current.WeatherLat != lat || _settings.Current.WeatherLon != lon)
            {
                return; // bu arada şehir değişti
            }

            _cache = data;
            _cacheKey = (lat, lon);
            _timer.Interval = RefreshEvery;
            ShowWeather(data);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception) when (!cts.IsCancellationRequested)
        {
            // İnternet yok / servis yanıt vermiyor: eski veri kalır, kısa süre sonra yeniden denenir.
            _timer.Interval = RetryAfterError;
            if (_cache is not null && _cacheKey == (lat, lon))
            {
                ShowWeather(_cache, error: true);
            }
            else
            {
                ShowMessage(Loc.T("W.WeatherUnavailable"));
            }
        }
        finally
        {
            if (!cts.IsCancellationRequested && _settings.Current.WeatherLat is not null)
            {
                _timer.Start();
            }
        }
    }

    // ---- Görünüm ----

    private void ShowSetup()
    {
        _body.Children.Clear();
        _body.Children.Add(Title(Loc.T("W.Weather"), refresh: false));

        var hint = new TextBlock { Text = Loc.T("W.WeatherAsk"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _body.Children.Add(hint);

        _query.Padding = new Thickness(6, 3, 6, 3);
        _query.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 255, 255, 255));
        _query.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 255, 255, 255));
        _query.BorderThickness = new Thickness(1);
        _query.CaretBrush = System.Windows.Media.Brushes.White;
        _query.MaxLength = 60;
        _query.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _query.KeyDown -= OnQueryKey;
        _query.KeyDown += OnQueryKey;
        _body.Children.Add(_query);

        var search = new Button { Content = Loc.T("W.Search"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(12, 3, 12, 3) };
        search.SetResourceReference(StyleProperty, "DarkButton");
        search.Click += async (_, _) => await SearchAsync();
        _body.Children.Add(search);

        _results.Children.Clear();
        _body.Children.Add(_results);
        _status.Text = string.Empty;
        _body.Children.Add(_status);
    }

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = SearchAsync();
            e.Handled = true;
        }
    }

    private async Task SearchAsync()
    {
        var text = _query.Text.Trim();
        if (text.Length < 2)
        {
            return;
        }

        _results.Children.Clear();
        _status.Text = Loc.T("W.Searching");
        try
        {
            var language = Loc.Current == AppLanguage.Turkish ? "tr" : "en";
            var places = await WeatherService.SearchAsync(text, language);
            _status.Text = places.Count == 0 ? Loc.T("W.NoResults") : string.Empty;
            foreach (var place in places)
            {
                var button = new Button
                {
                    Content = place.Display,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 0),
                    Padding = new Thickness(8, 4, 8, 4)
                };
                button.SetResourceReference(StyleProperty, "DarkButton");
                var chosen = place;
                button.Click += (_, _) => Choose(chosen);
                _results.Children.Add(button);
            }
        }
        catch (Exception)
        {
            _status.Text = Loc.T("W.WeatherUnavailable");
        }
    }

    private void Choose(GeoPlace place)
    {
        var s = _settings.Current;
        s.WeatherPlace = place.Display;
        s.WeatherLat = place.Latitude;
        s.WeatherLon = place.Longitude;
        _settings.NotifyChanged(); // WidgetManager.Apply -> Reload
    }

    private void ShowLoading()
    {
        _body.Children.Clear();
        _body.Children.Add(Title(_settings.Current.WeatherPlace, refresh: true));
        var text = new TextBlock { Text = Loc.T("W.Loading"), Margin = new Thickness(0, 8, 0, 4) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _body.Children.Add(text);
    }

    private void ShowMessage(string message)
    {
        _body.Children.Clear();
        _body.Children.Add(Title(_settings.Current.WeatherPlace, refresh: true));
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        _body.Children.Add(text);
    }

    /// <summary>Ekran görüntüsü üretimi için: ağa çıkmadan verilen hava durumunu gösterir.</summary>
    internal void ShowDemo(WeatherData data) => ShowWeather(data);

    private void ShowWeather(WeatherData data, bool error = false)
    {
        var f = _settings.Current.WeatherFahrenheit;
        string Temp(double c) => $"{Math.Round(f ? WeatherService.ToFahrenheit(c) : c):0}°";
        var culture = CultureInfo.CurrentCulture;

        _body.Children.Clear();
        _body.Children.Add(Title(_settings.Current.WeatherPlace, refresh: true));

        // Şimdiki hava: simge + sıcaklık + açıklama
        var now = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        now.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        now.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new TextBlock
        {
            Text = WeatherService.Icon(data.Now.Code, data.Now.IsDay),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji, Segoe UI Symbol"),
            FontSize = 38,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var temp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        temp.Children.Add(new TextBlock { Text = Temp(data.Now.Temperature), FontSize = 32, FontWeight = FontWeights.SemiBold });
        temp.Children.Add(new TextBlock { Text = Loc.T("W.Wx." + WeatherService.Kind(data.Now.Code)) });
        Grid.SetColumn(temp, 1);
        now.Children.Add(icon);
        now.Children.Add(temp);
        _body.Children.Add(now);

        var wind = f ? $"{data.Now.WindKmh * 0.621371:0} mph" : $"{data.Now.WindKmh:0} km/h";
        var details = new TextBlock { Text = Loc.T("W.Feels", Temp(data.Now.Feels)) + "  ·  " + Loc.T("W.Wind", wind), Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
        details.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        _body.Children.Add(details);

        // 3 günlük tahmin
        if (data.Days.Count > 0)
        {
            var row = new UniformGrid { Rows = 1, Columns = data.Days.Count, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var day in data.Days)
            {
                var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var name = new TextBlock { Text = day.Date.ToString("ddd", culture), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
                name.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
                cell.Children.Add(name);
                cell.Children.Add(new TextBlock
                {
                    Text = WeatherService.Icon(day.Code, true),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji, Segoe UI Symbol"),
                    FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                cell.Children.Add(new TextBlock { Text = $"{Temp(day.Max)} / {Temp(day.Min)}", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
                row.Children.Add(cell);
            }

            _body.Children.Add(row);
        }

        _status.Text = error
            ? Loc.T("W.WeatherStale", data.FetchedUtc.ToLocalTime().ToString("t", culture))
            : Loc.T("W.WeatherUpdated", data.FetchedUtc.ToLocalTime().ToString("t", culture));
        _status.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(_status);
    }

    private Grid Title(string text, bool refresh)
    {
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (refresh)
        {
            var reload = IconButton("⟳", Loc.T("W.Refresh"), (_, _) =>
            {
                _timer.Stop();
                _ = RefreshAsync();
            });
            Grid.SetColumn(reload, 1);
            title.Children.Add(reload);
        }

        var close = IconButton("✕", Loc.T("W.Hide"), (_, _) =>
        {
            _settings.Current.WidgetWeather = false;
            _settings.NotifyChanged();
        });
        Grid.SetColumn(close, 2);
        title.Children.Add(close);
        return title;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var change = new MenuItem { Header = Loc.T("W.ChangeCity") };
        change.Click += (_, _) =>
        {
            _settings.Current.WeatherLat = _settings.Current.WeatherLon = null;
            _settings.Current.WeatherPlace = string.Empty;
            _settings.NotifyChanged();
        };
        var units = new MenuItem { Header = Loc.T("W.Fahrenheit"), IsCheckable = true, IsChecked = _settings.Current.WeatherFahrenheit };
        units.Click += (_, _) =>
        {
            _settings.Current.WeatherFahrenheit = units.IsChecked;
            _settings.NotifyChanged();
        };
        menu.Items.Add(change);
        menu.Items.Add(units);
        return menu;
    }
}
