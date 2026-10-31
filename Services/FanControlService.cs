using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace DHud.Services;

/// <summary>Arayüzde gösterilen fan durumu (arka plan thread'inde üretilir, değişmez kopya).</summary>
public sealed record FanStatus(
    string Id, string Name, bool IsGpu, bool HasRpm, float? Rpm, float? Percent, bool Controlled,
    bool TestRunning, bool TestDone, bool TestOk, float TestBefore, float TestPeak, bool Stalled);

/// <summary>
/// Fan hızlarını ayarlara göre yazılımla kontrol eder. Güvenlik önceliklidir:
/// <list type="bullet">
/// <item>Kontrol kapalıyken veya fan "Otomatik" modundayken dHud fana dokunmaz; devralınan fanlar BIOS/sürücü denetimine geri verilir.</item>
/// <item>CPU veya GPU sıcaklığı kritik eşiğe ulaşırsa dHud'un kontrol ettiği tüm fanlar %100'e çıkar.</item>
/// <item>Eğrinin sıcaklık kaynağı okunamıyorsa birkaç saniye sonra fan %100'e çıkar.</item>
/// <item>Fan yavaşlarken hızlı değil kademeli iner (sürekli inip çıkmayı önler).</item>
/// <item>Kontrol edilen bir fan dönmeyi bırakırsa (0 RPM) uyarı verilir.</item>
/// <item>Uygulama kapanırken veya çökerken <see cref="ReleaseAll"/> fanları otomatiğe geri verir.</item>
/// </list>
/// </summary>
public sealed class FanControlService : IDisposable
{
    /// <summary>Sıcaklık kaynağı bu kadar ardışık ölçüm okunamazsa fan %100'e çıkar.</summary>
    public const int MissingTempLimit = 5;

    /// <summary>Fan en fazla bu kadar yüzde/saniye yavaşlar; hızlanırken sınır yok.</summary>
    public const double MaxRampDownPerTick = 4;

    /// <summary>Fan testi süresi (saniye): fan bu kadar %100'de çalışır.</summary>
    public const int TestSeconds = 8;

    /// <summary>Fan bu kadar ardışık ölçüm 0 RPM okunursa (komut ≥ %30 iken) uyarı verilir.</summary>
    public const int StallTicks = 10;

    private const double StallMinPercent = 30;
    private const float SpinningRpm = 50;

    private sealed class State
    {
        public double? Last;
        public int Missing;
        public int TicksSinceSend;
        public bool Taken;

        public bool TestRequested;
        public DateTime? TestEnd;
        public bool TestDone;
        public bool TestOk;
        public float TestBefore;
        public float TestBeforePercent;
        public float TestPeak;

        public bool SeenSpinning;
        public int ZeroTicks;
        public bool Stalled;
    }

    private readonly HardwareMonitorService _monitor;
    private readonly SettingsService _settings;
    private readonly Dictionary<string, State> _states = [];
    private readonly object _gate = new();
    private volatile IReadOnlyList<FanStatus> _status = [];
    private volatile bool _emergency;
    private bool _disposed;

    public FanControlService(HardwareMonitorService monitor, SettingsService settings)
    {
        _monitor = monitor;
        _settings = settings;
        _monitor.SnapshotUpdated += OnSnapshot;
    }

    public IReadOnlyList<FanStatus> Status => _status;

    /// <summary>Sıcaklık kritik eşikte olduğu için fanlar zorla %100'de mi.</summary>
    public bool Emergency => _emergency;

    /// <summary>Kontrol edilen bir fan dönmüyor: fan adı ve ayarlanan yüzde. DİKKAT: arka plan thread'inden çağrılır.</summary>
    public event Action<string, int>? FanStalled;

