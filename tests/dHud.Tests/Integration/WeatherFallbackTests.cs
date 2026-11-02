using System.IO;
using System.Net.Http;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>Hava durumu: ana servis (Open-Meteo) çökse de widget boş kalmasın diye yedek kaynak (MET Norway) çevrilir.</summary>
public class WeatherFallbackTests
{
    private const string MetNoSample = """
    {"type":"Feature","properties":{"timeseries":[
      {"time":"2026-11-02T12:00:00Z","data":{"instant":{"details":{"air_temperature":11.4,"wind_speed":3.0}},"next_1_hours":{"summary":{"symbol_code":"partlycloudy_day"}}}},
      {"time":"2026-11-02T15:00:00Z","data":{"instant":{"details":{"air_temperature":13.0,"wind_speed":2.0}},"next_1_hours":{"summary":{"symbol_code":"lightrain"}}}},
      {"time":"2026-11-03T00:00:00Z","data":{"instant":{"details":{"air_temperature":4.2,"wind_speed":1.0}},"next_6_hours":{"summary":{"symbol_code":"clearsky_night"}}}},
      {"time":"2026-11-03T12:00:00Z","data":{"instant":{"details":{"air_temperature":9.0,"wind_speed":5.0}},"next_6_hours":{"summary":{"symbol_code":"heavyrainandthunder"}}}},
      {"time":"2026-11-04T12:00:00Z","data":{"instant":{"details":{"air_temperature":-1.5,"wind_speed":0.5}},"next_6_hours":{"summary":{"symbol_code":"snow"}}}}
    ]}}
    """;

    [Fact]
    public void MetNo_forecast_becomes_current_conditions_and_three_days_in_the_given_zone()
    {
        var data = WeatherService.ParseMetNo(MetNoSample, TimeZoneInfo.Utc);

        Assert.Equal(11.4, data.Now.Temperature);
        Assert.Equal(2, data.Now.Code);
        Assert.True(data.Now.IsDay);
        Assert.Equal(10.8, data.Now.WindKmh, 1);   // 3 m/s
        Assert.Equal(3, data.Days.Count);
        Assert.Equal(new DateTime(2026, 11, 2), data.Days[0].Date);
        Assert.Equal(11.4, data.Days[0].Min);
        Assert.Equal(13.0, data.Days[0].Max);
        Assert.Equal(4.2, data.Days[1].Min);
        Assert.Equal(95, data.Days[1].Code);
        Assert.Equal(-1.5, data.Days[2].Min);
        Assert.Equal("snow", WeatherService.Kind(data.Days[2].Code));
    }

    [Theory]
    [InlineData("clearsky_day", 0, true)]
    [InlineData("clearsky_night", 0, false)]
    [InlineData("fair_polartwilight", 1, true)]
    [InlineData("cloudy", 3, true)]
    [InlineData("fog", 45, true)]
    [InlineData("lightrain", 61, true)]
    [InlineData("heavyrain", 65, true)]
    [InlineData("rainshowers_day", 80, true)]
    [InlineData("heavyrainshowers_night", 82, false)]
    [InlineData("sleet", 67, true)]
    [InlineData("lightsnow", 71, true)]
    [InlineData("snowshowers_day", 85, true)]
    [InlineData("rainandthunder", 95, true)]
    [InlineData("lightssleetshowersandthunder_day", 95, true)]
    [InlineData("", 3, true)]
    [InlineData(null, 3, true)]
    [InlineData("somethingnew", 3, true)]
    public void Symbols_map_to_a_wmo_code_and_day_night(string? symbol, int code, bool day) =>
        Assert.Equal((code, day), WeatherService.SymbolToWmo(symbol));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"properties\":{\"timeseries\":[]}}")]
    [InlineData("not json")]
    public void A_broken_or_empty_fallback_response_throws_instead_of_showing_nonsense(string json) =>
        Assert.ThrowsAny<Exception>(() => WeatherService.ParseMetNo(json, TimeZoneInfo.Utc));

    /// <summary>Gerçek ağ: Ankara için tam yol (ana servis çökükse yedek). İnternet yoksa sessizce geçer; varsa mutlaka veri gelmeli.</summary>
    [Fact]
    [Trait(Cat.Name, Cat.Integration)]
    public async Task Ankara_weather_can_be_fetched_from_at_least_one_provider()
    {
        WeatherData data;
        try
        {
            data = await WeatherService.FetchAsync(39.9199, 32.8543);
        }
        catch (HttpRequestException)
        {
            return;   // bu makinede internet yok
        }

        if (Environment.GetEnvironmentVariable("DHUD_WX_OUT") is { Length: > 0 } outPath)
        {
            File.WriteAllText(outPath, $"{data.Now.Temperature}C code {data.Now.Code} days {data.Days.Count}");
        }

        Assert.InRange(data.Now.Temperature, -40, 55);
        Assert.NotEmpty(data.Days);
        Assert.All(data.Days, d => Assert.True(d.Max >= d.Min));
    }
}
