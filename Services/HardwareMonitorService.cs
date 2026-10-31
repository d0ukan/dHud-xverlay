using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace DHud.Services;

/// <summary>Tek bir donanımın (CPU, GPU veya RAM) okunan değerleri. Okunamayan değerler null'dır.</summary>
public sealed record HardwareReading(
    string Name,
    float? Temperature = null,
    float? Load = null,
    float? ClockMhz = null,
    float? PowerWatts = null,
    float? FanRpm = null,
    float? MemoryUsedGb = null,
    float? MemoryTotalGb = null);

/// <summary>Yazılımla kontrol edilebilen bir fan kanalı (anakart başlığı veya GPU fanı).</summary>
public sealed record FanChannel(string Id, string Name, bool IsGpu, ISensor Control, ISensor? Rpm);

public sealed record SensorSnapshot(
    HardwareReading? Cpu,
    HardwareReading? Gpu,
    HardwareReading? Memory,
    NetworkReading? Network = null);

/// <summary>
/// LibreHardwareMonitor ile CPU, GPU ve RAM sensörlerini arka plan thread'inde periyodik olarak okur.
/// Okuma UI thread'ini bloklamaz; sonuçlar <see cref="SnapshotUpdated"/> olayıyla (arka plan thread'inden) yayınlanır.
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    // Üreticiye göre "asıl" CPU sıcaklığını veren sensör adları, öncelik sırasıyla.
    private static readonly string[] CpuTemperatureNames =
    [
        "CPU Package",       // Intel
        "Core (Tctl/Tdie)",  // AMD Ryzen
        "Core (Tdie)",
        "Core (Tctl)",
        "Tctl",
        "Package",
        "Core Average",
        "Core Max",
        "CPU Cores"
    ];

    private static readonly string[] CpuLoadNames = ["CPU Total"];
    private static readonly string[] CpuPowerNames = ["CPU Package", "Package"];

    private static readonly string[] GpuTemperatureNames = ["GPU Core", "GPU Hot Spot"];
    private static readonly string[] GpuLoadNames = ["GPU Core", "D3D 3D"];
    private static readonly string[] GpuClockNames = ["GPU Core"];
    private static readonly string[] GpuPowerNames = ["GPU Package", "GPU Power", "GPU Core"];

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true
    };

    private readonly CancellationTokenSource _cts = new();

    /// <summary>Okunacak donanımlar. Kapalı olan donanım taranmaz ve okunmaz.</summary>
    public volatile bool CpuEnabled = true;
    public volatile bool GpuEnabled = true;

    /// <summary>Fan hızları. Anakart (SuperIO) taraması yalnızca açıkken yapılır; yönetici yetkisi gerekir.</summary>
    public volatile bool FanEnabled;

    private PeriodicTimer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private Task? _loop;
    private IHardware? _cpu;
    private IHardware? _gpu;
    private IHardware? _memory;
    private IHardware? _motherboard;
    private bool _opened;
    private volatile IReadOnlyList<FanChannel> _fanChannels = [];
    private volatile bool _fanDirty = true;

    /// <summary>Kontrol edilebilir fanlar. Anakart taraması açıkken (<see cref="FanEnabled"/>) ve ilk ölçümden sonra dolar.</summary>
    public IReadOnlyList<FanChannel> FanChannels => _fanChannels;

    /// <summary>Ağ hızı ve ping ölçümü; sensör okumalarıyla aynı döngüde örneklenir.</summary>
    public NetworkMonitor Network { get; } = new();

    /// <summary>Her okuma sonrası tetiklenir. DİKKAT: arka plan thread'inden çağrılır.</summary>
    public event Action<SensorSnapshot>? SnapshotUpdated;

    public SensorSnapshot? Latest { get; private set; }

    /// <summary>Okuma aralığı; çalışırken de değiştirilebilir.</summary>
    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            _interval = value;
            if (_timer is not null)
            {
                _timer.Period = value;
            }
        }
    }

    public void Start(TimeSpan interval)
    {
        _interval = interval;
        Network.Start();
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // Open() sürücü yükleme ve donanım taraması yaptığı için 1-2 sn sürebilir.
            _computer.IsCpuEnabled = CpuEnabled;
            _computer.IsGpuEnabled = GpuEnabled;
            _computer.IsMotherboardEnabled = FanEnabled;
            _computer.Open();
            _opened = true;
            _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            _gpu = SelectGpu(_computer.Hardware);
            _motherboard = FanEnabled ? FindMotherboard() : null;
            // Bellek modülleri (DIMM) de "Memory" tipinde olabilir; toplam kullanımı veren donanımı seç.
            _memory = _computer.Hardware.FirstOrDefault(h =>
                h.HardwareType == HardwareType.Memory && h.Sensors.Any(s => s.Name == "Memory Used"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LibreHardwareMonitor açılamadı: {ex}");
        }

        Publish();

        _timer = new PeriodicTimer(_interval);
        try
        {
            while (await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                Publish();
            }
        }
        catch (OperationCanceledException)
        {
            // Kapanış.
        }
        finally
        {
            _timer.Dispose();
        }
    }

    private void Publish()
    {
        SensorSnapshot snapshot;
        try
        {
            SyncEnabledHardware();
            UpdateHardware(_cpu);
            UpdateHardware(_gpu);
            UpdateHardware(_memory);
            UpdateHardware(_motherboard);
            if (_fanDirty)
            {
                DiscoverFans();
                _fanDirty = false;
            }


            snapshot = new SensorSnapshot(
                _cpu is null ? null : ReadCpu(_cpu, _motherboard),
                _gpu is null ? null : ReadGpu(_gpu),
                _memory is null ? null : ReadMemory(_memory));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Sensör okuma hatası: {ex}");
            snapshot = new SensorSnapshot(null, null, null);
        }

        try
        {
            snapshot = snapshot with { Network = Network.Sample() };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ağ okuma hatası: {ex}");
        }

        Latest = snapshot;
        SnapshotUpdated?.Invoke(snapshot);
    }

    /// <summary>
    /// Ayardan CPU/GPU açılıp kapatıldıysa ilgili donanım grubunu çalışırken ekler/kaldırır.
    /// Okuma döngüsü ile aynı iş parçacığında çalışır, bu yüzden Update çağrılarıyla çakışmaz.
    /// </summary>
    private void SyncEnabledHardware()
    {
        if (!_opened)
        {
            return;
        }

        if (_computer.IsCpuEnabled != CpuEnabled)
        {
            _computer.IsCpuEnabled = CpuEnabled;
            _cpu = CpuEnabled ? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu) : null;
        }

        if (_computer.IsGpuEnabled != GpuEnabled)
        {
            _computer.IsGpuEnabled = GpuEnabled;
            _gpu = GpuEnabled ? SelectGpu(_computer.Hardware) : null;
            _fanDirty = true;
        }

        if (_computer.IsMotherboardEnabled != FanEnabled)
        {
            _computer.IsMotherboardEnabled = FanEnabled;
            _motherboard = FanEnabled ? FindMotherboard() : null;
            _fanDirty = true;
        }
    }

    /// <summary>Kontrol sensörü olan (yazılımla ayarlanabilen) tüm fanları listeler ve devir sensörleriyle eşler.</summary>
    private void DiscoverFans()
    {
        var channels = new List<FanChannel>();
        foreach (var hardware in _computer.Hardware)
        {
            Walk(hardware);
        }

        _fanChannels = channels;

        void Walk(IHardware h)
        {
            var isGpu = h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
            foreach (var sensor in h.Sensors)
            {
                if (sensor.SensorType == SensorType.Control && sensor.Control is not null)
                {
                    var rpm = h.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Name == sensor.Name);
                    channels.Add(new FanChannel(sensor.Identifier.ToString(), sensor.Name, isGpu, sensor, rpm));
                }
            }

            foreach (var sub in h.SubHardware)
            {
                Walk(sub);
            }
        }
    }

    private IHardware? FindMotherboard() =>
        _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);

    private static void UpdateHardware(IHardware? hardware)
    {
        if (hardware is null)
        {
            return;
        }

        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
            sub.Update();
        }
    }

    private static HardwareReading ReadCpu(IHardware cpu, IHardware? motherboard)
    {
        var sensors = AllSensors(cpu);

        // Çekirdek frekanslarının ortalaması ("Core #1", "P-Core #1", "E-Core #1" ...).
        var coreClocks = sensors
            .Where(s => s.SensorType == SensorType.Clock
                        && s.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase)
                        && s.Value is > 0)
            .Select(s => s.Value!.Value)
            .ToList();

        return new HardwareReading(
            cpu.Name,
            Temperature: PickTemperature(sensors, CpuTemperatureNames),
            Load: PickValue(sensors, SensorType.Load, CpuLoadNames),
            ClockMhz: coreClocks.Count > 0 ? coreClocks.Average() : null,
            PowerWatts: PickValue(sensors, SensorType.Power, CpuPowerNames),
            FanRpm: motherboard is null ? null : PickCpuFan(AllSensors(motherboard)));
    }

    private static HardwareReading ReadGpu(IHardware gpu)
    {
        var sensors = AllSensors(gpu);

        // VRAM: NVIDIA/AMD "GPU Memory Used/Total" (MB) verir; yoksa D3D sayaçlarına düş.
        var usedMb = PickValue(sensors, SensorType.SmallData, ["GPU Memory Used", "D3D Dedicated Memory Used"]);
        var totalMb = PickValue(sensors, SensorType.SmallData, ["GPU Memory Total"]);

        return new HardwareReading(
            gpu.Name,
            Temperature: PickTemperature(sensors, GpuTemperatureNames),
            Load: PickValue(sensors, SensorType.Load, GpuLoadNames),
            ClockMhz: PickValue(sensors, SensorType.Clock, GpuClockNames),
            PowerWatts: PickValue(sensors, SensorType.Power, GpuPowerNames, fallbackToFirst: true),
            FanRpm: sensors.Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue).Max(s => s.Value),
            MemoryUsedGb: usedMb / 1024f,
            MemoryTotalGb: totalMb / 1024f);
    }

    /// <summary>"CPU Fan" başlığı; yoksa pompa okunur (sıvı soğutmada CPU fanı genelde yoktur).</summary>
    private static float? PickCpuFan(IEnumerable<ISensor> sensors) =>
        PickValue(sensors, SensorType.Fan, ["CPU Fan", "CPU Fan #1", "Pump Fan", "AIO Pump"]);

    private static HardwareReading ReadMemory(IHardware memory)
    {
        var sensors = AllSensors(memory);
        var used = PickValue(sensors, SensorType.Data, ["Memory Used"]);
        var available = PickValue(sensors, SensorType.Data, ["Memory Available"]);

        return new HardwareReading(
            memory.Name,
            Load: PickValue(sensors, SensorType.Load, ["Memory"]),
            MemoryUsedGb: used,
            MemoryTotalGb: used + available);
    }

    private static List<ISensor> AllSensors(IHardware hardware) =>
        hardware.Sensors.Concat(hardware.SubHardware.SelectMany(s => s.Sensors)).ToList();

    private static float? PickTemperature(IEnumerable<ISensor> sensors, string[] preferredNames) =>
        PickValue(
            // Intel'in "Distance to TjMax" sensörleri gerçek sıcaklık değil.
            sensors.Where(s => !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)),
            SensorType.Temperature,
            preferredNames,
            fallbackToFirst: true);

    /// <summary>
    /// Verilen tipteki sensörlerde önce isim listesindeki sırayla değer taşıyan birini arar,
    /// bulamazsa (istenirse) değeri sıfırdan büyük ilk sensöre düşer.
    /// </summary>
    private static float? PickValue(IEnumerable<ISensor> sensors, SensorType type, string[] preferredNames,
        bool fallbackToFirst = false)
    {
        var candidates = sensors.Where(s => s.SensorType == type).ToList();

        foreach (var name in preferredNames)
        {
            var match = candidates.FirstOrDefault(s =>
                s.Value.HasValue && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Value;
            }
        }

        return fallbackToFirst ? candidates.FirstOrDefault(s => s.Value is > 0)?.Value : null;
    }

    /// <summary>
    /// Birden fazla GPU varsa (ör. dahili + harici) harici olanı tercih eder:
    /// NVIDIA &gt; AMD &gt; Intel, ayrıca kendi belleği (dedicated memory) olan GPU'ya ek puan.
    /// </summary>
    private static IHardware? SelectGpu(IEnumerable<IHardware> hardware)
    {
        return hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .OrderByDescending(Score)
            .FirstOrDefault();

        static int Score(IHardware gpu)
        {
            var score = gpu.HardwareType switch
            {
                HardwareType.GpuNvidia => 4,
                HardwareType.GpuAmd => 2,
                _ => 0
            };

            if (gpu.Sensors.Any(s => s.Name.Contains("Dedicated", StringComparison.OrdinalIgnoreCase)
                                     || s.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }

            return score;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Döngü zaten iptal edildi.
        }

        if (_opened)
        {
            try
            {
                // Sürücüyü ve donanım tutamaçlarını düzgünce serbest bırak.
                _computer.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibreHardwareMonitor kapatılamadı: {ex}");
            }
        }

        Network.Dispose();
        _cts.Dispose();
    }
}
