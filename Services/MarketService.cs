using System.Net.Http;
using System.Text.Json;

namespace DHud.Services;

/// <summary>Bir sembolün anlık fiyatı. <see cref="Closes"/> günün küçük grafiği içindir.</summary>
public sealed record MarketQuote(string Symbol, string Name, double Price, double? PreviousClose, string Currency, IReadOnlyList<double> Closes)
{
    /// <summary>Önceki kapanışa göre günlük değişim yüzdesi.</summary>
    public double? ChangePercent => PreviousClose is double prev and > 0 ? (Price - prev) / prev * 100 : null;
}

/// <summary>
/// Kripto, borsa ve döviz fiyatları (Yahoo Finance herkese açık grafik uç noktası; hesap/anahtar yok).
/// Semboller: BTC-USD, AAPL, THYAO.IS, USDTRY=X, GC=F ... Yalnızca widget açıkken ve görünürken çağrılır.
/// </summary>
public static class MarketService
{
    public static readonly string[] DefaultSymbols = ["BTC-USD", "ETH-USD", "USDTRY=X", "EURTRY=X", "GC=F"];

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) dHud");
        return client;
    }

    /// <summary>Kullanıcının yazdığı sembolü temizler: büyük harf, boşluksuz, yalnızca harf/rakam ve - . = ^ _ karakterleri, en çok 20 karakter.</summary>
    public static string CleanSymbol(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var clean = new string(text.Trim().ToUpperInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '=' or '^' or '_').ToArray());
        return clean.Length > 20 ? clean[..20] : clean;
    }

    /// <summary>Yahoo grafik yanıtını ayrıştırır; bozuk, boş veya hata yanıtında null döner.</summary>
    public static MarketQuote? ParseChart(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("chart", out var chart)
                || !chart.TryGetProperty("result", out var results)
                || results.ValueKind != JsonValueKind.Array
                || results.GetArrayLength() == 0)
            {
                return null;
            }

            var result = results[0];
            if (!result.TryGetProperty("meta", out var meta))
            {
                return null;
            }

            var symbol = meta.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? string.Empty : string.Empty;
            var price = Number(meta, "regularMarketPrice");
            if (symbol.Length == 0 || price is not double p || double.IsNaN(p) || p < 0)
            {
                return null;
            }

            var name = meta.TryGetProperty("shortName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? symbol : symbol;
            var currency = meta.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty;
            var previous = Number(meta, "chartPreviousClose") ?? Number(meta, "previousClose");

            var closes = new List<double>();
            if (result.TryGetProperty("indicators", out var indicators)
                && indicators.TryGetProperty("quote", out var quotes)
                && quotes.ValueKind == JsonValueKind.Array && quotes.GetArrayLength() > 0
                && quotes[0].TryGetProperty("close", out var close)
                && close.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in close.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var value) && double.IsFinite(value))
                    {
                        closes.Add(value); // null (işlem görmeyen) aralıklar atlanır
                    }
                }
            }

            return new MarketQuote(symbol, name, p, previous, currency, closes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    /// <summary>Fiyatı okunabilir yazar: büyük sayılar iki, küçükler dört ondalıkla.</summary>
    public static string FormatPrice(double price) => price switch
    {
        >= 1000 => price.ToString("#,##0.00", System.Globalization.CultureInfo.CurrentCulture),
        >= 10 => price.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture),
        _ => price.ToString("0.0000", System.Globalization.CultureInfo.CurrentCulture)
    };

    /// <summary>Verilen sembollerin fiyatlarını eşzamanlı çeker; alınamayanlar sonuçta bulunmaz.</summary>
    public static async Task<Dictionary<string, MarketQuote>> FetchAsync(IEnumerable<string> symbols, CancellationToken cancel = default)
    {
        var list = symbols.Select(CleanSymbol).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(WidgetService.MaxMarkets).ToList();
        var tasks = list.Select(symbol => FetchOneAsync(symbol, cancel)).ToList();
        var quotes = await Task.WhenAll(tasks).ConfigureAwait(false);
        var result = new Dictionary<string, MarketQuote>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            if (quotes[i] is { } quote)
            {
                result[list[i]] = quote;
            }
        }

        return result;
    }

    private static async Task<MarketQuote?> FetchOneAsync(string symbol, CancellationToken cancel)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=15m&range=1d";
            var json = await Http.GetStringAsync(url, cancel).ConfigureAwait(false);
            return ParseChart(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null; // internet yok / sembol yok / zaman aşımı
        }
    }
}
