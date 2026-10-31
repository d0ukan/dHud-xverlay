using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace DHud.Services;

/// <summary>Arayüzde gösterilen fan durumu (arka plan thread'inde üretilir, değişmez kopya).</summary>
public sealed record FanStatus(string Id, string Name, bool IsGpu, float? Rpm, float? Percent, bool Controlled);

/// <summary>
/// Fan hızlarını ayarlara göre yazılımla kontrol eder. Güvenlik önceliklidir:
/// <list type="bullet">
/// <item>Kontrol kapalıyken veya fan "Otomatik" modundayken dHud fana dokunmaz; devralınan fanlar BIOS/sürücü denetimine geri verilir.</item>
/// <item>CPU veya GPU sıcaklığı kritik eşiğe ulaşırsa dHud'un kontrol ettiği tüm fanlar %100'e çıkar.</item>
/// <item>Eğrinin sıcaklık kaynağı okunamıyorsa birkaç saniye sonra fan %100'e çıkar.</item>
/// <item>Fan yavaşlarken hızlı değil kademeli iner (sürekli inip çıkmayı önler).</item>
/// <item>Uygulama kapanırken veya çökerken <see cref="ReleaseAll"/> fanları otomatiğe geri verir.</item>
/// </list>
/// </summary>
public sealed class FanControlService : IDisposable
{
    /// <summary>Sıcaklık kaynağı bu kadar ardışık ölçüm okunamazsa fan %100'e çıkar.</summary>
    public const int MissingTempLimit = 5;

    /// <summary>Fan en fazla bu kadar yüzde/saniye yavaşlar; hızlanırken sınır yok.</summary>
    public const double MaxRampDownPerTick = 4;

    private sealed class State
    {
        public double? Last;
        public int Missing;
        public int TicksSinceSend;
        public bool Taken;
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

            settings.Fans.TryGetValue(channel.Id, out var cfg);
            var mode = enabled ? cfg?.Mode ?? FanMode.Auto : FanMode.Auto;

            if (mode == FanMode.Auto || cfg is null)
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
                var source = cfg.Source == FanSource.Gpu ? gpuTemp : cpuTemp;
                state.Missing = source is null ? state.Missing + 1 : 0;

                var target = ComputeTarget(cfg, source, state.Missing, emergency, state.Last);
                target = Math.Clamp(target, control.MinSoftwareValue, control.MaxSoftwareValue);

                // Değişiklik varsa hemen, yoksa yaklaşık 10 saniyede bir yeniden yaz (sürücü kendiliğinden bırakmasın).
                state.TicksSinceSend++;
                if (state.Last is null || Math.Abs(target - state.Last.Value) >= 1 || state.TicksSinceSend >= 10)
                {
                    control.SetSoftware((float)target);
                    state.Last = target;
                    state.TicksSinceSend = 0;
                }

                state.Taken = true;
            }

            statuses.Add(new FanStatus(channel.Id, channel.Name, channel.IsGpu, channel.Rpm?.Value, channel.Control.Value, state.Taken));
        }

        _status = statuses;
    }

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
