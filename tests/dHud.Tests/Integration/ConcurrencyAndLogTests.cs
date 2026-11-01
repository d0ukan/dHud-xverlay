using DHud.Helpers;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>Ölçüm iş parçacığı ile arayüz iş parçacığı aynı anda çalışır; yarış durumları istisna veya bozuk durum üretmemeli.</summary>
[Trait(Cat.Name, Cat.Integration)]
public class ConcurrencyTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Remote_fan_state_can_be_built_while_settings_change_and_commands_apply()
    {
        var settings = new SettingsService(_dir.File("settings.json"));
        settings.Load();
        settings.Current.MobileFanControl = true;
        var monitor = new HardwareMonitorService();
        using var fan = new FanControlService(monitor, settings);
        var remote = new RemoteFanService(monitor, settings, fan);

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var errors = new List<Exception>();

        Task Loop(Action action) => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    lock (errors)
                    {
                        errors.Add(ex);
                    }
                }
            }
        });

        var toggle = false;
        var tasks = new[]
        {
            Loop(() => remote.BuildState()),                                   // ölçüm iş parçacığı
            Loop(() => remote.BuildState()),
            Loop(() =>                                                         // arayüz iş parçacığı
            {
                toggle = !toggle;
                using var doc = System.Text.Json.JsonDocument.Parse($"{{\"cmd\":\"fanControl\",\"enabled\":{(toggle ? "true" : "false")}}}");
                remote.Apply(doc.RootElement);
            }),
            Loop(() => settings.Current.Scale = settings.Current.Scale > 1 ? 0.9 : 1.1)   // tek değerli alan: listeyi başka iş parçacığından değiştiren gerçek bir yazar yok
        };
        Task.WaitAll(tasks);

        // Liste değiştirilirken okuma "koleksiyon değişti" fırlatabilir; BuildState bunu yutmalı, hiçbiri dışarı sızmamalı.
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(2).Select(e => e.ToString())));
    }

    [Fact]
    public void Snapshots_can_flow_while_the_phone_link_starts_and_stops()
    {
        var settings = new SettingsService(_dir.File("settings.json"));
        settings.Load();
        settings.Current.MobileToken = "t";
        var monitor = new HardwareMonitorService();
        using var link = new MobileLinkService(monitor, settings, port: 0, discoveryPort: -1);

        var snapshot = new SensorSnapshot(new HardwareReading("CPU", Temperature: 50), null, null);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var publisher = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                monitor.PublishForTest(snapshot);
            }
        });

        var error = Record.Exception(() =>
        {
            for (var i = 0; i < 30; i++)
            {
                settings.Current.MobileEnabled = true;
                link.Apply();
                settings.Current.MobileEnabled = false;
                link.Apply();
            }
        });
        publisher.Wait();
        Assert.Null(error);
    }
}

[Trait(Cat.Name, Cat.Integration)]
public class DiagLogTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Entries_are_kept_in_memory_and_written_to_the_log_file()
    {
        DiagLog.Add("test", "merhaba");
        Assert.Contains(DiagLog.Snapshot(), l => l.Contains("[test] merhaba"));
        Assert.Contains("[test] merhaba", File.ReadAllText(_dir.File("dhud.log")));
    }

    [Fact]
    public void Line_breaks_in_messages_cannot_forge_extra_log_lines()
    {
        DiagLog.Add("x", "satır1\r\nSAHTE [crash] satır");
        Assert.DoesNotContain(File.ReadAllLines(_dir.File("dhud.log")), l => l.StartsWith("SAHTE"));
    }

    [Fact]
    public void Many_threads_can_log_at_once_and_memory_stays_bounded()
    {
        Parallel.For(0, 2000, i => DiagLog.Add("paralel", $"kayıt {i}"));
        Assert.True(DiagLog.Snapshot().Count <= 60);
    }

    [Fact]
    public void The_log_file_is_rotated_so_it_never_grows_without_bound()
    {
        var big = new string('a', 1000);
        for (var i = 0; i < 700; i++)
        {
            DiagLog.Add("dolgu", big);
        }

        Assert.True(new FileInfo(_dir.File("dhud.log")).Length < 700 * 1000, "günlük döndürülmeli");
        Assert.True(File.Exists(_dir.File("dhud.log.old")));
    }

    [Fact]
    public void An_unwritable_log_location_never_throws()
    {
        var previous = AppPaths.DataDir;
        try
        {
            AppPaths.DataDir = @"Z:\olmayan\surucu\dHud";
            Assert.Null(Record.Exception(() => DiagLog.Add("x", "yazılamaz")));
        }
        finally
        {
            AppPaths.DataDir = previous;
        }
    }
}
