using System.Collections.Concurrent;
using System.Diagnostics;
using DHud.Helpers;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace DHud.Services;

/// <summary>
/// Oyunların FPS değerini Windows olay izleme (ETW) ile ölçer: DirectX 9/10/11/12'nin "Present" (kareyi ekrana gönder)
/// olaylarını sayar. Yalnızca öndeki, tüm monitörü kaplayan (tam ekran / kenarlıksız) pencerenin işlemi ölçülür.
/// Oyuna müdahale etmez (enjeksiyon yok), bu yüzden anti-cheat sistemleriyle sorun çıkarmaz. Yönetici yetkisi gerekir.
/// </summary>
public sealed class FpsMonitor : IDisposable
{
    private const string SessionName = "dHud-FPS";

    // Microsoft-Windows-DXGI (D3D10/11/12): Present tamamlandı = olay 43 (Chrome ölçümünde ~145/sn = 144 Hz ekran, doğrulandı).
    // Microsoft-Windows-D3D9: Present başlangıcı = olay 1 (henüz doğrulanmadı).
    private static readonly Guid DxgiProvider = new("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
    private static readonly Guid D3D9Provider = new("783aca0a-790e-4d7f-8451-aa850511c6b9");
    private const int DxgiPresentStop = 43;
    private const int D3D9PresentStart = 1;

    private static readonly string[] IgnoredProcesses = ["explorer", "dwm", "dHud"];

    private readonly ConcurrentDictionary<int, int> _presents = new();
    private readonly object _gate = new();

    private TraceEventSession? _session;
    private Timer? _timer;
    private long _lastTick;
    private float _fps = float.NaN;

    /// <summary>Ölçülen FPS; ön planda tam ekran bir oyun yoksa null.</summary>
    public float? Fps
    {
        get
        {
            var value = Volatile.Read(ref _fps);
            return float.IsNaN(value) ? null : value;
        }
    }

    public bool IsRunning => _session is not null;

    /// <summary>Başlatılamadıysa nedeni (yönetici yetkisi yok, oturum açılamadı).</summary>
    public string? Error { get; private set; }

    /// <summary>Her saniye tetiklenir. DİKKAT: arka plan thread'inden çağrılır.</summary>
    public event Action<float?>? Changed;

    public void Start()
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                return;
            }

            Error = null;
            try
            {
                if (TraceEventSession.IsElevated() != true)
                {
                    Error = "admin";
                    return;
                }

                // Önceki örnek çöktüyse arkada kalmış oturumu kapat.
                TraceEventSession.GetActiveSession(SessionName)?.Stop();

                var session = new TraceEventSession(SessionName) { StopOnDispose = true };
                session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, ulong.MaxValue);
                session.EnableProvider(D3D9Provider, TraceEventLevel.Verbose, ulong.MaxValue);
                session.Source.AllEvents += OnEvent;
                _session = session;

                _presents.Clear();
                _lastTick = Stopwatch.GetTimestamp();
                _ = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        session.Source.Process();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"FPS izleme durdu: {ex}");
                    }
                }, TaskCreationOptions.LongRunning);

                _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Debug.WriteLine($"FPS izleme başlatılamadı: {ex}");
                DisposeSession();
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            DisposeSession();
        }

        Volatile.Write(ref _fps, float.NaN);
        Changed?.Invoke(null);
    }

    private void DisposeSession()
    {
        _timer?.Dispose();
        _timer = null;
        try
        {
            _session?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FPS oturumu kapatılamadı: {ex}");
        }

        _session = null;
    }

    private void OnEvent(TraceEvent e)
    {
        var id = (int)e.ID;
        if ((e.ProviderGuid == DxgiProvider && id == DxgiPresentStop)
            || (e.ProviderGuid == D3D9Provider && id == D3D9PresentStart))
        {
            _presents.AddOrUpdate(e.ProcessID, 1, static (_, count) => count + 1);
        }
    }

    private void Tick()
    {
        try
        {
            var now = Stopwatch.GetTimestamp();
            var seconds = (now - _lastTick) / (double)Stopwatch.Frequency;
            _lastTick = now;

            // Bu aralıktaki sayaçları al ve sıfırla.
            var counts = new Dictionary<int, int>();
            foreach (var pid in _presents.Keys)
            {
                if (_presents.TryRemove(pid, out var count))
                {
                    counts[pid] = count;
                }
            }

            float? fps = null;
            if (seconds > 0 && TryGetFullscreenForegroundProcess(out var foregroundPid)
                && counts.TryGetValue(foregroundPid, out var frames) && frames > 0)
            {
                fps = (float)Math.Round(frames / seconds);
            }

            Volatile.Write(ref _fps, fps ?? float.NaN);
            Changed?.Invoke(fps);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FPS hesaplanamadı: {ex}");
        }
    }

    /// <summary>Öndeki pencere tüm monitörü kaplıyorsa (tam ekran/kenarlıksız) ve masaüstü kabuğu değilse işlem kimliğini verir.</summary>
    private static bool TryGetFullscreenForegroundProcess(out int pid)
    {
        pid = 0;
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        var m = info.rcMonitor;
        if (rect.Left > m.Left || rect.Top > m.Top || rect.Right < m.Right || rect.Bottom < m.Bottom)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        pid = (int)processId;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !IgnoredProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisposeSession();
        }
    }
}
