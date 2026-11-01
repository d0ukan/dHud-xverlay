using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace DHud.Services;

/// <summary>Bir işlemin bir andaki sayaçları (yalnızca birikimli değerler; hız iki örneğin farkından çıkar).</summary>
public readonly record struct ProcSample(int Pid, string Name, long CpuTime100ns, long IoBytes, long MemoryBytes);

/// <summary>Aynı adlı işlemlerin (ör. tarayıcının tüm alt işlemleri) toplamı; hız değerleri son örnekleme aralığına aittir.</summary>
public sealed record ProcRow(string Name, int Count, double CpuPercent, long MemoryBytes, double DiskBytesPerSecond, double NetBytesPerSecond);

public enum ProcSort
{
    Cpu,
    Memory,
    Disk,
    Network
}

/// <summary>Görev yöneticisi widget'ının hesapları (saf mantık; test edilebilir).</summary>
public static class ProcessTable
{
    /// <summary>
    /// İki örnekten adlara göre toplanmış satırlar üretir. CPU yüzdesi tüm çekirdeklere bölünür (Görev Yöneticisi gibi: %100 = tüm CPU).
    /// Önceki örnekte olmayan (yeni açılmış) işlemin hızı 0 sayılır; sayacı geri giden (PID yeniden kullanılmış) işlem de 0 sayılır.
    /// </summary>
    public static List<ProcRow> Compute(IReadOnlyCollection<ProcSample> previous, IReadOnlyCollection<ProcSample> current, double seconds,
        int cores, IReadOnlyDictionary<int, long>? netBytesByPid = null)
    {
        seconds = Math.Max(seconds, 0.001);
        cores = Math.Max(cores, 1);
        var before = new Dictionary<int, ProcSample>(previous.Count);
        foreach (var sample in previous)
        {
            before[sample.Pid] = sample;
        }

        var groups = new Dictionary<string, (int Count, double Cpu, long Memory, double Disk, double Net)>(StringComparer.OrdinalIgnoreCase);
        foreach (var now in current)
        {
            double cpu = 0, disk = 0;
            if (before.TryGetValue(now.Pid, out var old) && string.Equals(old.Name, now.Name, StringComparison.OrdinalIgnoreCase))
            {
                var cpuDelta = now.CpuTime100ns - old.CpuTime100ns;
                if (cpuDelta > 0)
                {
                    cpu = cpuDelta / (seconds * 10_000_000d * cores) * 100;
                }

                var ioDelta = now.IoBytes - old.IoBytes;
                if (ioDelta > 0)
                {
                    disk = ioDelta / seconds;
                }
            }

            var net = netBytesByPid is not null && netBytesByPid.TryGetValue(now.Pid, out var bytes) ? bytes / seconds : 0;
            groups.TryGetValue(now.Name, out var group);
            groups[now.Name] = (group.Count + 1, group.Cpu + cpu, group.Memory + now.MemoryBytes, group.Disk + disk, group.Net + net);
        }

        return groups.Select(g => new ProcRow(g.Key, g.Value.Count, Math.Min(g.Value.Cpu, 100), g.Value.Memory, g.Value.Disk, g.Value.Net)).ToList();
    }

    /// <summary>Seçilen sütuna göre azalan sırada en çok kaynak yiyen <paramref name="count"/> satır.</summary>
    public static List<ProcRow> Top(IEnumerable<ProcRow> rows, ProcSort sort, int count)
    {
        IOrderedEnumerable<ProcRow> ordered = sort switch
        {
            ProcSort.Memory => rows.OrderByDescending(r => r.MemoryBytes),
            ProcSort.Disk => rows.OrderByDescending(r => r.DiskBytesPerSecond),
            ProcSort.Network => rows.OrderByDescending(r => r.NetBytesPerSecond),
            _ => rows.OrderByDescending(r => r.CpuPercent)
        };

        return ordered.ThenByDescending(r => r.MemoryBytes).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Take(Math.Max(count, 0)).ToList();
    }
}

/// <summary>
/// İşlem başına CPU, RAM, disk ve (yönetici yetkisiyle) ağ kullanımını örnekler. Yalnızca görev yöneticisi widget'ı görünürken çalışır;
/// widget kapanınca ETW oturumu kapatılır. Ağ, "Microsoft-Windows-Kernel-Network" olaylarından toplanır ve yalnızca yönetici yetkisinde mümkündür.
/// </summary>
public sealed class ProcessMonitor : IDisposable
{
    private static readonly Guid KernelNetwork = new("7dd42a49-5329-4832-8dfd-43d979153a88");
    private const string SessionName = "dHud-ProcessNet";

    // Kernel-Network olay numaraları: TCP/UDP, IPv4/IPv6, gönderme ve alma (hepsinde "PID" ve "size" alanı var).
    private static readonly HashSet<int> NetworkEventIds = [10, 11, 26, 27, 42, 43, 58, 59];

