using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Services;
using Forms = System.Windows.Forms;

namespace DHud;

public partial class App : Application
{
    // Ad bilerek eski haliyle (CpuGpuOverlay) bırakıldı: güncelleme sırasında eski sürümle aynı kilidi paylaşsın.
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
    private string? _notifiedUpdateTag;
    private DispatcherTimer? _updateTimer;
    private int _updateTimerHours;
    private Mutex? _singleInstanceMutex;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;
    private MainWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private FpsWindow? _fpsWindow;
    private bool? _hotkeysRegisteredFor;
    private AppLanguage? _appliedLanguage;
    private readonly TemperatureAlert _cpuAlert = new("CPU", isGpu: false);
    private readonly TemperatureAlert _gpuAlert = new("GPU", isGpu: true);

    public static new App Current => (App)Application.Current;

    public SettingsService Settings { get; } = new();
    public HardwareMonitorService Monitor { get; } = new();
    public MobileLinkService MobileLink { get; private set; } = null!;
    public FpsMonitor Fps { get; } = new();
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
        // Geliştirme/ölçüm kolaylığı: --allow-multiple ile aynı anda birden fazla örnek açılabilir.
        var acquired = e.Args.Contains("--allow-multiple") || TryAcquireSingleInstance();
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
        StartupManager.MigrateLegacy();

        MobileLink = new MobileLinkService(Monitor, Settings) { FpsProvider = () => Fps.Fps };
        Fps.Changed += fps => Dispatcher.BeginInvoke(() => OnFps(fps));

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
        StartMemoryTrimTimer();

        if (!IsElevated)
        {
            _tray.ShowBalloon(Loc.T("N.NoAdmin.Title"),
                Loc.T("N.NoAdmin.Text"),
                Forms.ToolTipIcon.Warning);
        }
        else if (!IsPawnIoInstalled)
        {
            _tray.ShowBalloon(Loc.T("N.NoPawnIo.Title"),
                Loc.T("N.NoPawnIo.Text"),
                Forms.ToolTipIcon.Warning);
        }
        else if (Settings.Current.ClickThrough)
        {
            _tray.ShowBalloon(Loc.T("N.ClickThroughOn"),
                Loc.T("N.ClickThroughOn.Text", ToggleClickThroughShortcut));
        }

        // Geliştirme/test kolaylığı: ayarlar penceresini doğrudan açar.
        if (e.Args.Contains("--settings"))
        {
            OpenSettings();
        }

