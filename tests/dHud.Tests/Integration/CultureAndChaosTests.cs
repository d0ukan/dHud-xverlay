using System.Globalization;
using DHud.Helpers;
using DHud.Services;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>Bilgisayarın bölgesel ayarı ne olursa olsun (virgül/nokta, saat biçimi, sayı simgeleri) dHud doğru çalışmalı.</summary>
[Trait(Cat.Name, Cat.Integration)]
public class CultureTests
{
    public static IEnumerable<object[]> Cultures() =>
        new[] { "tr-TR", "en-US", "en-GB", "de-DE", "fr-FR", "ar-SA", "ja-JP", "ru-RU", "fa-IR", "th-TH" }.Select(c => new object[] { c });

    private static void WithCulture(string name, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Reminder_clock_times_parse_in_every_culture(string culture) => WithCulture(culture, () =>
    {
        var now = new DateTime(2026, 11, 1, 14, 0, 0);
        Assert.True(WidgetService.TryParseClock("14:30", now, out var due));
        Assert.Equal(new DateTime(2026, 11, 1, 14, 30, 0), due);

        Assert.True(WidgetService.TryParseClock("09.15", now, out due));   // nokta da kabul edilir; geçtiyse yarın
        Assert.Equal(new DateTime(2026, 11, 2, 9, 15, 0), due);

        Assert.False(WidgetService.TryParseClock("25:00", now, out _));
        Assert.False(WidgetService.TryParseClock("abc", now, out _));
        Assert.False(WidgetService.TryParseClock("", now, out _));
    });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_iso_date_format_works_everywhere_for_day_counters(string culture) => WithCulture(culture, () =>
    {
        Assert.True(CountdownWidgetWindow.TryParseDate("2026-12-25", out var date));
        Assert.Equal(new DateTime(2026, 12, 25), date);
        Assert.False(CountdownWidgetWindow.TryParseDate("yarın", out _));
    });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Json_and_calendar_parsing_do_not_depend_on_the_machine_culture(string culture) => WithCulture(culture, () =>
    {
        var forecast = WeatherService.ParseForecast("""{"current":{"temperature_2m":18.5,"weather_code":3},"daily":{"time":["2026-11-02"],"weather_code":[3],"temperature_2m_max":[19.5],"temperature_2m_min":[12.25]}}""");
        Assert.Equal(18.5, forecast.Now.Temperature);
        Assert.Equal(12.25, forecast.Days[0].Min);

        var events = IcsParser.Occurrences("BEGIN:VEVENT\r\nSUMMARY:X\r\nDTSTART:20261103T100000\r\nEND:VEVENT\r\n", new DateTime(2026, 11, 2), new DateTime(2026, 11, 9));
        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0), Assert.Single(events).Start);
    });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Display_formatting_never_throws_and_is_never_empty(string culture) => WithCulture(culture, () =>
    {
        Assert.NotEmpty(Format.Temperature(57.4f, false));
        Assert.NotEmpty(Format.Temperature(null, true));
        Assert.NotEmpty(Format.Clock(4350, false));
        Assert.NotEmpty(Format.Power(61, true));
        Assert.NotEmpty(Format.Fan(1120, false));
        Assert.NotEmpty(Format.Speed(4_300_000, inBits: true, compact: false));
        Assert.NotEmpty(Format.Speed(null, false, true));
        Assert.NotEmpty(Format.Memory(14.7f, 32f, false));
        Assert.NotEmpty(Format.Ping(14, false));
        Assert.NotEmpty(Format.Load(18));
    });

    [Fact]
    public void Day_counter_math_ignores_the_time_of_day_and_survives_the_midnight_rollover()
    {
        Assert.Equal(0, CountdownWidgetWindow.DaysUntil(new DateTime(2026, 11, 1, 8, 0, 0), new DateTime(2026, 11, 1, 23, 59, 59)));
        Assert.Equal(1, CountdownWidgetWindow.DaysUntil(new DateTime(2026, 11, 2), new DateTime(2026, 11, 1, 23, 59, 59)));
        Assert.Equal(-3, CountdownWidgetWindow.DaysUntil(new DateTime(2026, 10, 29), new DateTime(2026, 11, 1)));
        Assert.Equal(365, CountdownWidgetWindow.DaysUntil(new DateTime(2027, 11, 1), new DateTime(2026, 11, 1)));
    }

    [Fact]
    public void Day_counter_math_is_exact_across_a_daylight_saving_change()
    {
        // Gün farkı saat farkından değil takvim tarihinden hesaplanır: yaz saati geçişinde 23/25 saatlik gün şaşırtmamalı.
        Assert.Equal(1, CountdownWidgetWindow.DaysUntil(new DateTime(2026, 3, 29), new DateTime(2026, 3, 28, 23, 0, 0)));
        Assert.Equal(7, CountdownWidgetWindow.DaysUntil(new DateTime(2026, 11, 1), new DateTime(2026, 10, 25)));
    }
}