    private readonly ConcurrentDictionary<int, long> _netBytes = new();
    private readonly object _gate = new();
    private TraceEventSession? _session;
    private readonly object _sampleGate = new();   // Sample (arka plan) ve Reset (arayüz) aynı anda çalışmasın
    private List<ProcSample> _previous = [];
    private long _previousTick;

    /// <summary>Ağ ölçümü çalışıyor mu (yönetici yetkisi ve oturum gerekir).</summary>
    public bool NetworkAvailable => _session is not null;

    public int Cores { get; } = Environment.ProcessorCount;

    /// <summary>Ağ ölçümünü başlatır; yönetici değilse veya oturum açılamazsa sessizce kapalı kalır.</summary>
    public void StartNetwork()
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                return;
            }

            try
            {
                if (TraceEventSession.IsElevated() != true)
                {
                    return;
                }

                TraceEventSession.GetActiveSession(SessionName)?.Stop();
                var session = new TraceEventSession(SessionName) { StopOnDispose = true };
                session.EnableProvider(KernelNetwork, TraceEventLevel.Informational, ulong.MaxValue);
                session.Source.AllEvents += OnNetworkEvent;
                _session = session;
                _ = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        session.Source.Process();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Ağ izleme durdu: {ex}");
                    }
                }, TaskCreationOptions.LongRunning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ağ izleme başlatılamadı: {ex}");
                StopNetwork();
            }
        }
    }

    public void StopNetwork()
    {
        lock (_gate)
        {
            try
            {
                _session?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ağ oturumu kapatılamadı: {ex}");
            }

            _session = null;
            _netBytes.Clear();
        }
    }

    private void OnNetworkEvent(TraceEvent e)
    {
        if (e.ProviderGuid != KernelNetwork || !NetworkEventIds.Contains((int)e.ID))
        {
            return;
        }

        try
        {
            if (e.PayloadByName("PID") is { } pid && e.PayloadByName("size") is { } size)
            {
                var bytes = Convert.ToInt64(size);
                if (bytes > 0)
                {
                    _netBytes.AddOrUpdate(Convert.ToInt32(pid), bytes, (_, total) => total + bytes);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            // Beklenmeyen alan biçimi: o olay sayılmaz.
        }
    }

    /// <summary>Widget yeniden göründü: eski örnek bayat olduğundan bir sonraki örnek yalnızca başlangıç noktası olur.</summary>
    public void Reset()
    {
        lock (_sampleGate)
        {
            _previous = [];
            _netBytes.Clear();
        }
    }

    /// <summary>Şimdiki örneği alır ve bir öncekiyle karşılaştırıp satırları verir (ilk çağrıda boş liste: karşılaştıracak örnek yok).</summary>
    public List<ProcRow> Sample()
    {
        var current = Capture();   // kilit dışında: yavaş kısım
        lock (_sampleGate)
        {
            var tick = Stopwatch.GetTimestamp();
            var net = _netBytes.IsEmpty ? null : DrainNetwork();

            List<ProcRow> rows;
            if (_previous.Count == 0)
            {
                rows = [];
            }
            else
            {
                var seconds = (tick - _previousTick) / (double)Stopwatch.Frequency;
                rows = ProcessTable.Compute(_previous, current, seconds, Cores, net);
            }

            _previous = current;
            _previousTick = tick;
            return rows;
        }
    }

    private Dictionary<int, long> DrainNetwork()
    {
        var result = new Dictionary<int, long>();
        foreach (var pid in _netBytes.Keys)
        {
            if (_netBytes.TryRemove(pid, out var bytes))
            {
                result[pid] = bytes;
            }
        }

        return result;
    }

    /// <summary>Çalışan işlemlerin sayaçlarını okur; erişilemeyen (korumalı) işlemler yalnızca bellekle listelenir.</summary>
    public static List<ProcSample> Capture()
    {
        var result = new List<ProcSample>(256);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return result;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.Id == 0)
                    {
                        continue; // Boşta işlemi: CPU boşluğunu gösterir, gerçek bir uygulama değil.
                    }

                    long cpu = 0, io = 0;
                    var handle = NativeProcess.OpenProcess(NativeProcess.QueryLimitedInformation, false, process.Id);
                    if (handle != IntPtr.Zero)
                    {
                        try
                        {
                            if (NativeProcess.GetProcessTimes(handle, out _, out _, out var kernel, out var user))
                            {
                                cpu = kernel + user;
                            }

                            if (NativeProcess.GetProcessIoCounters(handle, out var counters))
                            {
                                io = (long)Math.Min(counters.ReadTransferCount + counters.WriteTransferCount, long.MaxValue);
                            }
                        }
                        finally
                        {
                            NativeProcess.CloseHandle(handle);
                        }
                    }

                    result.Add(new ProcSample(process.Id, process.ProcessName, cpu, io, process.WorkingSet64));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // İşlem okunurken kapandı; atla.
                }
            }
        }

        return result;
    }

    public void Dispose() => StopNetwork();

    private static class NativeProcess
    {
        public const uint QueryLimitedInformation = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        public struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetProcessIoCounters(IntPtr handle, out IoCounters counters);
    }
}
