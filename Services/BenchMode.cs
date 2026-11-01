using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Widgets;

namespace DHud.Services;

/// <summary>
/// Gizli performans ölçüm modu: <c>dHud.exe --bench [--bench-out dir] [--bench-scale 0.5] [--soak dakika] [--bench real]</c>.
/// Gerçek başlangıç yolu (bar, tepsi, widget yöneticisi, telefon bağlantısı) çalışır; yalnızca ölçüm akışı sentetiktir, geçici veri klasörü
/// kullanılır, kullanıcının ayarlarına, başlangıç kaydına ve fanlara dokunulmaz. Çıktı: <c>bench-report.json</c> + <c>bench-report.md</c>.
/// </summary>
internal sealed class BenchMode
{
    private const string Token = "bench-token";

    private readonly App _app;
    private readonly string _outDir;
    private readonly double _scale;
    private readonly int _soakMinutes;
    private readonly bool _realHardware;
    private readonly List<ScenarioResult> _results = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string[] _args;
    private int _frames;
    private Dictionary<string, double> _extra = [];   // çalışan senaryonun ek bulguları (Measure her senaryoda yenisini kurar)
    private Timer? _publisher;
    private int _publishTick;

    private BenchMode(App app, string[] args)
    {
        _app = app;
        _args = args;
        _realHardware = UseRealHardware(args);
        _outDir = Path.GetFullPath(ArgValue(args, "--bench-out") ?? AppPaths.DataDir);
        _scale = double.TryParse(ArgValue(args, "--bench-scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0 ? scale : 1;
        _soakMinutes = int.TryParse(ArgValue(args, "--soak"), out var soak) && soak > 0 ? soak : 0;
    }

    public static bool IsRequested(string[] args) => args.Contains("--bench");

    /// <summary><c>--bench real</c>: donanım okumaları gerçek (yönetici + sürücü gerekir); varsayılan sentetik.</summary>
    public static bool UseRealHardware(string[] args)
    {
        var index = Array.IndexOf(args, "--bench");
        return index >= 0 && index + 1 < args.Length && string.Equals(args[index + 1], "real", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ölçüm ayarları: ağ/kısayol/güncelleme yok, fan kontrolü kapalı, telefon bağlantısı açık.</summary>
    public static void Prepare(AppSettings s)
    {
        s.AutoUpdateCheck = false;
        s.HotkeysEnabled = false;
        s.FanControlEnabled = false;
        s.FpsEnabled = false;
        s.MobileEnabled = true;
        s.MobileToken = Token;
    }

    public static async Task RunAsync(App app, string[] args)
    {
        var bench = new BenchMode(app, args);
        var code = 0;
        try
        {
            await bench.RunAllAsync();
        }
        catch (Exception ex)
        {
            code = 1;
            Directory.CreateDirectory(bench._outDir);
            await File.WriteAllTextAsync(Path.Combine(bench._outDir, "bench-error.txt"), ex.ToString());
        }
        finally
        {
            bench._publisher?.Dispose();
            Environment.ExitCode = code;
            app.ExitApplication();
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // ---------------------------------------------------------------- senaryolar

    private static readonly (string Name, Action<AppSettings, bool> Set)[] WidgetSwitches =
    [
        ("Saat", (s, on) => s.WidgetClock = on),
        ("Araçlar", (s, on) => s.WidgetTools = on),
        ("Kronometre", (s, on) => s.WidgetStopwatch = on),
        ("Hava durumu", (s, on) => s.WidgetWeather = on),
        ("Pano", (s, on) => s.Extra(WidgetIds.Clipboard).Enabled = on),
        ("Yapılacaklar", (s, on) => s.Extra(WidgetIds.Todo).Enabled = on),
        ("Sistem özeti", (s, on) => s.Extra(WidgetIds.SysInfo).Enabled = on),
        ("Ses", (s, on) => s.Extra(WidgetIds.Audio).Enabled = on),
        ("Takvim", (s, on) => s.Extra(WidgetIds.Calendar).Enabled = on),
        ("Gün sayacı", (s, on) => s.Extra(WidgetIds.Countdown).Enabled = on),
        ("Başlatıcı", (s, on) => s.Extra(WidgetIds.Launcher).Enabled = on)
    ];

    private TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(Math.Max(2, seconds * _scale));

    private async Task RunAllAsync()
    {
        var startupSeconds = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
        CompositionTarget.Rendering += (_, _) => Interlocked.Increment(ref _frames);
        StartPublisher(1000);
        await Pause(2000);

        // S1 — boşta: yalnızca bar
        await AllWidgetsAsync(false);
        await Measure("S1 Boşta (yalnızca bar)", "Tüm widget'lar kapalı, 1 sn'de bir ölçüm akışı.", ct => Task.Delay(Seconds(30), ct));

        // S2 — her widget tek başına
        foreach (var (name, set) in WidgetSwitches)
        {
            await AllWidgetsAsync(false);
            await Configure(s => set(s, true));
            await Measure($"S2 Widget: {name}", "Bar + yalnızca bu widget.", ct => Task.Delay(Seconds(10), ct));
        }

        await AllWidgetsAsync(false);
        EnsureNotes(1);
        await Configure(s => s.NotesVisible = true);
        await Measure("S2 Widget: Not (1 adet)", "Bar + tek yapışkan not.", ct => Task.Delay(Seconds(10), ct));

        // S3 — hepsi açık, 30 not
        await AllWidgetsAsync(true);
        EnsureNotes(WidgetService.MaxNotes);
        await Configure(s => s.NotesVisible = true);
        await Measure("S3 Her şey açık (11 widget + 30 not)", "En kötü sürekli kullanım.", ct => Task.Delay(Seconds(20), ct));

        // S4 — 250 ms yenileme + 5 telefon
        await AllWidgetsAsync(false);
        await Configure(s => { s.NotesVisible = false; s.UpdateIntervalMs = 250; });
        StartPublisher(250);
        var phones = await ConnectPhonesAsync(5);
        await Measure("S4 250 ms yenileme + 5 telefon", "Bar, hızlı ölçüm akışı, 5 telefon bağlı (JSON serileştirme + TCP).", async ct =>
        {
            await Task.Delay(Seconds(20), ct);
            _extra["phones"] = phones.Count;
        });
        foreach (var phone in phones)
        {
            phone.Dispose();
        }

        await Configure(s => s.UpdateIntervalMs = 1000);
        StartPublisher(1000);

        // S5 — sızıntı avı
        await ChurnWidgetsAsync();
        await ChurnSettingsWindowAsync();
        await ChurnProfilesAsync();

        // S6/S7 — yoğun iş yükleri (veri katmanı)
        await NoteStormAsync();
        await GridStormAsync();

        // S8 — oyun oturumu
        await GameSessionAsync();

        // S9 — uzun soak (isteğe bağlı)
        if (_soakMinutes > 0)
        {
            await SoakAsync();
        }

        await WriteReportAsync(startupSeconds);
    }

    private async Task ChurnWidgetsAsync()
    {
        var cycles = Math.Max(3, (int)(40 * _scale));
        foreach (var (name, set) in WidgetSwitches)
        {
            await AllWidgetsAsync(false);
            for (var i = 0; i < 3; i++)                         // ısınma: ilk açılışın tek seferlik maliyeti sızıntı sayılmasın
            {
                await Configure(s => set(s, true), 40);
                await Configure(s => set(s, false), 40);
            }

            var windowsBefore = Application.Current.Windows.Count;
            await Measure($"S5 Sızıntı: {name} {cycles}× aç/kapat", "Aç, 40 ms bekle, kapat. Kapanışta canlı bellek, tutamak, GDI/USER ve pencere sayısı başlangıca dönmeli.", async _ =>
            {
                for (var i = 0; i < cycles; i++)
                {
                    await Configure(s => set(s, true), 40);
                    await Configure(s => set(s, false), 40);
                }

                _extra["windowCountDelta"] = Application.Current.Windows.Count - windowsBefore;
            });
        }
    }

    private async Task ChurnSettingsWindowAsync()
    {
        var count = Math.Max(5, (int)(100 * _scale));
        var alive = new List<WeakReference>();
        for (var i = 0; i < 3; i++)
        {
            var warm = new SettingsWindow();
            warm.Show();
            await Pause(60);
            warm.Close();
        }

        await Measure($"S5 Sızıntı: Ayarlar penceresi {count}× aç/kapat", "Pencere kapandıktan sonra çöp toplama onu serbest bırakmalı (hayatta kalan pencere sayısı ~0).", async _ =>
        {
            for (var i = 0; i < count; i++)
            {
                var window = new SettingsWindow();
                alive.Add(new WeakReference(window));
                window.Show();
                await Pause(50);
                window.Close();
                await Pause(20);
            }

            await Pause(300);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            _extra["settingsWindowsStillAlive"] = alive.Count(w => w.IsAlive);
        });
    }

    private async Task ChurnProfilesAsync()
    {
        var count = Math.Max(5, (int)(100 * _scale));
        await AllWidgetsAsync(false);
        await Configure(s => s.WidgetClock = true);
        var a = _app.Profiles.Save("bench-a");
        await Configure(s => { s.WidgetClock = false; s.WidgetTools = true; s.Scale = 1.2; });
        var b = _app.Profiles.Save("bench-b");

        await Measure($"S5 Sızıntı: profil geçişi {count}×", "İki profil arasında gidip gelme (pencereler yeniden kurulur).", async _ =>
        {
            for (var i = 0; i < count; i++)
            {
                _app.ApplyProfile(ProfileService.Read(i % 2 == 0 ? a.Path : b.Path), includeWidgetData: false, includePairingKey: false);
                await Pause(40);
            }
        });

        _app.Profiles.Delete(a);
        _app.Profiles.Delete(b);
        await Configure(s => { s.Scale = 1; });
        await AllWidgetsAsync(false);
    }

    private async Task NoteStormAsync()
    {
        await AllWidgetsAsync(false);
        EnsureNotes(1);
        await Configure(s => s.NotesVisible = true);
        var note = _app.Widgets.Data.Notes[0];
        const int keystrokes = 10_000;
        await Measure($"S6 Not yazma fırtınası ({keystrokes} tuş)", "Veri katmanı: her tuşta metin değişir + kaydetme zamanlanır (disk yazması ertelenmeli).", ct =>
        {
            var rnd = new Random(1);
            for (var i = 0; i < keystrokes; i++)
            {
                note.Text = i % 1000 == 0 ? string.Empty : note.Text + (char)('a' + rnd.Next(26));
                _app.Widgets.ScheduleSave();
            }

            return Task.CompletedTask;
        });
        await Configure(s => s.NotesVisible = false);
    }

    private async Task GridStormAsync()
    {
        const int rounds = 10_000;
        await Measure($"S7 Izgara/yerleşim ({rounds} sürükleme adımı)", "WidgetGrid.FindFree, 12 engelli 1920×1080 ekranda.", ct =>
        {
            var work = new Rect(0, 0, 1920, 1040);
            var g = WidgetGrid.Make(work);
            var card = new Size(300, 190);
            var span = WidgetGrid.SpanOf(card, g);
            var rnd = new Random(2);
            var obstacles = Enumerable.Range(0, 12).Select(i => new Rect(rnd.Next(0, 1600), rnd.Next(0, 800), 300, 190)).ToList();
            var occupied = WidgetGrid.Occupied(obstacles, g);
            var found = 0;
            for (var i = 0; i < rounds; i++)
            {
                found += WidgetGrid.FindFree((rnd.Next(g.Cols), rnd.Next(g.Rows)), span, occupied, g) is null ? 0 : 1;
            }

            _extra["placed"] = found;
            return Task.CompletedTask;
        });
    }

    private async Task GameSessionAsync()
    {
        var seconds = Math.Max(5, 60 * _scale);
        await Measure($"S8 Oyun oturumu ({seconds:0} sn, 10 Hz FPS)", "GameSessionTracker'a 10 Hz örnek; oturum sonunda özet.", async ct =>
        {
            var rnd = new Random(3);
            var end = Stopwatch.StartNew();
            while (end.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested)
            {
                _app.GameTracker.Sample(60 + (float)rnd.NextDouble() * 20, 55 + (float)rnd.NextDouble() * 5, 60 + (float)rnd.NextDouble() * 5);
                await Task.Delay(100, ct);
            }

            _app.GameTracker.Sample(null, null, null);
        });
    }

    private async Task SoakAsync()
    {
        await AllWidgetsAsync(true);
        EnsureNotes(10);
        await Configure(s => { s.NotesVisible = true; s.UpdateIntervalMs = 1000; });
        var phones = await ConnectPhonesAsync(2);
        var series = new List<(double Minute, double Live, double Private, int Handles, int Gdi, int User)>();

        await Measure($"S9 Soak ({_soakMinutes} dk)", "Hepsi açık + 10 not + 2 telefon; dakikada bir canlı bellek ölçümü. Eğim = büyüme hızı.", async ct =>
        {
            for (var minute = 1; minute <= _soakMinutes && !ct.IsCancellationRequested; minute++)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                var live = GC.GetTotalMemory(true) / 1048576.0;
                var s = Take();
                series.Add((minute, live, s.PrivateMb, s.Handles, s.Gdi, s.User));
            }

            var skip = series.Count / 5;                        // ilk %20 ısınma sayılır
            var tail = series.Skip(skip).ToList();
            if (tail.Count >= 3)
            {
                _extra["liveHeapSlopeMbPerHour"] = Slope(tail.Select(t => (t.Minute, t.Live))) * 60;
                _extra["privateSlopeMbPerHour"] = Slope(tail.Select(t => (t.Minute, t.Private))) * 60;
                _extra["handlesSlopePerHour"] = Slope(tail.Select(t => (t.Minute, (double)t.Handles))) * 60;
                _extra["gdiSlopePerHour"] = Slope(tail.Select(t => (t.Minute, (double)t.Gdi))) * 60;
                _extra["userSlopePerHour"] = Slope(tail.Select(t => (t.Minute, (double)t.User))) * 60;
            }
        });
        foreach (var phone in phones)
        {
            phone.Dispose();
        }
    }

    // ---------------------------------------------------------------- yardımcılar

    private async Task AllWidgetsAsync(bool on)
    {
        await Configure(s =>
        {
            foreach (var (_, set) in WidgetSwitches)
            {
                set(s, on);
            }

            if (!on)
            {
                s.NotesVisible = false;
            }
        });
    }

    private async Task Configure(Action<AppSettings> change, int settleMs = 400)
    {
        change(_app.Settings.Current);
        _app.Settings.NotifyChanged();
        await Pause(settleMs);
    }

    private void EnsureNotes(int count)
    {
        while (_app.Widgets.Data.Notes.Count < count && _app.Widgets.AddNote() is { } note)
        {
            note.Text = new string('n', 400);
        }
    }

    private static Task Pause(int ms) => Task.Delay(ms);

    private void StartPublisher(int intervalMs)
    {
        _publisher?.Dispose();
        if (_realHardware)
        {
            return;
        }

        _publisher = new Timer(_ =>
        {
            var t = Interlocked.Increment(ref _publishTick);
            var wave = (float)Math.Sin(t / 10.0);
            _app.Monitor.PublishForTest(new SensorSnapshot(
                new HardwareReading("Bench CPU", Temperature: 55 + 8 * wave, Load: 30 + 25 * wave, ClockMhz: 4300 + 100 * wave, PowerWatts: 60 + 10 * wave, FanRpm: 1100 + 50 * wave, CoreMaxLoad: 50 + 20 * wave, CoreAvgLoad: 25 + 10 * wave),
                new HardwareReading("Bench GPU", Temperature: 60 + 6 * wave, Load: 40 + 30 * wave, MemoryUsedGb: 5 + wave, MemoryTotalGb: 10f),
                new HardwareReading("Memory", Load: 46 + 3 * wave, MemoryUsedGb: 14.7f, MemoryTotalGb: 32f),
                new NetworkReading("Ethernet", 4_300_000 + 1_000_000 * wave, 380_000, 14),
                new DiskReading("C:", 61, 568, 931)));
        }, null, 0, intervalMs);
    }

    private async Task<List<TcpClient>> ConnectPhonesAsync(int count)
    {
        var clients = new List<TcpClient>();
        for (var i = 0; i < count; i++)
        {
            var tcp = new TcpClient { NoDelay = true };
            await tcp.ConnectAsync(IPAddress.Loopback, _app.MobileLink.Port);
            await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes($"{{\"hello\":\"dHud\",\"token\":\"{Token}\"}}\n"));
            _ = Task.Run(async () =>
            {
                var buffer = new byte[16384];
                try
                {
                    while (await tcp.GetStream().ReadAsync(buffer) > 0)
                    {
                    }
                }
                catch (Exception)
                {
                    // bağlantı kapandı
                }
            });
            clients.Add(tcp);
        }

        await Pause(500);
        return clients;
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static Sample Take()
    {
        using var p = Process.GetCurrentProcess();
        return new Sample(
            WorkingSetMb: p.WorkingSet64 / 1048576.0,
            PrivateMb: p.PrivateMemorySize64 / 1048576.0,
            ManagedMb: GC.GetTotalMemory(false) / 1048576.0,
            Handles: p.HandleCount,
            Threads: p.Threads.Count,
            Gdi: (int)GetGuiResources(p.Handle, 0),
            User: (int)GetGuiResources(p.Handle, 1),
            CpuSec: p.TotalProcessorTime.TotalSeconds,
            Gen0: GC.CollectionCount(0),
            Gen1: GC.CollectionCount(1),
            Gen2: GC.CollectionCount(2),
            AllocatedMb: GC.GetTotalAllocatedBytes(false) / 1048576.0);
    }

    private static void Settle()
    {
        for (var i = 0; i < 2; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private async Task Measure(string name, string description, Func<CancellationToken, Task> workload)
    {
        _extra = [];
        Settle();
        await Pause(500);
        var liveStart = GC.GetTotalMemory(true) / 1048576.0;
        var s0 = Take();
        var framesStart = Volatile.Read(ref _frames);
        var wall = Stopwatch.StartNew();
        var samples = new List<Sample> { s0 };
        var latencies = new List<double>();
        using var cts = new CancellationTokenSource();

        var sampler = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    lock (samples)
                    {
                        samples.Add(Take());
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        var probe = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var t = Stopwatch.StartNew();
                    await _app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task;
                    lock (latencies)
                    {
                        latencies.Add(t.Elapsed.TotalMilliseconds);
                    }

                    await Task.Delay(200, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        string? failure = null;
        try
        {
            await workload(cts.Token);
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
        }

        var s1 = Take();
        var seconds = wall.Elapsed.TotalSeconds;
        var frames = Volatile.Read(ref _frames) - framesStart;
        cts.Cancel();
        await Task.WhenAll(sampler, probe);

        Settle();
        await Pause(300);
        var liveEnd = GC.GetTotalMemory(true) / 1048576.0;
        var s2 = Take();

        double[] ws;
        double privMax;
        lock (samples)
        {
            ws = samples.Select(s => s.WorkingSetMb).Append(s1.WorkingSetMb).ToArray();
            privMax = samples.Select(s => s.PrivateMb).Append(s1.PrivateMb).Max();
        }

        double[] lat;
        lock (latencies)
        {
            lat = [.. latencies];
        }

        var result = new ScenarioResult
        {
            Name = name,
            Description = description,
            Seconds = Math.Round(seconds, 1),
            WorkingSetAvgMb = Math.Round(ws.Average(), 1),
            WorkingSetMaxMb = Math.Round(ws.Max(), 1),
            PrivateStartMb = Math.Round(s0.PrivateMb, 1),
            PrivateEndMb = Math.Round(s2.PrivateMb, 1),
            PrivateMaxMb = Math.Round(privMax, 1),
            LiveHeapStartMb = Math.Round(liveStart, 2),
            LiveHeapEndMb = Math.Round(liveEnd, 2),
            CpuPercentOfOneCore = Math.Round((s1.CpuSec - s0.CpuSec) / Math.Max(seconds, 0.001) * 100, 2),
            AllocMbPerSec = Math.Round((s1.AllocatedMb - s0.AllocatedMb) / Math.Max(seconds, 0.001), 2),
            Gen0 = s1.Gen0 - s0.Gen0,
            Gen1 = s1.Gen1 - s0.Gen1,
            Gen2 = s1.Gen2 - s0.Gen2,
            HandlesDelta = s2.Handles - s0.Handles,
            ThreadsDelta = s2.Threads - s0.Threads,
            GdiDelta = s2.Gdi - s0.Gdi,
            UserDelta = s2.User - s0.User,
            HandlesEnd = s2.Handles,
            GdiEnd = s2.Gdi,
            UserEnd = s2.User,
            DispatcherLatencyP50Ms = Math.Round(Percentile(lat, 0.50), 1),
            DispatcherLatencyP99Ms = Math.Round(Percentile(lat, 0.99), 1),
            RenderFramesPerSec = Math.Round(frames / Math.Max(seconds, 0.001), 1),
            Extra = _extra,
            Failure = failure
        };
        _results.Add(result);
    }

    private static double Percentile(double[] values, double p)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        Array.Sort(values);
        return values[Math.Min(values.Length - 1, (int)Math.Ceiling(p * values.Length) - 1 < 0 ? 0 : (int)Math.Ceiling(p * values.Length) - 1)];
    }

    /// <summary>En küçük kareler doğru eğimi (y birimi / x birimi).</summary>
    private static double Slope(IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        var mx = list.Average(p => p.X);
        var my = list.Average(p => p.Y);
        var den = list.Sum(p => (p.X - mx) * (p.X - mx));
        return den == 0 ? 0 : list.Sum(p => (p.X - mx) * (p.Y - my)) / den;
    }

    // ---------------------------------------------------------------- rapor

    private async Task WriteReportAsync(double startupSeconds)
    {
        Directory.CreateDirectory(_outDir);
        var exe = Environment.ProcessPath ?? string.Empty;
        var info = new Dictionary<string, object?>
        {
            ["version"] = UpdateService.CurrentVersionText,
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["os"] = RuntimeInformation.OSDescription,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["cpuThreads"] = Environment.ProcessorCount,
            ["exeSizeMb"] = File.Exists(exe) ? Math.Round(new FileInfo(exe).Length / 1048576.0, 1) : null,
            ["startupToReadySeconds"] = Math.Round(startupSeconds, 2),
            ["hardware"] = _realHardware ? "real" : "synthetic",
            ["gcMode"] = System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
            ["scale"] = _scale,
            ["totalMinutes"] = Math.Round(_clock.Elapsed.TotalMinutes, 1),
            ["args"] = string.Join(' ', _args.Where(a => !a.StartsWith("--data-dir", StringComparison.Ordinal)))
        };

        var json = JsonSerializer.Serialize(new { info, scenarios = _results }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(_outDir, "bench-report.json"), json);
        await File.WriteAllTextAsync(Path.Combine(_outDir, "bench-report.md"), BuildMarkdown(info));
    }

    private string BuildMarkdown(Dictionary<string, object?> info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# dHud performans raporu").AppendLine();
        foreach (var (key, value) in info)
        {
            sb.AppendLine($"- **{key}**: {value}");
        }

        sb.AppendLine().AppendLine("> CPU % = tek çekirdeğin yüzdesi (100 = bir çekirdek tam dolu). Çalışma kümesi (WS) Windows'un kırpmasından etkilenir (dHud 3 dakikada bir `EmptyWorkingSet` yapar); gerçek bellek maliyeti için **Private** ve **Canlı yığın** sütunlarına bakın.").AppendLine();
        sb.AppendLine("| Senaryo | sn | WS ort MB | Private son MB | Canlı yığın MB (baş→son) | CPU % | Ayrılan MB/sn | GC 0/1/2 | Tutamak Δ | GDI Δ | USER Δ | Gecikme p50/p99 ms | Kare/sn |");
        sb.AppendLine("| --- | ---: | ---: | ---: | --- | ---: | ---: | --- | ---: | ---: | ---: | --- | ---: |");
        foreach (var r in _results)
        {
            sb.AppendLine($"| {r.Name} | {r.Seconds} | {r.WorkingSetAvgMb} | {r.PrivateEndMb} | {r.LiveHeapStartMb}→{r.LiveHeapEndMb} | {r.CpuPercentOfOneCore} | {r.AllocMbPerSec} | {r.Gen0}/{r.Gen1}/{r.Gen2} | {r.HandlesDelta} | {r.GdiDelta} | {r.UserDelta} | {r.DispatcherLatencyP50Ms}/{r.DispatcherLatencyP99Ms} | {r.RenderFramesPerSec} |");
        }

        var extras = _results.Where(r => r.Extra.Count > 0 || r.Failure is not null).ToList();
        if (extras.Count > 0)
        {
            sb.AppendLine().AppendLine("## Ek bulgular");
            foreach (var r in extras)
            {
                sb.AppendLine($"- **{r.Name}**: " + string.Join(", ", r.Extra.Select(kv => $"{kv.Key} = {kv.Value:0.##}")) + (r.Failure is null ? string.Empty : $" — HATA: {r.Failure}"));
            }
        }

        var idle = _results.FirstOrDefault(r => r.Name.StartsWith("S1", StringComparison.Ordinal));
        if (idle is not null)
        {
            sb.AppendLine().AppendLine("## Widget başına maliyet (boşta bara göre)").AppendLine();
            sb.AppendLine("| Widget | Δ Private MB | Δ CPU % | Δ GDI |").AppendLine("| --- | ---: | ---: | ---: |");
            foreach (var r in _results.Where(r => r.Name.StartsWith("S2", StringComparison.Ordinal)))
            {
                sb.AppendLine($"| {r.Name[("S2 Widget: ".Length)..]} | {r.PrivateEndMb - idle.PrivateEndMb:+0.0;-0.0;0.0} | {r.CpuPercentOfOneCore - idle.CpuPercentOfOneCore:+0.00;-0.00;0.00} | {r.GdiEnd - idle.GdiEnd:+0;-0;0} |");
            }
        }

        return sb.ToString();
    }

    private readonly record struct Sample(
        double WorkingSetMb, double PrivateMb, double ManagedMb, int Handles, int Threads, int Gdi, int User, double CpuSec, int Gen0, int Gen1, int Gen2, double AllocatedMb);

    internal sealed class ScenarioResult
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Seconds { get; set; }
        public double WorkingSetAvgMb { get; set; }
        public double WorkingSetMaxMb { get; set; }
        public double PrivateStartMb { get; set; }
        public double PrivateEndMb { get; set; }
        public double PrivateMaxMb { get; set; }
        public double LiveHeapStartMb { get; set; }
        public double LiveHeapEndMb { get; set; }
        public double CpuPercentOfOneCore { get; set; }
        public double AllocMbPerSec { get; set; }
        public int Gen0 { get; set; }
        public int Gen1 { get; set; }
        public int Gen2 { get; set; }
        public int HandlesDelta { get; set; }
        public int ThreadsDelta { get; set; }
        public int GdiDelta { get; set; }
        public int UserDelta { get; set; }
        public int HandlesEnd { get; set; }
        public int GdiEnd { get; set; }
        public int UserEnd { get; set; }
        public double DispatcherLatencyP50Ms { get; set; }
        public double DispatcherLatencyP99Ms { get; set; }
        public double RenderFramesPerSec { get; set; }
        public Dictionary<string, double> Extra { get; set; } = [];
        public string? Failure { get; set; }
    }
}
