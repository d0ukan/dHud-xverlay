using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace DHud.Services;

/// <summary>
/// dHud ani sonlandırılırsa (Görev Yöneticisi, çökme, güç kesintisi sonrası değil sadece işlem ölümü) fanlar son yazılan
/// hızda kalır; bu hız düşükse işlemci ısınabilir. İki katmanlı koruma:
/// <list type="number">
/// <item><b>İşaret dosyası:</b> dHud fanları devralınca hangi fanları kontrol ettiğini diske yazar, temiz kapanışta siler.
/// Dosya bir sonraki açılışta varsa önceki kapanışın temiz olmadığı anlaşılır.</item>
/// <item><b>Bekçi süreci:</b> aynı exe <c>--fan-watchdog &lt;pid&gt;</c> ile küçük bir yardımcı olarak başlatılır, dHud'un
/// bitmesini bekler. İşaret dosyası hâlâ duruyorsa (temiz kapanmadı) ve dHud yeniden açılmadıysa o fanları
/// güvenli sabit hıza (%<see cref="SafePercent"/>) çıkarır.</item>
/// </list>
/// </summary>
public static class FanSafety
{
    public const string WatchdogArgument = "--fan-watchdog";
    public const int SafePercent = 70;

    private const string MutexName = @"Local\CpuGpuOverlay_SingleInstance";
    private static readonly TimeSpan RestartGrace = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string[] _lastIds = [];
    private static Process? _watchdog;

    public sealed record Marker(int Pid, string[] Fans);

    public static string DefaultMarkerPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dHud", "fans-active.json");

    // ---- dHud tarafı ----

    /// <summary>Kontrol edilen fanların listesi değişince işaret dosyasını günceller; ilk fanda bekçiyi başlatır.</summary>
    public static void Update(IReadOnlyCollection<string> controlledIds)
    {
        var ids = controlledIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (ids.SequenceEqual(_lastIds))
        {
            return;
        }

        _lastIds = ids;
        if (ids.Length == 0)
        {
            Clear();
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultMarkerPath)!);
            File.WriteAllText(DefaultMarkerPath, JsonSerializer.Serialize(new Marker(Environment.ProcessId, ids), Json));
            EnsureWatchdog();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Fan işaret dosyası yazılamadı: {ex.Message}");
        }
    }

    /// <summary>Fanlar BIOS'a geri verildi: temiz kapanış işareti (dosyayı sil).</summary>
    public static void Clear()
    {
        _lastIds = [];
        try
        {
            File.Delete(DefaultMarkerPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Fan işaret dosyası silinemedi: {ex.Message}");
        }
    }

    /// <summary>Önceki çalışma temiz kapanmadıysa kontrol ettiği fanların kimlikleri; yoksa null. Dosyayı siler.</summary>
    public static string[]? TakePreviousCrashMarker()
    {
        var marker = Read(DefaultMarkerPath);
        if (marker is null)
        {
            return null;
        }

        // Başka bir dHud örneği hâlâ çalışıyorsa (bu örnek ikincisi) onun işaretine dokunma.
        if (marker.Pid != Environment.ProcessId && IsProcessRunning(marker.Pid))
        {
            return null;
        }

        try
        {
            File.Delete(DefaultMarkerPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Eski fan işaret dosyası silinemedi: {ex.Message}");
        }

        return marker.Fans;
    }

    private static void EnsureWatchdog()
    {
        if (_watchdog is { HasExited: false })
        {
            return;
        }

        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _watchdog = Process.Start(new ProcessStartInfo(path, $"{WatchdogArgument} {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            // Bekçi başlamazsa yalnızca ikinci koruma katmanı (ani ölüm) devre dışı kalır.
            Debug.WriteLine($"Fan bekçisi başlatılamadı: {ex.Message}");
        }
    }

    // ---- Bekçi süreci ----

    /// <summary>
    /// Bekçi giriş noktası: <c>--fan-watchdog &lt;pid&gt; [--marker yol] [--dry-run]</c>. dHud (pid) bitince, işaret dosyası
    /// duruyorsa ve dHud yeniden açılmadıysa işaretli fanları güvenli hıza çıkarır. <c>--dry-run</c> donanıma dokunmaz.
    /// </summary>
    public static int RunWatchdog(string[] args)
    {
        var pidIndex = Array.IndexOf(args, WatchdogArgument) + 1;
        if (pidIndex <= 0 || pidIndex >= args.Length || !int.TryParse(args[pidIndex], out var pid))
        {
            return 2;
        }

        var markerIndex = Array.IndexOf(args, "--marker");
        var markerPath = markerIndex >= 0 && markerIndex + 1 < args.Length ? args[markerIndex + 1] : DefaultMarkerPath;
        var dryRun = args.Contains("--dry-run");

        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
            // dHud zaten bitmiş.
        }

        var marker = Read(markerPath);
        if (marker is null)
        {
            return 0; // Temiz kapanış: yapılacak bir şey yok.
        }

        // dHud kısa süre içinde yeniden açıldıysa (güncelleme, elle yeniden başlatma) fanları o devralır.
        Thread.Sleep(RestartGrace);
        if (IsAnotherInstanceRunning(dryRun))
        {
            return 0;
        }

        if (dryRun)
        {
            File.WriteAllText(markerPath + ".acted", string.Join(',', marker.Fans));
            return 10;
        }

        ApplySafeSpeed(marker.Fans);
        return 10;
    }

    private static void ApplySafeSpeed(string[] ids)
    {
        var computer = new Computer { IsMotherboardEnabled = true, IsGpuEnabled = true };
        try
        {
            computer.Open();
            foreach (var hardware in computer.Hardware)
            {
                SetSafe(hardware, ids);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Bekçi fanları ayarlayamadı: {ex.Message}");
        }
        finally
        {
            try
            {
                computer.Close();
            }
            catch (Exception)
            {
                // Kapanış hatası önemsiz.
            }
        }
    }

    private static void SetSafe(IHardware hardware, string[] ids)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Control && ids.Contains(sensor.Identifier.ToString()) && sensor.Control is { } control)
            {
                try
                {
                    control.SetSoftware(Math.Clamp(SafePercent, control.MinSoftwareValue, control.MaxSoftwareValue));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fan güvenli hıza alınamadı ({sensor.Name}): {ex.Message}");
                }
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            SetSafe(sub, ids);
        }
    }

    // ---- Yardımcılar ----

    private static Marker? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsAnotherInstanceRunning(bool dryRun)
    {
        if (dryRun)
        {
            return false;
        }

        try
        {
            if (Mutex.TryOpenExisting(MutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // Başka (yükseltilmiş) bir örnek tutuyor.
        }
        catch (Exception)
        {
            return false;
        }
    }
}