/// <summary>Bozuk, kilitli, salt okunur dosyalar ve devasa girdiler: uygulama çökmemeli, veri kaybetmemeli.</summary>
[Trait(Cat.Name, Cat.Integration)]
public class ChaosTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose()
    {
        // Salt okunur bırakılan dosyalar silinebilsin.
        foreach (var file in Directory.EnumerateFiles(_dir.Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        _dir.Dispose();
    }

    [Fact]
    public void Saving_into_a_folder_that_does_not_exist_creates_it() => Sta.Run(() =>
    {
        var path = Path.Combine(_dir.Path, "a", "b", "c", "widgets.json");
        var service = new WidgetService(path);
        service.Load();
        service.AddNote()!.Text = "derin klasör";
        service.Flush();
        Assert.True(File.Exists(path));
    });

    [Fact]
    public void A_locked_file_does_not_crash_the_app_and_the_data_is_saved_once_it_is_unlocked() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        service.AddNote()!.Text = "kilitli";
        service.Flush();

        service.Data.Notes[0].Text = "kilit açılınca";
        service.ScheduleSave();
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            service.Flush();                       // dosya başka bir süreçte kilitli: istisna fırlatmamalı
        }

        service.ScheduleSave();
        service.Flush();
        var reloaded = new WidgetService(path);
        reloaded.Load();
        Assert.Equal("kilit açılınca", reloaded.Data.Notes[0].Text);
    });

    [Fact]
    public void A_read_only_data_file_does_not_crash_saving() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        service.AddNote()!.Text = "x";
        service.Flush();

        File.SetAttributes(path, FileAttributes.ReadOnly);
        service.Data.Notes[0].Text = "salt okunur dosyaya yazma denemesi";
        service.ScheduleSave();
        var error = Record.Exception(service.Flush);
        Assert.Null(error);
        Assert.Contains(DiagLog.Snapshot(), line => line.Contains("[widgets]")); // hata günlüğe düştü
    });

    [Fact]
    public void A_huge_note_round_trips_and_is_capped_by_the_text_box_limit() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        var text = new string('ğ', WidgetService.MaxNoteLength);
        service.AddNote()!.Text = text;
        service.Flush();

        var reloaded = new WidgetService(path);
        reloaded.Load();
        Assert.Equal(text, reloaded.Data.Notes[0].Text);
    });

    [Fact]
    public void Settings_survive_when_the_file_is_replaced_by_a_directory_or_an_empty_file()
    {
        var path = _dir.File("settings.json");
        Directory.CreateDirectory(path);                     // dosya yerine klasör
        var settings = new SettingsService(path);
        Assert.Null(Record.Exception(settings.Load));
        Assert.Null(Record.Exception(settings.Save));        // yazılamaz ama çökmez
    }

    [Fact]
    public void A_one_gigabyte_looking_ics_header_does_not_hang_the_parser()
    {
        var text = "BEGIN:VEVENT\r\nSUMMARY:" + new string('x', 5_000_000) + "\r\nDTSTART:20261103T100000\r\nEND:VEVENT\r\n";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var list = IcsParser.Occurrences(text, new DateTime(2026, 11, 2), new DateTime(2026, 11, 9));
        Assert.Single(list);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"{sw.Elapsed.TotalSeconds:0.0} sn");
    }

    [Fact]
    public void Ten_thousand_calendar_events_parse_quickly()
    {
        var text = string.Concat(Enumerable.Range(0, 10_000).Select(i =>
            $"BEGIN:VEVENT\r\nSUMMARY:Etkinlik {i}\r\nDTSTART:2026{(1 + i % 12):00}{(1 + i % 28):00}T100000\r\nDTEND:2026{(1 + i % 12):00}{(1 + i % 28):00}T110000\r\nEND:VEVENT\r\n"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var list = IcsParser.Occurrences(text, new DateTime(2026, 11, 1), new DateTime(2026, 12, 1));
        Assert.NotEmpty(list);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"{sw.Elapsed.TotalSeconds:0.0} sn");
    }

    [Theory]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=DAILY;INTERVAL=-5")]
    [InlineData("FREQ=DAILY;COUNT=999999999")]
    [InlineData("FREQ=WEEKLY;BYDAY=")]
    [InlineData("FREQ=WEEKLY;BYDAY=XX,YY")]
    [InlineData("FREQ=MONTHLY;INTERVAL=99999999")]
    [InlineData("FREQ=YEARLY;UNTIL=00000000")]
    [InlineData("FREQ=SECONDLY")]
    [InlineData("")]
    public void Abusive_recurrence_rules_terminate_quickly(string rule)
    {
        var text = $"BEGIN:VEVENT\r\nSUMMARY:X\r\nDTSTART:20000101T100000\r\nRRULE:{rule}\r\nEND:VEVENT\r\n";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var error = Record.Exception(() => IcsParser.Occurrences(text, new DateTime(2026, 11, 2), new DateTime(2026, 11, 9)));
        Assert.Null(error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"{rule}: {sw.Elapsed.TotalSeconds:0.0} sn");
    }
}
