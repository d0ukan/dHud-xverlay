using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DHud.Services;

/// <summary>
/// Tam ekran bir uygulama (film, sunum, oyun) çalışıyor mu? Windows'un kendi "kullanıcı meşgul" durumunu
/// (<c>SHQueryUserNotificationState</c>) saniyede bir sorar; pencere boyutlarını tahmin etmekten güvenilirdir.
/// Titreşmesin diye durum ancak iki ardışık okumada aynıysa değişir.
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    // QUERY_USER_NOTIFICATION_STATE
    private const int Busy = 2;                // tam ekran uygulama (D3D olmayan) veya sunum ayarları
    private const int RunningD3DFullScreen = 3; // tam ekran Direct3D (oyun)
    private const int PresentationMode = 4;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _candidate;

    public bool IsFullscreen { get; private set; }

    /// <summary>Tam ekran durumu değişti (arayüz iş parçacığında).</summary>
    public event Action<bool>? Changed;

    public FullscreenWatcher() => _timer.Tick += (_, _) => Poll();

    public void Start() => _timer.Start();

    /// <summary>Windows durum kodu tam ekran uygulama anlamına geliyor mu.</summary>
    internal static bool IsFullscreenState(int state) => state is Busy or RunningD3DFullScreen or PresentationMode;

    /// <summary>Titreşim önleme: yeni durum ancak art arda iki okumada aynıysa kabul edilir.</summary>
    internal bool Feed(bool reading)
    {
        if (reading == IsFullscreen)
        {
            _candidate = reading;
            return false;
        }

        if (_candidate == reading)
        {
            IsFullscreen = reading;
            return true;
        }

        _candidate = reading;
        return false;
    }

    private void Poll()
    {
        var reading = false;
        try
        {
            reading = SHQueryUserNotificationState(out var state) == 0 && IsFullscreenState(state);

            // Test kancası: DHUD_FAKE_FULLSCREEN ortam değişkeninin gösterdiği dosya varsa tam ekran sayılır
            // (etkileşimsiz oturumlarda gerçek tam ekran uygulama başlatılamadığı için otomatik testler bunu kullanır).
            if (Environment.GetEnvironmentVariable("DHUD_FAKE_FULLSCREEN") is { Length: > 0 } flag && System.IO.File.Exists(flag))
            {
                reading = true;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Beklenmeyen sistemde özellik sessizce devre dışı kalır.
        }

        if (Feed(reading))
        {
            Changed?.Invoke(IsFullscreen);
        }
    }

    public void Dispose() => _timer.Stop();
}