    /// <summary>Bir fanı <see cref="TestSeconds"/> saniye %100'de çalıştırıp devrin artıp artmadığına bakar; sonra eski haline döner.</summary>
    public void StartTest(string id)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(id, out var state))
            {
                _states[id] = state = new State();
            }

            state.TestRequested = true;
            state.TestDone = false;
        }
    }

    /// <summary>Tüm fanların testini aynı anda başlatır (her biri <see cref="TestSeconds"/> saniye %100).</summary>
    public void StartTestAll()
    {
        lock (_gate)
        {
            foreach (var channel in _monitor.FanChannels)
            {
                if (!_states.TryGetValue(channel.Id, out var state))
                {
                    _states[channel.Id] = state = new State();
                }

                state.TestRequested = true;
                state.TestDone = false;
            }
        }
    }

    private void OnSnapshot(SensorSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Apply(snapshot);
            }
            catch (Exception ex)
            {
                // Bir şey ters giderse fanları BIOS denetimine geri ver.
                Debug.WriteLine($"Fan kontrolü hatası: {ex}");
                ReleaseLocked();
            }
        }
    }

    private void Apply(SensorSnapshot snapshot)
    {
        var settings = _settings.Current;
        var enabled = settings.FanControlEnabled;
        var now = DateTime.UtcNow;

        var cpuTemp = ValidTemp(snapshot.Cpu?.Temperature);
        var gpuTemp = ValidTemp(snapshot.Gpu?.Temperature);
        var emergency = enabled
                        && ((cpuTemp >= settings.CpuHotTemperature) || (gpuTemp >= settings.GpuHotTemperature));
        _emergency = emergency;

        var statuses = new List<FanStatus>();
        foreach (var channel in _monitor.FanChannels)
        {
            var control = channel.Control.Control;
            if (control is null)
            {
                continue;
            }

            if (!_states.TryGetValue(channel.Id, out var state))
            {
                _states[channel.Id] = state = new State();
            }

            var rpm = channel.Rpm?.Value;

            // ---- Fan testi ----
            if (state.TestRequested)
            {
                state.TestRequested = false;
                state.TestEnd = now.AddSeconds(TestSeconds);
                state.TestBefore = rpm ?? 0;
                state.TestBeforePercent = channel.Control.Value ?? 0;
                state.TestPeak = 0;
            }

            var testing = state.TestEnd is DateTime end && now < end;
            if (state.TestEnd is not null && !testing)
            {
                state.TestEnd = null;
                state.TestDone = true;
                state.TestOk = TestPassed(state.TestBefore, state.TestPeak, state.TestBeforePercent);
            }

            settings.Fans.TryGetValue(channel.Id, out var cfg);
            var mode = enabled ? cfg?.Mode ?? FanMode.Auto : FanMode.Auto;

            if (testing)
            {
                state.TestPeak = Math.Max(state.TestPeak, rpm ?? 0);
                Write(control, state, 100);
            }
            else if (mode == FanMode.Auto || cfg is null)
            {
                if (state.Taken)
                {
                    control.SetDefault();
                    state.Taken = false;
                    state.Last = null;
                }
            }
            else
            {
                var source = SourceTemp(cfg.Source, cpuTemp, gpuTemp);
                state.Missing = source is null ? state.Missing + 1 : 0;

                var target = ComputeTarget(cfg, source, state.Missing, emergency, state.Last);
                Write(control, state, target);
            }

            DetectStall(channel, state, rpm, testing);

            statuses.Add(new FanStatus(channel.Id, channel.Name, channel.IsGpu, channel.Rpm is not null, rpm,
                channel.Control.Value, state.Taken, testing, state.TestDone, state.TestOk,
                state.TestBefore, state.TestPeak, state.Stalled));
        }

        _status = statuses;
    }

    /// <summary>Hedef yüzdeyi donanımın sınırlarına çekip yazar. Değişiklik varsa hemen, yoksa yaklaşık 10 saniyede bir yeniden yazar.</summary>
    private static void Write(IControl control, State state, double target)
    {
        target = Math.Clamp(target, control.MinSoftwareValue, control.MaxSoftwareValue);

        state.TicksSinceSend++;
        if (state.Last is null || Math.Abs(target - state.Last.Value) >= 1 || state.TicksSinceSend >= 10)
        {
            control.SetSoftware((float)target);
            state.Last = target;
            state.TicksSinceSend = 0;
        }

        state.Taken = true;
    }

    /// <summary>
    /// dHud'un yüzde yazdığı bir fan daha önce döndüğü halde 0 RPM okunuyorsa uyarır. Hiç dönmemiş fanlar
    /// (devir sensörü bazı fanlarda hep 0 okur) uyarı üretmez.
    /// </summary>
    private void DetectStall(FanChannel channel, State state, float? rpm, bool testing)
    {
        if (rpm is not float value || testing || !state.Taken)
        {
            state.ZeroTicks = 0;
            return;
        }

        if (value > SpinningRpm)
        {
            state.SeenSpinning = true;
            state.ZeroTicks = 0;
            state.Stalled = false;
        }
        else if (state.SeenSpinning && state.Last >= StallMinPercent)
        {
            state.ZeroTicks++;
            if (state.ZeroTicks >= StallTicks && !state.Stalled)
            {
                state.Stalled = true;
                FanStalled?.Invoke(channel.Name, (int)Math.Round(state.Last.Value));
            }
        }
    }

    /// <summary>Test geçti mi: devir en az 100 RPM arttı; ya da fan zaten (≥ %90) en yüksekteydi ve dönüyor.</summary>
    public static bool TestPassed(float rpmBefore, float rpmPeak, float percentBefore) =>
        rpmPeak - rpmBefore >= 100 || (percentBefore >= 90 && rpmPeak > 200);

    /// <summary>Eğrinin sıcaklık kaynağı: CPU, GPU veya ikisinin yüksek olanı (biri okunamıyorsa diğeri).</summary>
    public static double? SourceTemp(FanSource source, double? cpuTemp, double? gpuTemp) => source switch
    {
        FanSource.Cpu => cpuTemp,
        FanSource.Gpu => gpuTemp,
        _ => cpuTemp is double c && gpuTemp is double g ? Math.Max(c, g) : cpuTemp ?? gpuTemp
    };

    /// <summary>
    /// Bir fan için hedef yüzdeyi hesaplar (donanıma dokunmaz). Sıra: acil durum → manuel/eğri → alt sınır → kademeli yavaşlama.
    /// </summary>
    /// <param name="sourceTemp">Eğrinin sıcaklık kaynağı; okunamıyorsa null.</param>
    /// <param name="missingTicks">Kaynak sıcaklık kaç ölçümdür okunamıyor.</param>
    /// <param name="last">Bu fana en son yazılan yüzde (yoksa null).</param>
    public static double ComputeTarget(FanSetting cfg, double? sourceTemp, int missingTicks, bool emergency, double? last)
    {
        if (emergency)
        {
            return 100;
        }

        double target;
        if (cfg.Mode == FanMode.Manual)
        {
            target = cfg.ManualPercent;
        }
        else if (sourceTemp is double temp)
        {
            target = FanCurve.Interpolate(cfg.Curve, temp);
        }
        else
        {
            // Sıcaklık okunamıyor: önce son değeri koru, uzun sürerse güvenli tarafa (%100) geç.
            target = missingTicks >= MissingTempLimit ? 100 : last ?? 100;
        }

        target = Math.Max(target, cfg.MinPercent);

        if (last is double previous && target < previous)
        {
            target = Math.Max(target, previous - MaxRampDownPerTick);
        }

        return target;
    }

    /// <summary>Sıfır veya negatif değerler okunamayan sensör sayılır (yönetici yetkisi yok vb.).</summary>
    private static float? ValidTemp(float? t) => t is > 0 ? t : null;

    /// <summary>Devralınan tüm fanları BIOS/sürücü denetimine geri verir. Kapanışta ve çökmede çağrılır.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            ReleaseLocked();
        }
    }

    private void ReleaseLocked()
    {
        foreach (var channel in _monitor.FanChannels)
        {
            if (_states.TryGetValue(channel.Id, out var state) && state.Taken)
            {
                try
                {
                    channel.Control.Control?.SetDefault();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fan serbest bırakılamadı ({channel.Name}): {ex.Message}");
                }

                state.Taken = false;
                state.Last = null;
                state.TestEnd = null;
            }
        }
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshot;
        lock (_gate)
        {
            ReleaseLocked();
            _disposed = true;
        }
    }
}
