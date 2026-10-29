using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CpuGpuOverlay.Helpers;
using CpuGpuOverlay.Services;
using Forms = System.Windows.Forms;

namespace CpuGpuOverlay;

public partial class App : Application
{
    private const string MutexName = @"Local\CpuGpuOverlay_SingleInstance";

    // Sabit kısayollar. F tuşları seçildi: Ctrl+Shift+harf kombinasyonları tarayıcı/editörlerle sık çakışıyor.
    private const ModifierKeys HotkeyModifiers = ModifierKeys.Control | ModifierKeys.Shift;
    private const Key ToggleOverlayKey = Key.F9;
    private const Key ToggleClickThroughKey = Key.F10;
    public const string ToggleOverlayShortcut = "Ctrl+Shift+F9";
    public const string ToggleClickThroughShortcut = "Ctrl+Shift+F10";

    private const string UpdateBalloonKind = "update";

    private readonly UpdateService _updates = new();
    private UpdateInfo? _pendingUpdate;
    private Mutex? _singleInstanceMutex;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;
    private MainWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private bool? _hotkeysRegisteredFor;
    private readonly TemperatureAlert _cpuAlert = new("CPU");
    private readonly TemperatureAlert _gpuAlert = new("GPU");

    public static new App Current => (App)Application.Current;

    public SettingsService Settings { get; } = new();
    public HardwareMonitorService Monitor { get; } = new();
    public bool IsElevated { get; private set; }
    public bool IsPawnIoInstalled { get; private set; }

