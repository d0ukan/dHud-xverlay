using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Takvim: bir .ics dosyasındaki (veya bir .ics adresindeki) önümüzdeki 7 günün etkinlikleri. Hesaba bağlanmaz;
/// kaynak Ayarlar → Widget'lar → Takvim bölümünde verilir. Dosya her 15 dakikada ve ⟳ ile yeniden okunur.
/// </summary>
public sealed class CalendarWidgetWindow : WidgetWindowBase
{
    private const int Days = 7;
    private const int MaxShown = 9;
    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly StackPanel _list = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private string _loadedSource = string.Empty;
    private CancellationTokenSource? _cts;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Calendar);

    public CalendarWidgetWindow()
    {
        Card.Width = 300;
        var refresh = IconButton("⟳", Loc.T("W.Refresh"), (_, _) => _ = LoadAsync());
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.Calendar"), WidgetIds.Calendar, refresh));
        _list.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_list);
        Card.Child = root;

        RestorePosition(WidgetIds.Calendar, 8, 316);
        _timer.Tick += async (_, _) => await LoadAsync();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _cts?.Cancel();
        };

        _ = LoadAsync();
        _timer.Start();
        ApplyOptions();
    }

    /// <summary>Ayar değişince (kaynak) yeniden yükler.</summary>
    public void Reload()
    {
        if (App.Current.Settings.Current.CalendarSource != _loadedSource)
        {
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var source = App.Current.Settings.Current.CalendarSource;
        _loadedSource = source;
        if (string.IsNullOrWhiteSpace(source))
        {
            ShowMessage(Loc.T("W.CalNoSource"));
            return;
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            var text = await ReadAsync(source, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            // Ayrıştırma ağır olabileceği için arka planda yapılır.
            var from = DateTime.Today;
            var occurrences = await Task.Run(() => IcsParser.Occurrences(text, from, from.AddDays(Days)), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Show(occurrences);
            }
        }
        catch (OperationCanceledException)
        {
            // Yeni bir yükleme başladı.
        }
        catch (Exception)
        {
            if (!cts.IsCancellationRequested)
            {
                ShowMessage(Loc.T("W.CalError"));
            }
        }
    }

    private static async Task<string> ReadAsync(string source, CancellationToken ct)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var response = await Http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                throw new InvalidDataException("Takvim çok büyük.");
            }

            return await response.Content.ReadAsStringAsync(ct);
        }

        // webcal:// adresleri https olarak okunur.
        if (source.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadAsync("https://" + source["webcal://".Length..], ct);
        }

        if (new FileInfo(source).Length > MaxBytes)
        {
            throw new InvalidDataException("Takvim çok büyük.");
        }

        return await File.ReadAllTextAsync(source, ct);
    }

    private void ShowMessage(string text)
    {
        _list.Children.Clear();
        _list.Children.Add(Subtle(text));
    }

    private void Show(IReadOnlyList<CalendarOccurrence> occurrences)
    {
        _list.Children.Clear();
        if (occurrences.Count == 0)
        {
            _list.Children.Add(Subtle(Loc.T("W.CalEmpty", Days)));
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        DateTime? lastDay = null;
        foreach (var occ in occurrences.Take(MaxShown))
        {
            var day = occ.Start.Date < DateTime.Today ? DateTime.Today : occ.Start.Date; // dünden süren çok günlü etkinlik bugün gösterilir
            if (day != lastDay)
            {
                lastDay = day;
                var header = new TextBlock
                {
                    Text = day == DateTime.Today ? Loc.T("W.Today") : day == DateTime.Today.AddDays(1) ? Loc.T("W.Tomorrow") : day.ToString("dddd, d MMMM", culture),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, _list.Children.Count == 0 ? 0 : 8, 0, 3)
                };
                _list.Children.Add(header);
            }

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var time = Subtle(occ.AllDay ? Loc.T("W.AllDay") : occ.Start.ToString("t", culture));
            var title = new TextBlock { Text = string.IsNullOrWhiteSpace(occ.Title) ? Loc.T("W.NoTitle") : occ.Title, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = occ.Title };
            Grid.SetColumn(title, 1);
            row.Children.Add(time);
            row.Children.Add(title);
            _list.Children.Add(row);
        }

        if (occurrences.Count > MaxShown)
        {
            var more = Subtle(Loc.T("W.CalMore", occurrences.Count - MaxShown));
            more.Margin = new Thickness(0, 6, 0, 0);
            _list.Children.Add(more);
        }
    }
}