        // Test kolaylığı: güncelleme denetimini hemen (elle denetlemiş gibi) çalıştırır.
        if (e.Args.Contains("--check-update"))
        {
            _ = CheckForUpdatesAsync(manual: true);
        }

    }

    /// <summary>
    /// Otomatik güncelleme denetimi: açılıştan kısa süre sonra bir kez, ardından uygulama açık kaldığı sürece
    /// seçilen saatte bir. Ayar kapatılırsa durur; aralık değişirse yeni aralıkla yeniden başlar.
    /// </summary>
    private void ConfigureUpdateTimer()
    {
        var s = Settings.Current;
        if (!s.AutoUpdateCheck)
        {
            _updateTimer?.Stop();
            return;
        }

        if (_updateTimer is { IsEnabled: true } && _updateTimerHours == s.UpdateCheckHours)
        {
            return;
        }

        // Sıfırdan başlıyorsa ilk denetim açılışı yavaşlatmamak ve ağın hazır olması için kısa bir gecikmeyle yapılır.
        var firstStart = _updateTimer is not { IsEnabled: true };
        _updateTimerHours = s.UpdateCheckHours;
        if (_updateTimer is null)
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Tick += (_, _) =>
            {
                _updateTimer.Interval = TimeSpan.FromHours(_updateTimerHours);
                _ = CheckForUpdatesAsync(manual: false);
            };
        }

        _updateTimer.Stop();
        _updateTimer.Interval = firstStart ? TimeSpan.FromSeconds(15) : TimeSpan.FromHours(_updateTimerHours);
        _updateTimer.Start();
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
                MessageBox.Show(Loc.T("U.CheckFailed", ex.Message), "dHud",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (update is null)
        {
            if (manual)
            {
                MessageBox.Show(Loc.T("U.UpToDate", UpdateService.CurrentVersionText), "dHud",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        _pendingUpdate = update;
        if (manual)
        {
            await PromptAndInstallAsync(update);
        }
        else if (_notifiedUpdateTag != update.Tag)
        {
            // Aynı sürüm için bildirim bir kez gösterilir; her denetimde tekrar rahatsız etmez.
            _notifiedUpdateTag = update.Tag;
            _tray?.ShowBalloon(Loc.T("U.Available", update.Tag),
                Loc.T("U.ClickToUpdate"), Forms.ToolTipIcon.Info, UpdateBalloonKind);
        }
    }

    private async Task PromptAndInstallAsync(UpdateInfo update)
    {
        if (!UpdateService.CanSelfUpdate || update.DownloadUrl is null)
        {
            // Geliştirme derlemesi kendini değiştiremez; sürüm sayfasını aç.
            if (MessageBox.Show(Loc.T("U.OpenPage", update.Tag, UpdateService.CurrentVersionText),
                    "dHud", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            }

            return;
        }

        if (MessageBox.Show(Loc.T("U.Install", update.Tag, UpdateService.CurrentVersionText),
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
            MessageBox.Show(Loc.T("U.InstallFailed", ex.Message), "dHud",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var args = IsElevated
            ? $"{AdminHelper.ElevatedArgument} {UpdateService.RestartedArgument}"
            : UpdateService.RestartedArgument;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true });
        ExitApplication();
    }

    /// <summary>
    /// Açılıştaki donanım taramasından kalan çöpü toplar ve kullanılmayan çalışma kümesini Windows'a iade eder.
    /// Sonrasında seyrek aralıklarla tekrarlanır; sayfalar gerektiğinde geri yüklenir.
    /// </summary>
    private void StartMemoryTrimTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) =>
        {
            timer.Interval = TimeSpan.FromMinutes(3);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        };
        timer.Start();
    }

    /// <summary>FPS ölçümünü ayara göre başlatır/durdurur; sayaç penceresi yalnızca ölçüm ve sayaç ayarı açıkken görünür.</summary>
    private void ApplyFps()
    {
        var s = Settings.Current;
        if (s.FpsEnabled)
        {
            Fps.Start();
        }
        else
        {
            Fps.Stop();
        }

        if (!s.FpsEnabled || !s.FpsOverlay)
        {
            _fpsWindow?.Update(null);
        }
    }

    private void OnFps(float? fps)
    {
        var s = Settings.Current;
        if (!s.FpsEnabled || !s.FpsOverlay)
        {
            return;
        }

        _fpsWindow ??= new FpsWindow();
        _fpsWindow.Update(fps);
    }

    private void OnSettingsChanged()
    {
        var s = Settings.Current;

        ApplyLanguageChange(s.Language);
        Monitor.Interval = TimeSpan.FromMilliseconds(s.UpdateIntervalMs);
        Monitor.Network.PingHost = s.PingHost;
        Monitor.CpuEnabled = s.ShowCpu;
        Monitor.GpuEnabled = s.ShowGpu;
        Monitor.FanEnabled = s.ShowFan;
        Monitor.Network.Enabled = s.ShowNetwork;
        Monitor.Network.PingEnabled = s.ShowPing;
        _tray?.SetStates(_overlay?.IsVisible == true, s.ClickThrough, s.LockPosition);
        ConfigureUpdateTimer();
        MobileLink.Apply();
        ApplyFps();

        // Kısayollar yalnızca ayar gerçekten değiştiğinde yeniden kaydedilir.
        if (_hotkeysRegisteredFor != s.HotkeysEnabled)
        {
            _hotkeysRegisteredFor = s.HotkeysEnabled;
            RegisterHotkeys(s.HotkeysEnabled);
        }
    }

    /// <summary>
    /// Dil değiştiyse tepsi menüsünü yeniler ve (açıksa) ayarlar penceresini yeni dilde yeniden açar.
    /// Overlay menüsü her ayar değişiminde kendi metinlerini zaten yeniler.
    /// </summary>
    private void ApplyLanguageChange(AppLanguage language)
    {
        var previous = _appliedLanguage;
        _appliedLanguage = language;
        if (previous is null || previous == language)
        {
            return;
        }

        _tray?.ApplyLanguage();
        RegisterHotkeys(Settings.Current.HotkeysEnabled); // hata iletisi dile bağlı

        if (_settingsWindow is { IsLoaded: true } window)
        {
            var location = (window.Left, window.Top);
            window.Close();
            Dispatcher.BeginInvoke(() =>
            {
                OpenSettings();
                if (_settingsWindow is not null)
                {
                    (_settingsWindow.Left, _settingsWindow.Top) = location;
                }
            });
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
            HotkeyError = Loc.T("N.HotkeyFailed", string.Join(", ", failed));
        }
    }

    private void OnSnapshot(SensorSnapshot snapshot)
    {
        _overlay?.UpdateReadings(snapshot);
        var tooltip = new List<string>();
        if (Settings.Current.ShowCpu)
        {
            tooltip.Add($"CPU {Format.Temperature(snapshot.Cpu?.Temperature)}");
        }

        if (Settings.Current.ShowGpu)
        {
            tooltip.Add($"GPU {Format.Temperature(snapshot.Gpu?.Temperature)}");
        }

        _tray?.UpdateTooltip(tooltip.Count > 0 ? string.Join(" | ", tooltip) : "dHud");

        var now = DateTime.UtcNow;
        foreach (var message in new[]
                 {
                     _cpuAlert.Check(snapshot.Cpu?.Temperature, Settings.Current, now),
                     _gpuAlert.Check(snapshot.Gpu?.Temperature, Settings.Current, now)
                 })
        {
            if (message is not null)
            {
                _tray?.ShowBalloon(Loc.T("N.HighTemp"), message, Forms.ToolTipIcon.Warning);
                MobileLink.SendAlert(message);
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
            Loc.T(s.ClickThrough ? "N.ClickThroughOn" : "N.ClickThroughOff"),
            s.ClickThrough
                ? Loc.T("N.ClickThroughOn.Hint", ToggleClickThroughShortcut)
                : Loc.T("N.ClickThroughOff.Hint"));
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
        Fps.Dispose();
        _fpsWindow?.Close();
        MobileLink?.Dispose();
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
        MessageBox.Show(Loc.T("N.Unexpected", e.Exception.Message), "dHud",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
