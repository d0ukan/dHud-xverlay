using System.Text;
using System.Text.Json;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Fuzz;

/// <summary>
/// Bozuk/saldırgan girdi: hiçbir okuyucu beklenmeyen istisna fırlatmamalı veya sonsuza dek sürmemeli.
/// Her yineleme tohumludur; hata mesajındaki tohumla aynı girdi yeniden üretilir (<c>DHUD_FUZZ_SEED</c> ortam değişkeni başlangıç tohumunu değiştirir).
/// </summary>
[Trait(Cat.Name, Cat.Fuzz)]
public class FuzzTests : IDisposable
{
    private static readonly int BaseSeed = int.TryParse(Environment.GetEnvironmentVariable("DHUD_FUZZ_SEED"), out var s) ? s : 20261101;

    private const int Iterations = 400;

    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>İşi en çok 10 sn içinde bitirir; istisna veya zaman aşımı tohumla birlikte raporlanır.</summary>
    private static void Guard(string what, int seed, Action action, Func<Exception, bool>? allowed = null)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex) when (allowed?.Invoke(ex) != true)
            {
                error = ex;
            }
            catch (Exception)
            {
                // izin verilen (belgelenmiş) hata türü
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10)))
        {
            Assert.Fail($"{what}: 10 sn içinde bitmedi (kilitlenme/sonsuz döngü). Tohum {seed}");
        }

        if (error is not null)
        {
            Assert.Fail($"{what}: beklenmeyen {error.GetType().Name} — {error.Message}\nTohum {seed}\n{error.StackTrace}");
        }
    }

    private static string Fixture(string name) => File.ReadAllText(Repo.Path("tests", "dHud.Tests", "Fixtures", name));

    [Fact]
    public void Settings_load_survives_corrupted_files()
    {
        var sample = Fixture("settings-v1.0.28.json");
        for (var i = 0; i < Iterations; i++)
        {
            var seed = BaseSeed + i;
            var path = _dir.File("settings.json");
            File.WriteAllText(path, new Mutator(seed).Mutate(sample));
            Guard("SettingsService.Load", seed, () =>
            {
                var service = new SettingsService(path);
                service.Load();
                Assert.NotNull(service.Current);
                service.Save();
            });
        }
    }

    [Fact]
    public void Settings_load_survives_random_bytes_and_empty_files()
    {
        var rnd = new Random(BaseSeed);
        var path = _dir.File("settings.json");
        for (var i = 0; i < 100; i++)
        {
            var bytes = new byte[rnd.Next(0, 5000)];
            rnd.NextBytes(bytes);
            File.WriteAllBytes(path, bytes);
            Guard("SettingsService.Load(rastgele bayt)", BaseSeed, () => new SettingsService(path).Load());
        }
    }

    [Fact]
    public void Deeply_nested_json_does_not_crash_the_loaders()
    {
        foreach (var depth in new[] { 100, 1000, 100_000 })
        {
            File.WriteAllText(_dir.File("settings.json"), Mutator.Deep(depth));
            File.WriteAllText(_dir.File("widgets.json"), "{\"Notes\":" + Mutator.Deep(depth) + "}");
            Guard($"derin json {depth}", depth, () =>
            {
                new SettingsService(_dir.File("settings.json")).Load();
                new WidgetService(_dir.File("widgets.json")).Load();
            });
        }
    }

    [Fact]
    public void Widget_data_load_survives_corrupted_files()
    {
        var seeded = new WidgetService(_dir.File("seed-widgets.json"));
        Sta.Run(() =>
        {
            seeded.Load();
            seeded.AddNote()!.Text = "örnek not";
            seeded.Data.Todos.Add(new TodoItem { Text = "yap" });
            seeded.Data.Countdowns.Add(new Countdown { Name = "tatil", Date = new DateTime(2026, 12, 25) });
            seeded.Flush();
        });
        var sample = File.ReadAllText(_dir.File("seed-widgets.json"));

        for (var i = 0; i < Iterations; i++)
        {
            var seed = BaseSeed + i;
            var path = _dir.File("widgets.json");
            File.WriteAllText(path, new Mutator(seed).Mutate(sample));
            Guard("WidgetService.Load", seed, () =>
            {
                var service = new WidgetService(path);
                service.Load();
                Assert.NotNull(service.Data);
                service.ScheduleSave();
                service.Flush();
            });
        }
    }

    [Fact]
    public void Profile_import_rejects_or_applies_corrupted_files_without_crashing()
    {
        string sample = null!;
        Sta.Run(() =>
        {
            var settings = new SettingsService(_dir.File("p-settings.json"));
            settings.Load();
            var widgets = new WidgetService(_dir.File("p-widgets.json"));
            widgets.Load();
            widgets.AddNote()!.Text = "x";
            var profiles = new ProfileService(settings, widgets, "1.0.test", _dir.File("p-profiles"));
            var path = _dir.File("sample" + ProfileService.Extension);
            profiles.Export(path, includeWidgetData: true, includePairingKey: true);
            sample = File.ReadAllText(path);
        });

        for (var i = 0; i < Iterations; i++)
        {
            var seed = BaseSeed + i;
            var path = _dir.File("fuzz" + ProfileService.Extension);
            File.WriteAllText(path, new Mutator(seed).Mutate(sample));
            Guard("ProfileService.Read/Apply", seed, () =>
            {
                var settings = new SettingsService(_dir.File("t-settings.json"));
                settings.Load();
                var widgets = new WidgetService(_dir.File("t-widgets.json"));
                widgets.Load();
                var profiles = new ProfileService(settings, widgets, "1.0.test", _dir.File("t-profiles"));
                var file = ProfileService.Read(path);                       // yalnızca ProfileException beklenir
                profiles.Apply(file, includeWidgetData: true, includePairingKey: true);
                Assert.False(settings.Current.FanControlEnabled, "içe aktarma fan kontrolünü açmamalı");
            }, ex => ex is ProfileException);
        }
    }

    [Fact]
    public void Calendar_parser_survives_corrupted_feeds()
    {
        const string sample = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Toplantı\r\nDTSTART;TZID=Europe/Istanbul:20261103T100000\r\nDTEND:20261103T110000\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=20\r\nEXDATE:20261110T100000\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:Tüm gün\r\nDTSTART;VALUE=DATE:20261105\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        for (var i = 0; i < Iterations * 5; i++)
        {
            var seed = BaseSeed + i;
            var text = new Mutator(seed).Mutate(sample, 1 + i % 6);
            Guard("IcsParser", seed, () => IcsParser.Occurrences(text, new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)));
        }
    }

    [Fact]
    public void Weather_parsers_survive_corrupted_responses()
    {
        const string forecast = """{"current":{"temperature_2m":18.5,"weather_code":3,"is_day":1},"daily":{"time":["2026-11-02","2026-11-03"],"weather_code":[3,61],"temperature_2m_max":[19.5,null],"temperature_2m_min":[12.25,10]}}""";
        const string geocode = """{"results":[{"name":"İstanbul","latitude":41.01,"longitude":28.95,"country":"Türkiye","admin1":"İstanbul"}]}""";
        var observed = new HashSet<string>();
        for (var i = 0; i < Iterations * 3; i++)
        {
            var seed = BaseSeed + i;
            var mutator = new Mutator(seed);
            var bad1 = mutator.Mutate(forecast);
            var bad2 = mutator.Mutate(geocode);
            Guard("WeatherService.Parse*", seed, () =>
            {
                try
                {
                    WeatherService.ParseForecast(bad1);
                }
                catch (Exception ex)
                {
                    lock (observed)
                    {
                        observed.Add("forecast:" + ex.GetType().Name);
                    }
                }

                try
                {
                    WeatherService.ParseGeocoding(bad2);
                }
                catch (Exception ex)
                {
                    lock (observed)
                    {
                        observed.Add("geocode:" + ex.GetType().Name);
                    }
                }
            });
        }

        // Çağıranlar (WeatherService.GetAsync vb.) bunları yakalıyor; burada yalnızca JSON/biçim hataları beklenir, NullReference gibi mantık hataları değil.
        Assert.DoesNotContain(observed, o => o.EndsWith("NullReferenceException") || o.EndsWith("IndexOutOfRangeException") || o.EndsWith("InvalidCastException") || o.EndsWith("OverflowException") || o.EndsWith("StackOverflowException"));
    }

    [Fact]
    public void Phone_commands_never_throw_and_never_change_fans_when_remote_control_is_off()
    {
        var settings = new SettingsService(_dir.File("settings.json"));
        settings.Load();
        settings.Current.MobileFanControl = false;
        var monitor = new HardwareMonitorService();
        using var fan = new FanControlService(monitor, settings);
        var remote = new RemoteFanService(monitor, settings, fan);

        string[] samples =
        [
            """{"cmd":"fan","id":"/lpc/x/control/0","mode":"manual","percent":60}""",
            """{"cmd":"fanAll","preset":"quiet"}""",
            """{"cmd":"profile","name":"Gaming"}""",
            """{"cmd":"fanControl","enabled":true}"""
        ];

        for (var i = 0; i < Iterations * 5; i++)
        {
            var seed = BaseSeed + i;
            var json = new Mutator(seed).Mutate(samples[i % samples.Length]);
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                Guard("RemoteFanService.Apply", seed, () => remote.Apply(doc.RootElement));
            }
        }

        Assert.False(settings.Current.FanControlEnabled);
        Assert.Empty(settings.Current.Fans);
    }

    [Fact]
    public async Task The_phone_port_survives_garbage_and_still_serves_valid_clients()
    {
        var settings = new SettingsService(_dir.File("settings.json"));
        settings.Load();
        settings.Current.MobileEnabled = true;
        settings.Current.MobileToken = "tok";
        var monitor = new HardwareMonitorService();
        using var link = new MobileLinkService(monitor, settings, port: 0, discoveryPort: -1);
        link.Apply();
        Assert.True(link.IsRunning, link.LastError);

        for (var i = 0; i < 150; i++)
        {
            var mutator = new Mutator(BaseSeed + i);
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(System.Net.IPAddress.Loopback, link.Port);
            var stream = tcp.GetStream();
            var kind = i % 3;
            var hello = Encoding.UTF8.GetBytes("{\"hello\":\"dHud\",\"token\":\"tok\"}\n");
            var payload = kind == 0
                ? mutator.MutateBytes(hello)
                : kind == 1
                    ? Encoding.UTF8.GetBytes(mutator.RandomLine(4000) + "\n")
                    : Encoding.UTF8.GetBytes(new string('a', 700) + "\n");   // 512 bayt hello sınırını aşar
            try
            {
                await stream.WriteAsync(payload);
            }
            catch (IOException)
            {
                // sunucu bağlantıyı erken kapatmış olabilir; sorun değil
            }
        }

        using var good = new System.Net.Sockets.TcpClient();
        await good.ConnectAsync(System.Net.IPAddress.Loopback, link.Port);
        await good.GetStream().WriteAsync(Encoding.UTF8.GetBytes("""{"hello":"dHud","token":"tok"}""" + "\n"));
        using var reader = new StreamReader(good.GetStream(), Encoding.UTF8);
        using var cts = new CancellationTokenSource(5000);
        var reply = await reader.ReadLineAsync(cts.Token);
        Assert.Contains("\"ok\":true", reply);
    }
}
