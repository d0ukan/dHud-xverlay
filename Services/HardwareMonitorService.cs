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
    float? MemoryUsedGb = null,
    float? MemoryTotalGb = null);

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
    private PeriodicTimer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private Task? _loop;
    private IHardware? _cpu;
    private IHardware? _gpu;
    private IHardware? _memory;
    private bool _opened;

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
            _computer.Open();
            _opened = true;
            _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            _gpu = SelectGpu(_computer.Hardware);
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
            UpdateHardware(_cpu);
            UpdateHardware(_gpu);
            UpdateHardware(_memory);

            snapshot = new SensorSnapshot(
                _cpu is null ? null : ReadCpu(_cpu),
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

    private static HardwareReading ReadCpu(IHardware cpu)
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
            PowerWatts: PickValue(sensors, SensorType.Power, CpuPowerNames));
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
            MemoryUsedGb: usedMb / 1024f,
            MemoryTotalGb: totalMb / 1024f);
    }

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