    /// <summary>Kısayol tuşları kaydedilemediyse kullanıcıya gösterilecek açıklama.</summary>
    public string? HotkeyError { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        UpdateService.CleanupOldFiles();

        // Güncelleme sonrası yeniden başlatıldıysak eski örneğin kapanıp mutex'i bırakmasını bekle.
        var restarted = e.Args.Contains(UpdateService.RestartedArgument);
        var acquired = TryAcquireSingleInstance();
        for (var i = 0; restarted && !acquired && i < 20; i++)
        {
            Thread.Sleep(500);
            acquired = TryAcquireSingleInstance();
        }

        if (!acquired)
        {
            Shutdown();
            return;
        }

        // CPU sıcaklığı için yönetici yetkisi gerekir. Yetki yoksa bir kez UAC ile yeniden başlatmayı dene;
        // kullanıcı reddederse yetkisiz devam edilir (GPU değerleri genelde yine okunur).
        IsElevated = AdminHelper.IsElevated();
        if (!IsElevated && !e.Args.Contains(AdminHelper.ElevatedArgument))
        {
            // Yeni (yükseltilmiş) örnek mutex'i alabilsin diye önce bırak.
            ReleaseSingleInstance();
            if (AdminHelper.TryRelaunchElevated(e.Args))
            {
                Shutdown();
                return;
            }

            if (!TryAcquireSingleInstance())
            {
                Shutdown();
                return;
            }
        }

        IsPawnIoInstalled = AdminHelper.IsPawnIoInstalled();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        Settings.Load();

        _overlay = new MainWindow();
        _overlay.Show();

        _tray = new TrayIconService(ToggleOverlayShortcut, ToggleClickThroughShortcut);
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.ToggleOverlayRequested += (_, _) => ToggleOverlay();
        _tray.ClickThroughToggleRequested += (_, _) => ToggleClickThrough();
        _tray.LockToggleRequested += (_, _) => ToggleLock();
        _tray.ExitRequested += (_, _) => ExitApplication();
        _tray.UpdateCheckRequested += (_, _) => _ = CheckForUpdatesAsync(manual: true);
        _tray.BalloonClicked += (_, _) =>
        {
            if (_tray.BalloonKind == UpdateBalloonKind && _pendingUpdate is not null)
            {
                _ = PromptAndInstallAsync(_pendingUpdate);
            }
        };

        _hotkeys = new HotkeyService();

        Settings.Changed += (_, _) => OnSettingsChanged();
        OnSettingsChanged();

        Monitor.SnapshotUpdated += snapshot => Dispatcher.BeginInvoke(() => OnSnapshot(snapshot));
        Monitor.Start(TimeSpan.FromMilliseconds(Settings.Current.UpdateIntervalMs));

        if (!IsElevated)
        {
            _tray.ShowBalloon("Yönetici yetkisi yok",
                "CPU sıcaklığı okunamayabilir. Uygulamayı yönetici olarak çalıştırın.",
                Forms.ToolTipIcon.Warning);
        }
        else if (!IsPawnIoInstalled)
        {
            _tray.ShowBalloon("PawnIO sürücüsü bulunamadı",
                "CPU sıcaklığı için PawnIO sürücüsü gerekir: https://pawnio.eu",
                Forms.ToolTipIcon.Warning);
        }
        else if (Settings.Current.ClickThrough)
        {
            _tray.ShowBalloon("Tıklama geçirme açık",
                $"Overlay fareye tepki vermez. Kapatmak için {ToggleClickThroughShortcut} veya tepsi menüsü.");
        }

        if (Settings.Current.AutoUpdateCheck)
        {
            // Açılışı yavaşlatmamak ve ağın hazır olması için kısa bir gecikmeyle.
            _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(
                _ => Dispatcher.InvokeAsync(() => CheckForUpdatesAsync(manual: false)),
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// GitHub Releases'te yeni sürüm arar. manual=true ise sonuç (güncel/hata dahil) kullanıcıya gösterilir;
    /// otomatik denetimde yalnızca yeni sürüm varsa tepsi bildirimi çıkar.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool manual)
    {
        UpdateInfo? update;
        try
        {
            update = await _updates.CheckAsync();
        }
        catch (Exception ex)
        {
            if (manual)
            {
                MessageBox.Show($"Güncelleme denetlenemedi:\n\n{ex.Message}", "dHud",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (update is null)
        {
            if (manual)
            {
                MessageBox.Show($"dHud güncel ({UpdateService.CurrentVersionText}).", "dHud",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        _pendingUpdate = update;
        if (manual)
        {
            await PromptAndInstallAsync(update);
        }
        else
        {
            _tray?.ShowBalloon($"dHud {update.Tag} yayında",
                "Güncellemek için tıklayın.", Forms.ToolTipIcon.Info, UpdateBalloonKind);
        }
    }

    private async Task PromptAndInstallAsync(UpdateInfo update)
    {
        if (!UpdateService.CanSelfUpdate || update.DownloadUrl is null)
        {
            // Geliştirme derlemesi kendini değiştiremez; sürüm sayfasını aç.
            if (MessageBox.Show($"Yeni sürüm: {update.Tag} (şu an {UpdateService.CurrentVersionText}).\n\nSürüm sayfası açılsın mı?",
                    "dHud", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            }

            return;
        }

        if (MessageBox.Show($"Yeni sürüm: {update.Tag} (şu an {UpdateService.CurrentVersionText}).\n\nİndirilip kurulsun ve dHud yeniden başlatılsın mı?",
                "dHud", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _updates.InstallAsync(update);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Güncelleme kurulamadı:\n\n{ex.Message}", "dHud",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var args = IsElevated
            ? $"{AdminHelper.ElevatedArgument} {UpdateService.RestartedArgument}"
            : UpdateService.RestartedArgument;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true });
        ExitApplication();
    }

    private void OnSettingsChanged()
    {
        var s = Settings.Current;

        Monitor.Interval = TimeSpan.FromMilliseconds(s.UpdateIntervalMs);
        Monitor.Network.PingHost = s.PingHost;
        Monitor.Network.PingEnabled = s.ShowNetwork && s.ShowPing;
        _tray?.SetStates(_overlay?.IsVisible == true, s.ClickThrough, s.LockPosition);

        // Kısayollar yalnızca ayar gerçekten değiştiğinde yeniden kaydedilir.
        if (_hotkeysRegisteredFor != s.HotkeysEnabled)
        {
            _hotkeysRegisteredFor = s.HotkeysEnabled;
            RegisterHotkeys(s.HotkeysEnabled);
        }
    }

    private void RegisterHotkeys(bool enabled)
    {
        if (_hotkeys is null)
        {
            return;
        }

        _hotkeys.UnregisterAll();
        HotkeyError = null;
        if (!enabled)
        {
            return;
        }

        var failed = new List<string>();
        if (!_hotkeys.Register(HotkeyModifiers, ToggleOverlayKey, ToggleOverlay))
        {
            failed.Add(ToggleOverlayShortcut);
        }

        if (!_hotkeys.Register(HotkeyModifiers, ToggleClickThroughKey, ToggleClickThrough))
        {
            failed.Add(ToggleClickThroughShortcut);
        }

        if (failed.Count > 0)
        {
            HotkeyError = $"⚠ Başka bir uygulama kullandığı için kaydedilemedi: {string.Join(", ", failed)}";
        }
    }

    private void OnSnapshot(SensorSnapshot snapshot)
    {
        _overlay?.UpdateReadings(snapshot);
        _tray?.UpdateTooltip(
            $"CPU {Format.Temperature(snapshot.Cpu?.Temperature)} | GPU {Format.Temperature(snapshot.Gpu?.Temperature)}");

        var now = DateTime.UtcNow;
        foreach (var message in new[]
                 {
                     _cpuAlert.Check(snapshot.Cpu?.Temperature, Settings.Current, now),
                     _gpuAlert.Check(snapshot.Gpu?.Temperature, Settings.Current, now)
                 })
        {
            if (message is not null)
            {
                _tray?.ShowBalloon("Yüksek sıcaklık", message, Forms.ToolTipIcon.Warning);
            }
        }
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ToggleOverlay()
    {
        if (_overlay is null)
        {
            return;
        }

        if (_overlay.IsVisible)
        {
            _overlay.Hide();
        }
        else
        {
            _overlay.Show();
        }

        OnSettingsChanged();
    }

    public void ToggleClickThrough()
    {
        var s = Settings.Current;
        s.ClickThrough = !s.ClickThrough;
        Settings.NotifyChanged();

        _tray?.ShowBalloon(
            s.ClickThrough ? "Tıklama geçirme açık" : "Tıklama geçirme kapalı",
            s.ClickThrough
                ? $"Tıklamalar alttaki pencereye gider. Kapatmak için {ToggleClickThroughShortcut}."
                : "Overlay yeniden sürüklenebilir ve sağ tık menüsü kullanılabilir.");
    }

    public void ToggleLock()
    {
        Settings.Current.LockPosition = !Settings.Current.LockPosition;
        Settings.NotifyChanged();
    }

    public void ExitApplication()
    {
        _overlay?.SavePosition();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Monitor.Dispose();
        ReleaseSingleInstance();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
            {
                return true;
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Mutex yükseltilmiş bir örnek tarafından tutuluyor: uygulama zaten çalışıyor.
            return false;
        }
    }

    private void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex is null)
        {
            return;
        }

        try
        {
            _singleInstanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Bu thread sahibi değil.
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Beklenmeyen bir hata oluştu:\n\n{e.Exception.Message}", "dHud",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
