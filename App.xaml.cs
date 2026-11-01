using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Services;
using DHud.Widgets;
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
    private FanControlWindow? _fanWindow;
    private FpsWindow? _fpsWindow;
    private bool? _hotkeysRegisteredFor;
    private AppLanguage? _appliedLanguage;
    private readonly TemperatureAlert _cpuAlert = new("CPU", isGpu: false);
    private readonly TemperatureAlert _gpuAlert = new("GPU", isGpu: true);

    public static new App Current => (App)Application.Current;

    // Veri klasörü komut satırından (--data-dir) değişebildiği için OnStartup'ta yeniden kurulabilir.
    public SettingsService Settings { get; private set; } = new();
    public HardwareMonitorService Monitor { get; } = new();
    public MobileLinkService MobileLink { get; private set; } = null!;
    public FpsMonitor Fps { get; } = new();
    public FanControlService FanControl { get; private set; } = null!;
    public RemoteFanService RemoteFan { get; private set; } = null!;
    public WidgetService Widgets { get; private set; } = null!;
    public WidgetManager WidgetManager { get; private set; } = null!;
    public ProfileService Profiles { get; private set; } = null!;
    public GameSessionTracker GameTracker { get; } = new();
    private bool _recoveredFromCrash;

    /// <summary>Fan penceresi açıkken anakart sensörleri (fan listesi) kontrol kapalı olsa da taranır.</summary>
    public bool FanWindowOpen { get; set; }
    public bool IsElevated { get; private set; }
    public bool IsPawnIoInstalled { get; private set; }

    /// <summary>Kısayol tuşları kaydedilemediyse kullanıcıya gösterilecek açıklama.</summary>
    public string? HotkeyError { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Fan bekçisi: pencere/tepsi açmayan yardımcı mod (bkz. FanSafety). Tek örnek denetiminden önce çalışır.
        if (e.Args.Contains(FanSafety.WatchdogArgument))
        {
            Shutdown(FanSafety.RunWatchdog(e.Args));
            return;
        }

        // Veri klasörü değiştirilebilir (test / taşınabilir kullanım): --data-dir <klasör>
        var dataIndex = Array.IndexOf(e.Args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < e.Args.Length)
        {
            AppPaths.DataDir = System.IO.Path.GetFullPath(e.Args[dataIndex + 1]);
            Settings = new SettingsService(); // artık yeni klasörün settings.json'ını kullanır
        }

        // Performans ölçümü (--bench): gerçek başlangıç yolu çalışır; ama geçici veri klasörü, tek örnek ve UAC atlanır, donanıma yazılmaz (bkz. BenchMode).
        var bench = BenchMode.IsRequested(e.Args);
        if (bench && dataIndex < 0)
        {
            AppPaths.DataDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dhud-bench-" + Guid.NewGuid().ToString("N"));
            Settings = new SettingsService();
        }

        // README ekran görüntüleri: pencereler ekrana çıkmadan demo verilerle görüntü dosyasına çizilir (bkz. ScreenshotGenerator).
        var shotIndex = Array.IndexOf(e.Args, "--screenshots");
        if (shotIndex >= 0 && shotIndex + 1 < e.Args.Length)
        {
            _ = RunScreenshotModeAsync(System.IO.Path.GetFullPath(e.Args[shotIndex + 1]));
            return;
        }

        UpdateService.CleanupOldFiles();

        // Güncelleme sonrası yeniden başlatıldıysak eski örneğin kapanıp mutex'i bırakmasını bekle.
        var restarted = e.Args.Contains(UpdateService.RestartedArgument);
        // Geliştirme/ölçüm kolaylığı: --allow-multiple ile aynı anda birden fazla örnek açılabilir.
        var acquired = e.Args.Contains("--allow-multiple") || bench || TryAcquireSingleInstance();
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
        IsElevated = AdminHelper.IsElevated() || bench;
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
        if (bench)
        {
            BenchMode.Prepare(Settings.Current);
        }
        else
        {
            StartupManager.MigrateLegacy();
        }

        // Widget'lar: notlar, hatırlatıcılar, saat. Hatırlatıcı zamanı gelince bildirim + ses (+ istenirse telefon).
        Widgets = new WidgetService();
        Widgets.Load();
        WidgetManager = new WidgetManager(Settings, Widgets);
        Profiles = new ProfileService(Settings, Widgets, UpdateService.CurrentVersionText);
        Widgets.ReminderFired += OnReminderFired;

        FanControl = new FanControlService(Monitor, Settings);
        // Önceki çalışma ani sonlandıysa: fanlar bekçi tarafından güvenli hıza çıkarılmış olabilir; geri devral/bırak.
        if (FanSafety.TakePreviousCrashMarker() is { Length: > 0 } crashed)
        {
            FanControl.RecoverAfterCrash(crashed);
            _recoveredFromCrash = true;
        }

        // Kontrol edilen bir fan dönmeyi bırakırsa tepsi bildirimi ve (bağlıysa) mobil uyarı.
        FanControl.FanStalled += stalled => Dispatcher.BeginInvoke(() =>
        {
            // Birden fazla fan aynı anda durduysa tek mesaj: yağmur gibi bildirim gelmesin.
            var message = stalled.Count == 1
                ? Loc.T("F.Stalled", stalled[0].Name, stalled[0].Percent)
                : Loc.T("F.StalledMany", stalled.Count, string.Join(", ", stalled.Select(s => s.Name)));
            _tray?.ShowBalloon(Loc.T("F.StalledTitle"), message, Forms.ToolTipIcon.Warning);
            MobileLink?.SendAlert(message);
        });
        // Uygulama nasıl kapanırsa kapansın fanlar BIOS denetimine geri verilir.
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            DiagLog.Add("crash", a.ExceptionObject is Exception ex ? ex : new InvalidOperationException(a.ExceptionObject?.ToString()));
            FanControl?.ReleaseAll();
            Widgets?.Flush();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { FanControl?.ReleaseAll(); Widgets?.Flush(); };
        SessionEnding += (_, _) => { FanControl?.ReleaseAll(); Widgets?.Flush(); };

        RemoteFan = new RemoteFanService(Monitor, Settings, FanControl);
        RemoteFan.Changed += summary =>
        {
            _tray?.ShowBalloon(Loc.T("F.Title"), Loc.T("F.RemoteChanged", summary), Forms.ToolTipIcon.Info);
            _fanWindow?.Reload();
        };
        // Ölçümde kullanıcının çalışan dHud'uyla çakışmasın diye rastgele port, keşif kapalı.
        MobileLink = new MobileLinkService(Monitor, Settings, bench ? 0 : MobileLinkService.DefaultPort, bench ? -1 : MobileLinkService.DefaultDiscoveryPort)
        {
            FpsProvider = () => Fps.Fps,
            FanProvider = () => RemoteFan.BuildState(),
            CommandHandler = command => Dispatcher.InvokeAsync(() => RemoteFan.Apply(command)).Task
        };
        Fps.Changed += fps => Dispatcher.BeginInvoke(() => OnFps(fps));

        // Oyun oturumu bitince özet kartı (ayardan kapatılabilir).
        GameTracker.Finished += summary => Dispatcher.BeginInvoke(() =>
        {
            if (Settings.Current.FpsEnabled && Settings.Current.GameSummary)
            {
                new GameSummaryWindow(summary).Show();

                // Telefona da kısa bir özet bildirimi (mobil uygulama bağlıysa).
                var parts = new List<string>
                {
                    Loc.T("G.PhoneMain", (int)Math.Round(summary.Duration.TotalMinutes), $"{summary.AvgFps:0}", $"{summary.OnePercentLow:0}")
                };
                if (summary.MaxCpuTemp is float cpuMax)
                {
                    parts.Add($"CPU {cpuMax:0}°C");
                }

                if (summary.MaxGpuTemp is float gpuMax)
                {
                    parts.Add($"GPU {gpuMax:0}°C");
                }

                MobileLink?.SendAlert(string.Join(" · ", parts), "summary");
            }
        });
        Fps.Changed += fps =>
        {
            var latest = Monitor.Latest;
            GameTracker.Sample(fps, latest?.Cpu?.Temperature, latest?.Gpu?.Temperature);
        };

        _overlay = new MainWindow();
        if (Settings.Current.ShowOverlay)
        {
            _overlay.Show();
        }

        _tray = new TrayIconService(ToggleOverlayShortcut, ToggleClickThroughShortcut);
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.ToggleOverlayRequested += (_, _) => ToggleOverlay();
        _tray.ClickThroughToggleRequested += (_, _) => ToggleClickThrough();
        _tray.LockToggleRequested += (_, _) => ToggleLock();
        _tray.ExitRequested += (_, _) => ExitApplication();
        _tray.NewNoteRequested += (_, _) => WidgetManager.NewNote();
        _tray.ProfileRequested += (_, name) => ApplyProfileByName(name);
        RefreshTrayProfiles();
        if (_recoveredFromCrash)
        {
            _tray.ShowBalloon(Loc.T("F.Title"), Loc.T("F.Recovered"), Forms.ToolTipIcon.Warning);
        }

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
        if (!bench || BenchMode.UseRealHardware(e.Args))
        {
            Monitor.Start(TimeSpan.FromMilliseconds(Settings.Current.UpdateIntervalMs));
        }

        StartMemoryTrimTimer();

        if (bench)
        {
            _ = BenchMode.RunAsync(this, e.Args);
            return;
        }

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
                DarkMessageBox.Show(Loc.T("U.CheckFailed", ex.Message), "dHud",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        if (update is null)
        {
            if (manual)
            {
                DarkMessageBox.Show(Loc.T("U.UpToDate", UpdateService.CurrentVersionText), "dHud",
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
            if (DarkMessageBox.Show(Loc.T("U.OpenPage", update.Tag, UpdateService.CurrentVersionText),
                    "dHud", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            }

            return;
        }

        if (DarkMessageBox.Show(Loc.T("U.Install", update.Tag, UpdateService.CurrentVersionText),
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
            DarkMessageBox.Show(Loc.T("U.InstallFailed", ex.Message), "dHud",
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

    /// <summary>Okunacak donanımları ayara göre belirler. Fan kontrolü sıcaklık kaynaklarına ve anakart sensörlerine ihtiyaç duyar.</summary>
    public void ApplyMonitorFlags()
    {
        var s = Settings.Current;
        // Bar kapalıyken yalnızca bar için gereken okumalar durur; sistem özeti, telefon ve fan kontrolü kendi verisini yine ister.
        var bar = s.ShowOverlay;
        var others = s.Extra(WidgetIds.SysInfo).Enabled || s.MobileEnabled;
        Monitor.CpuEnabled = (bar && (s.ShowCpu || s.ShowCores)) || s.FanControlEnabled || others;
        Monitor.GpuEnabled = (bar && s.ShowGpu) || s.FanControlEnabled || others;
        Monitor.FanEnabled = (bar && s.ShowFan) || s.FanControlEnabled || FanWindowOpen;
    }

    /// <summary>
    /// Sorun bildirirken yapıştırılacak tanı bilgisi: sürüm, Windows, yetki, sürücü, donanım adları, açık özellikler ve son günlük kayıtları.
    /// Not metinleri, telefon anahtarı, takvim adresi, şehir ve dosya yolları İÇERMEZ.
    /// </summary>
    public string BuildDiagnostics()
    {
        var s = Settings.Current;
        var latest = Monitor.Latest;
        var widgets = new List<string>();
        if (s.WidgetClock) widgets.Add("clock");
        if (s.WidgetStopwatch) widgets.Add("stopwatch");
        if (s.WidgetTools) widgets.Add("timer");
        if (s.WidgetWeather) widgets.Add("weather");
        if (s.NotesVisible && Widgets.Data.Notes.Count > 0) widgets.Add($"notes({Widgets.Data.Notes.Count})");
        widgets.AddRange(WidgetIds.Extra.Where(id => s.Extra(id).Enabled));

        var lines = new List<string>
        {
            $"dHud {UpdateService.CurrentVersionText}",
            $"Windows: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit)",
            $".NET: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            $"Administrator: {IsElevated} | PawnIO driver: {IsPawnIoInstalled}",
            $"CPU: {latest?.Cpu?.Name ?? "-"} (temperature {(latest?.Cpu?.Temperature is null ? "not readable" : "ok")})",
            $"GPU: {latest?.Gpu?.Name ?? "-"} (temperature {(latest?.Gpu?.Temperature is null ? "not readable" : "ok")})",
            $"Language: {s.Language} | Bar: {(s.ShowOverlay ? "on" : "off")} ({s.Layout}) | Interval: {s.UpdateIntervalMs} ms",
            $"Fans found: {Monitor.FanChannels.Count} | Fan control: {s.FanControlEnabled} | Phone may change fans: {s.MobileFanControl}",
            $"FPS counter: {s.FpsEnabled} (error: {Fps.Error ?? "none"})",
            $"Mobile link: {s.MobileEnabled} (running: {MobileLink.IsRunning}, devices: {MobileLink.ClientCount}, error: {MobileLink.LastError ?? "none"})",
            $"Widgets: {(widgets.Count == 0 ? "none" : string.Join(", ", widgets))}",
            $"Calendar source: {(string.IsNullOrEmpty(s.CalendarSource) ? "not set" : s.CalendarSource.StartsWith("http", StringComparison.OrdinalIgnoreCase) || s.CalendarSource.StartsWith("webcal", StringComparison.OrdinalIgnoreCase) ? "link" : "file")}",
            $"Profiles saved: {Profiles.List().Count}",
            "",
            "Recent log:"
        };
        var log = DiagLog.Snapshot();
        lines.AddRange(log.Count == 0 ? ["(empty)"] : log.TakeLast(25));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Ekran görüntüsü modu: gerçek ayarlara dokunmadan geçici bir veri klasöründe demo verilerle görüntüleri üretir ve çıkar.</summary>
    private async Task RunScreenshotModeAsync(string outDir)
    {
        try
        {
            AppPaths.DataDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dhud-shots-" + Guid.NewGuid().ToString("N"));
            IsElevated = true;
            Settings = new SettingsService(); // geçici klasörün (boş) ayarları; gerçek ayarlar hiç okunmaz
            Settings.Load();
            Widgets = new WidgetService();
            Widgets.Load();
            WidgetManager = new WidgetManager(Settings, Widgets);
            Profiles = new ProfileService(Settings, Widgets, UpdateService.CurrentVersionText);
            FanControl = new FanControlService(Monitor, Settings);
            RemoteFan = new RemoteFanService(Monitor, Settings, FanControl);
            MobileLink = new MobileLinkService(Monitor, Settings);
            await ScreenshotGenerator.RunAsync(outDir);
        }
        catch (Exception ex)
        {
            System.IO.Directory.CreateDirectory(outDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "error.txt"), ex.ToString());
        }
        finally
        {
            Shutdown();
        }
    }

    /// <summary>Overlay'in ekrandaki dikdörtgeni (gölge payı hariç); gizliyse null. Widget'lar buna binmesin diye kullanılır.</summary>
    public Rect? OverlayRect => _overlay is { IsVisible: true, ActualWidth: > 20 } w
        ? new Rect(w.Left + 10, w.Top + 10, w.ActualWidth - 20, w.ActualHeight - 20)
        : null;

    /// <summary>Profili uygular; widget verisi (notlar vb.) değiştiyse widget pencerelerini yeniden kurar.</summary>
    public void ApplyProfile(ProfileFile file, bool includeWidgetData, bool includePairingKey)
    {
        var widgetsReplaced = Profiles.Apply(file, includeWidgetData, includePairingKey);
        if (widgetsReplaced)
        {
            WidgetManager.CloseAll();
            WidgetManager.Apply();
        }
    }

    /// <summary>Tepsi menüsündeki profil listesini yeniler.</summary>
    public void RefreshTrayProfiles() => _tray?.SetProfiles(Profiles.List().Select(p => p.Name).ToList());

    /// <summary>Tepsiden bir profile geçiş (notlara ve telefon anahtarına dokunmaz).</summary>
    private void ApplyProfileByName(string name)
    {
        var profile = Profiles.List().FirstOrDefault(p => p.Name == name);
        if (profile is null)
        {
            return;
        }

        try
        {
            ApplyProfile(ProfileService.Read(profile.Path), includeWidgetData: false, includePairingKey: false);
            _tray?.ShowBalloon(Loc.T("S.Tab.Profiles"), Loc.T("P.Applied", name));
        }
        catch (ProfileException)
        {
            _tray?.ShowBalloon(Loc.T("S.Tab.Profiles"), Loc.T("P.ErrBad"), Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>Tepsi balonu göster (tepsi henüz yoksa sessizce atlanır).</summary>
    public void NotifyTray(string title, string text) => _tray?.ShowBalloon(title, text);

    /// <summary>Bir hatırlatıcı/zamanlayıcı/pomodoro aşaması bitti: balon, ses ve (ayarlıysa) telefona uyarı.</summary>
    private void OnReminderFired(Reminder reminder)
    {
        var text = reminder.Kind == "pomodoro"
            ? Loc.T("W.PomodoroNow." + reminder.Text)
            : reminder.Text;

        _tray?.ShowBalloon(Loc.T("W.ReminderTitle"), text);
        System.Media.SystemSounds.Asterisk.Play();
        if (Settings.Current.RemindersToPhone)
        {
            MobileLink?.SendAlert(text, "reminder");
        }
    }

    public void OpenFanControl()
    {
        if (_fanWindow is { IsLoaded: true })
        {
            _fanWindow.Activate();
            return;
        }

        _fanWindow = new FanControlWindow();
        _fanWindow.Show();
    }

    private void OnSettingsChanged()
    {
        var s = Settings.Current;

        ApplyLanguageChange(s.Language);
        Monitor.Interval = TimeSpan.FromMilliseconds(s.UpdateIntervalMs);
        Monitor.Network.PingHost = s.PingHost;
        ApplyMonitorFlags();
        var needOthers = s.Extra(WidgetIds.SysInfo).Enabled || s.MobileEnabled;
        Monitor.Network.Enabled = (s.ShowOverlay && s.ShowNetwork) || needOthers;
        Monitor.Network.PingEnabled = (s.ShowOverlay && s.ShowPing) || s.MobileEnabled;
        ApplyOverlayVisibility();
        _tray?.SetStates(_overlay?.IsVisible == true, s.ClickThrough, s.LockPosition);
        ConfigureUpdateTimer();
        MobileLink.Apply();
        ApplyFps();
        WidgetManager.Apply();

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
        WidgetManager?.CloseAll(); // metinler yeni dilde yeniden oluşturulsun (OnSettingsChanged yeniden açar)
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

    /// <summary>
    /// Üstteki ince bar'ı açar/kapatır. Seçim KALICIDIR (ayarlardaki "Barı göster" kutusuyla aynı); bar kapalıyken CPU/GPU/ağ okumaları
    /// yalnızca başka bir özellik (sistem özeti, telefon, fan kontrolü) istiyorsa sürer.
    /// </summary>
    public void ToggleOverlay()
    {
        if (_overlay is null)
        {
            return;
        }

        var turnOff = _overlay.IsVisible;
        Settings.Current.ShowOverlay = !turnOff;
        Settings.NotifyChanged(); // -> OnSettingsChanged -> ApplyOverlayVisibility

        if (turnOff)
        {
            // Sağ tıkla kapatınca bar'ı geri getirmenin yolunu bir kez hatırlat.
            _tray?.ShowBalloon(Loc.T("M.BarOffTitle"), Loc.T("M.BarOffText", ToggleOverlayShortcut));
        }
    }

    /// <summary>Bar'ın görünürlüğünü ayara göre uygular; geri gelirse widget'lar bar'ın yerinden kayar.</summary>
    private void ApplyOverlayVisibility()
    {
        if (_overlay is null)
        {
            return;
        }

        var want = Settings.Current.ShowOverlay;
        if (want && !_overlay.IsVisible)
        {
            _overlay.Show();
            Dispatcher.BeginInvoke(() => WidgetManager?.ResolveAll(), DispatcherPriority.Background);
        }
        else if (!want && _overlay.IsVisible)
        {
            _overlay.Hide();
        }
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
        Widgets?.Flush();
        FanControl?.Dispose();
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
        DiagLog.Add("ui", e.Exception);
        try
        {
            DarkMessageBox.Show(Loc.T("N.Unexpected", e.Exception.Message), "dHud",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            MessageBox.Show(Loc.T("N.Unexpected", e.Exception.Message), "dHud",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true;
    }
}
