using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DHud.Services;

/// <summary>Şehir arama sonucu.</summary>
public sealed record GeoPlace(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string Display => string.Join(", ", new[] { Name, Region, Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}

public sealed record WeatherNow(double Temperature, double Feels, int Code, double WindKmh, bool IsDay);

public sealed record WeatherDay(DateTime Date, double Min, double Max, int Code);

public sealed record WeatherData(WeatherNow Now, IReadOnlyList<WeatherDay> Days, DateTime FetchedUtc);

/// <summary>
/// Hava durumu (Open-Meteo: ücretsiz, anahtar ve hesap gerektirmez). Yalnızca hava durumu widget'ı açık ve bir şehir seçilmişken
/// internete çıkılır; gönderilen tek şey şehir adı (aramada) ve seçilen şehrin koordinatlarıdır.
/// </summary>
public static class WeatherService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("dHud (+https://github.com/d0ukan/dHud-xverlay)");
        return client;
    }

    public static async Task<IReadOnlyList<GeoPlace>> SearchAsync(string query, string language, CancellationToken ct = default)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=6&format=json"
                  + $"&language={Uri.EscapeDataString(language)}&name={Uri.EscapeDataString(query.Trim())}";
        return ParseGeocoding(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
    }

    /// <summary>Önce Open-Meteo; servis çökmüşse/yanıt vermiyorsa MET Norway (yedek) denenir. İkisi de başarısızsa ilk hata fırlatılır.</summary>
    public static async Task<WeatherData> FetchAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        try
        {
            return await FetchOpenMeteoAsync(latitude, longitude, ct).ConfigureAwait(false);
        }
        catch (Exception first) when (!ct.IsCancellationRequested)
        {
            try
            {
                return await FetchMetNoAsync(latitude, longitude, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
                throw;
            }
        }
    }

    private static async Task<WeatherData> FetchMetNoAsync(double latitude, double longitude, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = $"https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={latitude.ToString("0.####", inv)}&lon={longitude.ToString("0.####", inv)}";
        return ParseMetNo(await Http.GetStringAsync(url, ct).ConfigureAwait(false), TimeZoneInfo.Local);
    }

    private static async Task<WeatherData> FetchOpenMeteoAsync(double latitude, double longitude, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = "https://api.open-meteo.com/v1/forecast"
                  + $"?latitude={latitude.ToString("0.####", inv)}&longitude={longitude.ToString("0.####", inv)}"
                  + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day"
                  + "&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=3&timezone=auto";
        return ParseForecast(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// MET Norway "locationforecast compact" yanıtını ayrıştırır (yedek kaynak). Günler <paramref name="zone"/> saatine göre bölünür
    /// (şehrin saat dilimi bilinmediği için bilgisayarınki kullanılır); gün kodu öğleye en yakın simgeden gelir.
    /// </summary>
    public static WeatherData ParseMetNo(string json, TimeZoneInfo zone)
    {
        using var doc = JsonDocument.Parse(json);
        var series = doc.RootElement.GetProperty("properties").GetProperty("timeseries").EnumerateArray().ToList();
        if (series.Count == 0)
        {
            throw new InvalidDataException("empty forecast");
        }

        var points = new List<(DateTime Local, double Temp, double Wind, string? Symbol)>();
        foreach (var item in series)
        {
            if (!DateTime.TryParse(item.GetProperty("time").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
            {
                continue;
            }

            var data = item.GetProperty("data");
            var details = data.GetProperty("instant").GetProperty("details");
            if (!details.TryGetProperty("air_temperature", out var temp))
            {
                continue;
            }

            string? symbol = null;
            foreach (var next in new[] { "next_1_hours", "next_6_hours", "next_12_hours" })
            {
                if (data.TryGetProperty(next, out var n) && n.TryGetProperty("summary", out var sum) && sum.TryGetProperty("symbol_code", out var code))
                {
                    symbol = code.GetString();
                    break;
                }
            }

            var wind = details.TryGetProperty("wind_speed", out var w) ? w.GetDouble() * 3.6 : 0;   // m/s -> km/h
            points.Add((TimeZoneInfo.ConvertTimeFromUtc(utc, zone), temp.GetDouble(), wind, symbol));
        }

        if (points.Count == 0)
        {
            throw new InvalidDataException("no usable forecast points");
        }

        var first = points[0];
        var (code0, isDay) = SymbolToWmo(first.Symbol);
        var now = new WeatherNow(first.Temp, first.Temp, code0, first.Wind, isDay);

        var days = new List<WeatherDay>();
        foreach (var group in points.GroupBy(p => p.Local.Date).OrderBy(g => g.Key).Take(3))
        {
            var noon = group.OrderBy(p => Math.Abs(p.Local.Hour - 12)).First();
            days.Add(new WeatherDay(group.Key, group.Min(p => p.Temp), group.Max(p => p.Temp), SymbolToWmo(noon.Symbol).Code));
        }

        return new WeatherData(now, days, DateTime.UtcNow);
    }

    /// <summary>MET Norway simge adını (ör. "partlycloudy_day", "heavyrainshowers") en yakın WMO koduna ve gündüz/gece bilgisine çevirir.</summary>
    public static (int Code, bool IsDay) SymbolToWmo(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            return (3, true);
        }

        var isDay = !symbol.EndsWith("_night", StringComparison.Ordinal);
        var name = symbol.Split('_')[0];
        var heavy = name.StartsWith("heavy", StringComparison.Ordinal);
        var code = name switch
        {
            "clearsky" => 0,
            "fair" => 1,
            "partlycloudy" => 2,
            "cloudy" => 3,
            "fog" => 45,
            _ when name.Contains("thunder") => 95,
            _ when name.Contains("sleet") => 67,
            _ when name.Contains("snow") && name.Contains("showers") => 85,
            _ when name.Contains("snow") => heavy ? 75 : name.StartsWith("light", StringComparison.Ordinal) ? 71 : 73,
            _ when name.Contains("rain") && name.Contains("showers") => heavy ? 82 : 80,
            _ when name.Contains("rain") => heavy ? 65 : name.StartsWith("light", StringComparison.Ordinal) ? 61 : 63,
            _ => 3
        };
        return (code, isDay);
    }

    public static IReadOnlyList<GeoPlace> ParseGeocoding(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<GeoPlace>();
        if (!doc.RootElement.TryGetProperty("results", out var results))
        {
            return result;
        }

        foreach (var item in results.EnumerateArray())
        {
            if (!item.TryGetProperty("latitude", out var lat) || !item.TryGetProperty("longitude", out var lon))
            {
                continue;
            }

            result.Add(new GeoPlace(Str(item, "name"), Str(item, "admin1"), Str(item, "country"), lat.GetDouble(), lon.GetDouble()));
        }

        return result;
    }

    public static WeatherData ParseForecast(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var now = new WeatherNow(
            cur.GetProperty("temperature_2m").GetDouble(),
            cur.TryGetProperty("apparent_temperature", out var feels) ? feels.GetDouble() : cur.GetProperty("temperature_2m").GetDouble(),
            cur.GetProperty("weather_code").GetInt32(),
            cur.TryGetProperty("wind_speed_10m", out var wind) ? wind.GetDouble() : 0,
            !cur.TryGetProperty("is_day", out var day) || day.GetInt32() != 0);

        var days = new List<WeatherDay>();
        if (root.TryGetProperty("daily", out var daily))
        {
            var times = daily.GetProperty("time").EnumerateArray().ToList();
            var max = daily.GetProperty("temperature_2m_max").EnumerateArray().ToList();
            var min = daily.GetProperty("temperature_2m_min").EnumerateArray().ToList();
            var codes = daily.GetProperty("weather_code").EnumerateArray().ToList();
            for (var i = 0; i < times.Count && i < max.Count && i < min.Count && i < codes.Count; i++)
            {
                if (DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && max[i].ValueKind == JsonValueKind.Number && min[i].ValueKind == JsonValueKind.Number)
                {
                    days.Add(new WeatherDay(date, min[i].GetDouble(), max[i].GetDouble(), codes[i].GetInt32()));
                }
            }
        }

        return new WeatherData(now, days, DateTime.UtcNow);
    }

    /// <summary>WMO hava kodunu sade bir türe çevirir (metin ve simge buna göre seçilir).</summary>
    public static string Kind(int code) => code switch
    {
        0 => "clear",
        1 or 2 => "partly",
        3 => "cloudy",
        45 or 48 => "fog",
        >= 51 and <= 57 => "drizzle",
        >= 61 and <= 67 => "rain",
        >= 71 and <= 77 or 85 or 86 => "snow",
        >= 80 and <= 82 => "showers",
        >= 95 => "thunder",
        _ => "cloudy"
    };

    public static string Icon(int code, bool isDay) => Kind(code) switch
    {
        "clear" => isDay ? "☀" : "☾",
        "partly" => isDay ? "⛅" : "☁",
        "cloudy" => "☁",
        "fog" => "〰",
        "drizzle" or "showers" => "🌦",
        "rain" => "🌧",
        "snow" => "❄",
        "thunder" => "⛈",
        _ => "☁"
    };

    public static double ToFahrenheit(double celsius) => celsius * 9 / 5 + 32;

    private static string Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
}
