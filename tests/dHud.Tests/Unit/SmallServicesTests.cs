using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

[Trait(Cat.Name, Cat.Fast)]
public class GameSessionTrackerTests
{
    private static GameSessionTracker Track(out List<GameSummary> finished)
    {
        var list = new List<GameSummary>();
        var tracker = new GameSessionTracker();
        tracker.Finished += list.Add;
        finished = list;
        return tracker;
    }

    private static void Play(GameSessionTracker t, int seconds, float fps = 100, float cpu = 60, float gpu = 70)
    {
        for (var i = 0; i < seconds; i++)
        {
            t.Sample(fps, cpu, gpu);
        }
    }

    private static void Idle(GameSessionTracker t, int seconds)
    {
        for (var i = 0; i < seconds; i++)
        {
            t.Sample(null, null, null);
        }
    }

    [Fact]
    public void A_short_burst_of_fps_is_not_a_game()
    {
        var t = Track(out var done);
        Play(t, 5);
        Idle(t, 60);
        Assert.Empty(done);
        Assert.False(t.IsActive);
    }

    [Fact]
    public void A_session_shorter_than_two_minutes_gives_no_summary()
    {
        var t = Track(out var done);
        Play(t, 90);
        Idle(t, GameSessionTracker.EndAfterSeconds + 5);
        Assert.Empty(done);
    }

    [Fact]
    public void Summary_has_average_low_and_peak_temperatures()
    {
        var t = Track(out var done);
        for (var i = 0; i < 300; i++)
        {
            t.Sample(i % 100 == 0 ? 20 : 100, 60 + i / 30, 70); // 3 saniye 20 FPS, gerisi 100
        }

        Assert.True(t.IsActive);
        Idle(t, GameSessionTracker.EndAfterSeconds - 1);
        Assert.Empty(done); // 19 sn boşlukta bitmez
        Idle(t, 1);

        var s = Assert.Single(done);
        Assert.Equal(300, s.Duration.TotalSeconds, 0);
        Assert.Equal((3 * 20 + 297 * 100) / 300.0, s.AvgFps, 1);
        Assert.Equal(20, s.OnePercentLow);
        Assert.Equal(20, s.MinFps);
        Assert.Equal(100, s.MaxFps);
        Assert.Equal(69, s.MaxCpuTemp);
        Assert.Equal(70, s.MaxGpuTemp);
    }

    [Fact]
    public void Alt_tab_gap_does_not_split_the_session()
    {
        var t = Track(out var done);
        Play(t, 200);
        Idle(t, 10);
        Play(t, 200);
        Idle(t, GameSessionTracker.EndAfterSeconds + 1);
        Assert.Single(done);
    }
}

[Trait(Cat.Name, Cat.Fast)]
public class TemperatureAlertTests
{
    private static AppSettings Settings(int hot = 90, int seconds = 10) =>
        new() { AlertEnabled = true, AlertSeconds = seconds, CpuWarnTemperature = 70, CpuHotTemperature = hot, GpuWarnTemperature = 70, GpuHotTemperature = hot };

    [Fact]
    public void Alerts_only_after_the_configured_time_above_the_hot_threshold_and_only_once()
    {
        var alert = new TemperatureAlert("CPU", isGpu: false);
        var settings = Settings();
        var t0 = new DateTime(2026, 11, 1, 12, 0, 0);

        Assert.Null(alert.Check(95, settings, t0));
        Assert.Null(alert.Check(95, settings, t0.AddSeconds(5)));
        Assert.NotNull(alert.Check(95, settings, t0.AddSeconds(10)));
        Assert.Null(alert.Check(95, settings, t0.AddSeconds(30)));  // tekrar uyarmaz
    }

    [Fact]
    public void Cooling_below_the_hysteresis_band_re_arms_the_alert()
    {
        var alert = new TemperatureAlert("GPU", isGpu: true);
        var settings = Settings();
        var t = new DateTime(2026, 11, 1, 12, 0, 0);

        alert.Check(95, settings, t);
        Assert.NotNull(alert.Check(95, settings, t.AddSeconds(15)));
        Assert.Null(alert.Check(88, settings, t.AddSeconds(20)));   // 3 derecelik bantta: sıfırlanmaz
        Assert.Null(alert.Check(95, settings, t.AddSeconds(40)));
        Assert.Null(alert.Check(70, settings, t.AddSeconds(50)));   // soğudu: yeniden silahlanır
        alert.Check(95, settings, t.AddSeconds(60));
        Assert.NotNull(alert.Check(95, settings, t.AddSeconds(75)));
    }

    [Fact]
    public void A_missing_reading_or_disabled_alerts_never_alert()
    {
        var alert = new TemperatureAlert("CPU", false);
        var t = new DateTime(2026, 11, 1, 12, 0, 0);
        Assert.Null(alert.Check(null, Settings(), t));

        var off = Settings();
        off.AlertEnabled = false;
        alert.Check(99, off, t);
        Assert.Null(alert.Check(99, off, t.AddMinutes(5)));
    }
}

[Trait(Cat.Name, Cat.Fast)]
public class WeatherParsingTests
{
    [Fact]
    public void Forecast_parses_current_conditions_and_skips_days_with_missing_values()
    {
        const string json = """
            {"current":{"temperature_2m":18.5,"apparent_temperature":17.2,"weather_code":61,"wind_speed_10m":12.3,"is_day":0},
             "daily":{"time":["2026-11-01","2026-11-02","2026-11-03"],"weather_code":[61,3,0],
                      "temperature_2m_max":[19,17,null],"temperature_2m_min":[12,11,9]}}
            """;
        var data = WeatherService.ParseForecast(json);

        Assert.Equal(18.5, data.Now.Temperature);
        Assert.Equal(17.2, data.Now.Feels);
        Assert.Equal(61, data.Now.Code);
        Assert.False(data.Now.IsDay);
        Assert.Equal(2, data.Days.Count);
        Assert.Equal(19, data.Days[0].Max);
    }

    [Fact]
    public void Geocoding_merges_repeated_names_and_tolerates_missing_results()
    {
        var places = WeatherService.ParseGeocoding(
            """{"results":[{"name":"Istanbul","latitude":41.01,"longitude":28.95,"admin1":"Istanbul","country":"Türkiye"},{"name":"X","latitude":1,"longitude":2}]}""");
        Assert.Equal(2, places.Count);
        Assert.Equal("Istanbul, Türkiye", places[0].Display);
        Assert.Empty(WeatherService.ParseGeocoding("{}"));
    }

    [Theory]
    [InlineData(0, "clear")]
    [InlineData(2, "partly")]
    [InlineData(3, "cloudy")]
    [InlineData(45, "fog")]
    [InlineData(53, "drizzle")]
    [InlineData(61, "rain")]
    [InlineData(73, "snow")]
    [InlineData(81, "showers")]
    [InlineData(95, "thunder")]
    [InlineData(999, "thunder")]
    public void Weather_codes_map_to_kinds(int code, string kind) => Assert.Equal(kind, WeatherService.Kind(code));

    [Fact]
    public void Temperature_conversion()
    {
        Assert.Equal(32, WeatherService.ToFahrenheit(0), 9);
        Assert.Equal(212, WeatherService.ToFahrenheit(100), 9);
        Assert.Equal(-40, WeatherService.ToFahrenheit(-40), 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"current\":{}}")]
    public void Broken_forecast_throws_a_catchable_exception_instead_of_hanging(string json) =>
        Assert.ThrowsAny<Exception>(() => WeatherService.ParseForecast(json));
}
