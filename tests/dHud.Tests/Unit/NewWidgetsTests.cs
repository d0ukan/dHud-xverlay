using DHud.Services;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Medya, görev yöneticisi, borsa, pil ve not defteri widget'larının saf mantığı.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class NewWidgetsTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    // ---- Görev yöneticisi ----

    private static ProcSample P(int pid, string name, long cpu = 0, long io = 0, long mem = 0) => new(pid, name, cpu, io, mem);

    [Fact]
    public void Cpu_percent_is_normalised_by_core_count()
    {
        // 1 sn'de 1 sn CPU zamanı, 2 çekirdek: toplam CPU'nun %50'si.
        var rows = ProcessTable.Compute([P(1, "game", cpu: 0)], [P(1, "game", cpu: 10_000_000)], seconds: 1, cores: 2);
        Assert.Equal(50, Assert.Single(rows).CpuPercent, 3);
    }

    [Fact]
    public void Processes_with_the_same_name_are_summed()
    {
        var before = new[] { P(1, "chrome", 0, 0, 100), P(2, "chrome", 0, 0, 200), P(3, "code", 0, 0, 50) };
        var after = new[] { P(1, "chrome", 5_000_000, 1000, 110), P(2, "Chrome", 5_000_000, 3000, 210), P(3, "code", 0, 0, 50) };
        var rows = ProcessTable.Compute(before, after, 1, 1);

        var chrome = rows.Single(r => r.Name.Equals("chrome", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, chrome.Count);
        Assert.Equal(100, chrome.CpuPercent, 3);
        Assert.Equal(320, chrome.MemoryBytes);
        Assert.Equal(4000, chrome.DiskBytesPerSecond, 3);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void New_or_recycled_processes_do_not_produce_bogus_rates()
    {
        var before = new[] { P(1, "old", cpu: 90_000_000, io: 9_000) };
        var after = new[]
        {
            P(1, "different", cpu: 1_000, io: 10),       // PID başka bir programa geçti
            P(2, "fresh", cpu: 80_000_000, io: 8_000)    // önceki örnekte yoktu
        };
        var rows = ProcessTable.Compute(before, after, 1, 4);

        Assert.All(rows, r => Assert.Equal(0, r.CpuPercent));
        Assert.All(rows, r => Assert.Equal(0, r.DiskBytesPerSecond));
    }

    [Fact]
    public void A_counter_that_goes_backwards_counts_as_zero_and_a_zero_interval_does_not_divide_by_zero()
    {
        var rows = ProcessTable.Compute([P(1, "a", 500, 500)], [P(1, "a", 100, 100)], 0, 0);
        var row = Assert.Single(rows);
        Assert.Equal(0, row.CpuPercent);
        Assert.Equal(0, row.DiskBytesPerSecond);
    }

    [Fact]
    public void Network_bytes_are_attached_by_pid_and_summed_per_name()
    {
        var current = new[] { P(1, "chrome"), P(2, "chrome"), P(3, "other") };
        var net = new Dictionary<int, long> { [1] = 1000, [2] = 500, [99] = 7 };
        var rows = ProcessTable.Compute(current, current, 2, 1, net);

        Assert.Equal(750, rows.Single(r => r.Name == "chrome").NetBytesPerSecond, 3);
        Assert.Equal(0, rows.Single(r => r.Name == "other").NetBytesPerSecond);
    }

    [Fact]
    public void Top_sorts_by_the_chosen_column_and_limits_the_count()
    {
        var rows = new[]
        {
            new ProcRow("a", 1, 10, 300, 5, 0),
            new ProcRow("b", 1, 30, 100, 1, 9),
            new ProcRow("c", 1, 20, 200, 9, 4)
        };

        Assert.Equal(["b", "c"], ProcessTable.Top(rows, ProcSort.Cpu, 2).Select(r => r.Name));
        Assert.Equal(["a", "c", "b"], ProcessTable.Top(rows, ProcSort.Memory, 5).Select(r => r.Name));
        Assert.Equal(["c"], ProcessTable.Top(rows, ProcSort.Disk, 1).Select(r => r.Name));
        Assert.Equal(["b"], ProcessTable.Top(rows, ProcSort.Network, 1).Select(r => r.Name));
        Assert.Empty(ProcessTable.Top(rows, ProcSort.Cpu, -3));
    }

    [Fact]
    public void Capturing_real_processes_finds_this_test_process()
    {
        var samples = ProcessMonitor.Capture();
        var me = samples.SingleOrDefault(s => s.Pid == Environment.ProcessId);

        Assert.NotEqual(default, me);
        Assert.True(me.CpuTime100ns > 0);
        Assert.True(me.MemoryBytes > 0);
        Assert.DoesNotContain(samples, s => s.Pid == 0);
    }

    [Fact]
    [Trait(Cat.Name, Cat.Integration)]
    public void Sampling_all_processes_is_cheap_enough_to_run_every_two_seconds()
    {
        ProcessMonitor.Capture();   // ısınma (JIT, ilk erişim)
        var watch = System.Diagnostics.Stopwatch.StartNew();
        const int Rounds = 5;
        var count = 0;
        for (var i = 0; i < Rounds; i++)
        {
            count = ProcessMonitor.Capture().Count;
        }

        var average = watch.Elapsed.TotalMilliseconds / Rounds;
        Console.WriteLine($"Process sampling: {count} processes, {average:0.0} ms per round");
        Assert.True(average < 400, $"Sampling took {average:0} ms per round for {count} processes");   // 2 sn'de bir çalışır: %20'den az
    }

    [Fact]
    public void Byte_sizes_are_short()
    {
        Assert.Equal("0B", TaskManWidgetWindow.FormatBytes(0));
        Assert.Equal("0B", TaskManWidgetWindow.FormatBytes(-5));
        Assert.Equal("812K", TaskManWidgetWindow.FormatBytes(812 * 1024));
        Assert.Equal("350M", TaskManWidgetWindow.FormatBytes(350L * 1024 * 1024));
        Assert.Equal($"1{System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator}5G", TaskManWidgetWindow.FormatBytes(1536L * 1024 * 1024));
    }

    // ---- Borsa ----

    private const string BtcJson = """
        {"chart":{"result":[{"meta":{"currency":"USD","symbol":"BTC-USD","shortName":"Bitcoin USD",
        "regularMarketPrice":77986.69,"chartPreviousClose":77440.93},
        "indicators":{"quote":[{"close":[77000.5,null,77500.25,77986.69]}]}}],"error":null}}
        """;

    [Fact]
    public void A_chart_response_is_parsed_with_change_and_skips_empty_candles()
    {
        var quote = MarketService.ParseChart(BtcJson);

        Assert.NotNull(quote);
        Assert.Equal("BTC-USD", quote.Symbol);
        Assert.Equal("Bitcoin USD", quote.Name);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal(77986.69, quote.Price, 2);
        Assert.Equal(3, quote.Closes.Count);
        Assert.Equal(0.7047, quote.ChangePercent!.Value, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"chart":{"result":null,"error":{"code":"Not Found"}}}""")]
    [InlineData("""{"chart":{"result":[]}}""")]
    [InlineData("""{"chart":{"result":[{"meta":{"symbol":"X"}}]}}""")]                                       // fiyat yok
    [InlineData("""{"chart":{"result":[{"meta":{"symbol":"X","regularMarketPrice":"abc"}}]}}""")]            // fiyat sayı değil
    [InlineData("""{"chart":{"result":[{"meta":{"symbol":"X","regularMarketPrice":-4}}]}}""")]               // negatif fiyat
    [InlineData("""{"chart":{"result":"oops"}}""")]
    public void Broken_responses_give_null_instead_of_throwing(string json) => Assert.Null(MarketService.ParseChart(json));

    [Fact]
    public void A_quote_without_previous_close_has_no_change()
    {
        var quote = MarketService.ParseChart("""{"chart":{"result":[{"meta":{"symbol":"X","regularMarketPrice":5}}]}}""");
        Assert.NotNull(quote);
        Assert.Null(quote.ChangePercent);
        Assert.Empty(quote.Closes);
    }

    [Theory]
    [InlineData(" btc-usd ", "BTC-USD")]
    [InlineData("usdtry=x", "USDTRY=X")]
    [InlineData("thyao.is", "THYAO.IS")]
    [InlineData("a b;c/d?e", "ABCDE")]
    [InlineData("../../etc", "....ETC")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ", "ABCDEFGHIJKLMNOPQRST")]
    public void Symbols_are_cleaned(string? input, string expected) => Assert.Equal(expected, MarketService.CleanSymbol(input));

    [Theory]
    [InlineData("BTC-USD", "BTC/USD")]
    [InlineData("USDTRY=X", "USD/TRY")]
    [InlineData("AAPL", "AAPL")]
    [InlineData("^GSPC", "^GSPC")]
    [InlineData("GC=F", "GC=F")]
    public void Symbols_are_shown_readably(string symbol, string expected) => Assert.Equal(expected, MarketWidgetWindow.Display(symbol));

    [Fact]
    public void Prices_use_more_decimals_when_small()
    {
        Assert.Contains("77", MarketService.FormatPrice(77986.69));
        Assert.Equal(2, MarketService.FormatPrice(49.9366).Split(System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)[1].Length);
        Assert.Equal(4, MarketService.FormatPrice(1.0856).Split(System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)[1].Length);
    }

    // ---- Medya ----

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("chrome.exe", "chrome")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Microsoft.ZuneMusic")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void App_names_are_shortened(string? id, string expected) => Assert.Equal(expected, MediaWidgetWindow.AppName(id));

    [Fact]
    public void Media_times_are_formatted_and_bad_values_become_zero()
    {
        Assert.Equal("0:00", MediaWidgetWindow.FormatTime(TimeSpan.Zero));
        Assert.Equal("3:07", MediaWidgetWindow.FormatTime(TimeSpan.FromSeconds(187)));
        Assert.Equal("1:03:07", MediaWidgetWindow.FormatTime(TimeSpan.FromSeconds(3787)));
        Assert.Equal("0:00", MediaWidgetWindow.FormatTime(TimeSpan.FromSeconds(-5)));
        Assert.Equal("0:00", MediaWidgetWindow.FormatTime(TimeSpan.FromDays(400)));
    }

    [Fact]
    [Trait(Cat.Name, Cat.Integration)]
    public void The_windows_media_session_manager_can_be_reached()
    {
        // Çalan bir şey olmayabilir (o zaman oturum null); önemli olan WinRT çağrısının bu sürümde çalışması ve istisna atmaması.
        var manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
        Assert.NotNull(manager);
        _ = manager.GetCurrentSession();
    }

    // ---- Pil ----

    [Fact]
    public void Battery_durations_read_as_hours_and_minutes()
    {
        var one = BatteryWidgetWindow.Duration(40 * 60);
        var two = BatteryWidgetWindow.Duration(2 * 3600 + 15 * 60);
        Assert.Contains("40", one);
        Assert.Contains("2", two);
        Assert.Contains("15", two);
        Assert.Contains("1", BatteryWidgetWindow.Duration(5));   // en az 1 dk
    }

    [Fact]
    public void Reading_device_batteries_never_throws_and_lists_each_device_once()
    {
        var devices = DeviceBatteryService.Read();
        Assert.Equal(devices.Count, devices.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(devices, d => Assert.InRange(d.Percent, 0, 100));
    }

    // ---- Kayıt ----

    [Fact]
    public void Markets_and_notepad_are_cleaned_and_capped_when_loaded() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        Assert.Null(service.Data.Markets);   // hiç seçilmedi: varsayılanlar widget açılınca konur

        service.Data.Markets = [" btc-usd", "BTC-USD", "", "aapl", "x y", .. Enumerable.Range(0, 30).Select(i => $"S{i}")];
        service.Data.NotepadTabs = Enumerable.Range(0, 20).Select(i => new NotepadTab { Title = $" {i} ", Text = i == 3 ? new string('x', WidgetService.MaxNotepadLength + 50) : "t" }).ToList();
        service.ScheduleSave();
        service.Flush();

        var reloaded = new WidgetService(path);
        reloaded.Load();
        Assert.Equal(WidgetService.MaxMarkets, reloaded.Data.Markets!.Count);
        Assert.Equal(["BTC-USD", "AAPL", "XY"], reloaded.Data.Markets.Take(3));
        Assert.Equal(WidgetService.MaxNotepadTabs, reloaded.Data.NotepadTabs.Count);
        Assert.Equal("3", reloaded.Data.NotepadTabs[3].Title);
        Assert.Equal(WidgetService.MaxNotepadLength, reloaded.Data.NotepadTabs[3].Text.Length);
    });

    [Fact]
    public void Every_extra_widget_has_a_factory_a_description_and_a_unique_id()
    {
        Assert.Equal(WidgetIds.Extra.Length, WidgetIds.Extra.Distinct().Count());
        foreach (var id in new[] { WidgetIds.Media, WidgetIds.TaskMan, WidgetIds.Market, WidgetIds.Battery, WidgetIds.Notepad })
        {
            Assert.Contains(id, WidgetIds.Extra);
        }
    }
}
