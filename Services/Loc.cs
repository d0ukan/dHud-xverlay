using System.Globalization;

namespace DHud.Services;

public enum AppLanguage
{
    English,
    Turkish
}

/// <summary>
/// Arayüz metinlerinin EN/TR çevirileri. Anahtar bulunamazsa anahtarın kendisi gösterilir.
/// Dil değişince <see cref="Current"/> güncellenir; pencereler metinlerini yeniden yazmaktan sorumludur.
/// </summary>
public static class Loc
{
    public static AppLanguage Current { get; set; } = AppLanguage.English;

    /// <summary>Sayı biçimlendirmesi (ondalık ayracı vb.) için dile uygun kültür.</summary>
    public static string CultureName => Current == AppLanguage.Turkish ? "tr-TR" : "en-US";

    public static string T(string key) =>
        Strings.TryGetValue(key, out var pair) ? (Current == AppLanguage.Turkish ? pair.Tr : pair.En) : key;

    public static string T(string key, params object[] args) =>
        string.Format(CultureInfo.GetCultureInfo(CultureName), T(key), args);

    private static readonly Dictionary<string, (string En, string Tr)> Strings = new()
    {
        // ---- Ayarlar penceresi: başlık, sekmeler, düğmeler ----
        ["S.Title"] = ("dHud - Settings", "dHud - Ayarlar"),
        ["S.Settings"] = ("Settings", "Ayarlar"),
        ["S.Close"] = ("Close", "Kapat"),
        ["S.ExitApp"] = ("Exit app", "Uygulamadan çık"),
        ["S.Tab.Appearance"] = ("Appearance", "Görünüm"),
        ["S.Tab.Metrics"] = ("Metrics", "Göstergeler"),
        ["S.Tab.Behavior"] = ("Behavior", "Davranış"),
        ["S.Tab.Alerts"] = ("Alerts", "Uyarılar"),

        // ---- Görünüm ----
        ["S.Layout"] = ("LAYOUT", "DÜZEN"),
        ["S.Layout.Vertical"] = ("Vertical", "Dikey"),
        ["S.Layout.Horizontal"] = ("Horizontal (single row)", "Yatay (tek satır)"),
        ["S.Compact"] = ("Compact view", "Kompakt görünüm"),
        ["S.Compact.Hint"] = ("Smaller text, tighter spacing and short units (like 52°).",
            "Daha küçük yazı, dar boşluklar ve kısa birimler (52° gibi)."),
        ["S.AlwaysOnTop"] = ("Always on top", "Her zaman en üstte göster"),
        ["S.AlwaysOnTop.Hint"] = ("When off, the overlay may stay behind other windows.",
            "Kapalıyken overlay diğer pencerelerin arkasında kalabilir."),
        ["S.SizeOpacity"] = ("SIZE AND OPACITY", "BOYUT VE SAYDAMLIK"),
        ["S.Opacity"] = ("Background opacity", "Arka plan opaklığı"),
        ["S.Size"] = ("Size", "Boyut"),
        ["S.Language"] = ("LANGUAGE", "DİL"),

        // ---- Göstergeler ----
        ["S.CpuGpu"] = ("CPU AND GPU", "CPU VE GPU"),
        ["S.Usage"] = ("Usage (%)", "Kullanım yüzdesi (%)"),
        ["S.Sparkline"] = ("Mini temperature graph (last 60 samples)", "Mini sıcaklık grafiği (son 60 ölçüm)"),
        ["S.Clock"] = ("Clock speed (GHz / MHz)", "Frekans (GHz / MHz)"),
        ["S.Power"] = ("Power draw (W)", "Güç tüketimi (W)"),
        ["S.Vram"] = ("GPU memory usage (VRAM)", "GPU bellek kullanımı (VRAM)"),
        ["S.System"] = ("SYSTEM", "SİSTEM"),
        ["S.Ram"] = ("RAM usage", "RAM kullanımı"),
        ["S.Network"] = ("NETWORK / INTERNET", "AĞ / İNTERNET"),
        ["S.NetSpeed"] = ("Download / upload speed", "İndirme / yükleme hızı"),
        ["S.NetBits"] = ("Show speed in Mbps (instead of MB/s)", "Hızı Mbps olarak göster (yerine MB/s)"),
        ["S.Ping"] = ("Ping (latency)", "Ping (gecikme)"),
        ["S.Address"] = ("Address", "Adres"),
        ["S.Refresh"] = ("REFRESH", "GÜNCELLEME"),
        ["S.Interval"] = ("Refresh interval", "Okuma aralığı"),
        ["S.Interval.Hint"] = ("Shorter intervals feel smoother, longer ones use less CPU.",
            "Kısa aralık daha akıcı, uzun aralık daha az işlemci kullanır."),
        ["S.Unit.Sec"] = (" s", " sn"),

        // ---- Davranış ----
        ["S.Startup"] = ("STARTUP", "BAŞLANGIÇ"),
        ["S.RunAtStartup"] = ("Run at Windows startup", "Windows başlangıcında çalıştır"),
        ["S.TaskScheduler"] = ("Task Scheduler (recommended)", "Görev Zamanlayıcı (önerilen)"),
        ["S.TaskScheduler.Hint"] = ("Starts as administrator at sign-in without a UAC prompt. Saving requires administrator rights.",
            "Oturum açılışında UAC sormadan yönetici olarak başlar. Kaydetmek için yönetici yetkisi gerekir."),
        ["S.Registry"] = ("Registry (HKCU\\...\\Run)", "Registry (HKCU\\...\\Run)"),
        ["S.Registry.Hint"] = ("Classic method. A UAC prompt appears at every startup for CPU temperature.",
            "Klasik yöntem. CPU sıcaklığı için her açılışta UAC onayı istenir."),
        ["S.Startup.Task"] = ("✓ Active: Task Scheduler (\"dHud\" task)", "✓ Etkin: Görev Zamanlayıcı (\"dHud\" görevi)"),
        ["S.Startup.Registry"] = ("✓ Active: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
            "✓ Etkin: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
        ["S.Startup.Off"] = ("Run at startup is off.", "Başlangıçta çalıştırma kapalı."),
        ["S.Startup.Title"] = ("Startup setting", "Başlangıç ayarı"),
        ["S.MouseAndPosition"] = ("MOUSE AND POSITION", "FARE VE KONUM"),
        ["S.ClickThrough"] = ("Click-through", "Tıklama geçirme"),
        ["S.ClickThrough.Hint"] = ("Clicks pass through the overlay to the window below (e.g. a game). Turn off with Ctrl+Shift+F10 or the tray menu.",
            "Tıklamalar overlay'den geçip alttaki pencereye (ör. oyuna) gider. Kapatmak için Ctrl+Shift+F10 veya tepsi menüsü."),
        ["S.Lock"] = ("Lock position", "Konumu kilitle"),
        ["S.Snap"] = ("Snap to screen edges", "Ekran kenarlarına yapıştır"),
        ["S.Hotkeys"] = ("HOTKEYS", "KISAYOL TUŞLARI"),
        ["S.Hotkeys.Enable"] = ("Enable hotkeys", "Kısayol tuşlarını etkinleştir"),
        ["S.Hotkeys.Toggle"] = ("Hide / show overlay", "Overlay'i gizle / göster"),
        ["S.Hotkeys.ClickThrough"] = ("Toggle click-through", "Tıklama geçirmeyi aç / kapat"),
        ["S.Version"] = ("VERSION", "SÜRÜM"),
        ["S.AutoUpdate"] = ("Check for updates on startup", "Açılışta güncellemeleri denetle"),
        ["S.CheckNow"] = ("Check now", "Şimdi denetle"),

        // ---- Uyarılar ----
        ["S.TempColors"] = ("TEMPERATURE COLORS", "SICAKLIK RENKLERİ"),
        ["S.ColorByTemp"] = ("Color by temperature", "Sıcaklığa göre renklendir"),
        ["S.Cpu"] = ("CPU", "CPU"),
        ["S.Gpu"] = ("GPU", "GPU"),
        ["S.WarnThreshold"] = ("Warning threshold", "Uyarı eşiği"),
        ["S.HotThreshold"] = ("Critical threshold", "Kritik eşik"),
        ["S.GreenHint"] = ("Below the warning threshold is shown in green.", "Uyarı eşiğinin altı yeşil gösterilir."),
        ["S.Notification"] = ("NOTIFICATION", "BİLDİRİM"),
        ["S.AlertEnable"] = ("Notify at critical temperature", "Kritik sıcaklıkta bildirim göster"),
        ["S.AlertSeconds"] = ("Time above critical threshold", "Kritik eşiğin üstünde kalma süresi"),

        // ---- Ayarlar: durum kutusu ----
        ["S.Elevated"] = ("✓ Running as administrator", "✓ Yönetici olarak çalışıyor"),
        ["S.NotElevated"] = ("⚠ No administrator rights — CPU temperature may not be readable",
            "⚠ Yönetici yetkisi yok — CPU sıcaklığı okunamayabilir"),
        ["S.PawnIo.Ok"] = ("✓ PawnIO driver installed", "✓ PawnIO sürücüsü kurulu"),
        ["S.PawnIo.Missing"] = ("⚠ PawnIO driver missing — install it from https://pawnio.eu",
            "⚠ PawnIO sürücüsü yok — https://pawnio.eu adresinden kurun"),
        ["S.Scanning"] = ("scanning...", "taranıyor..."),
        ["S.NotFound"] = ("not found", "bulunamadı"),

        // ---- Overlay sağ tık menüsü ve ipuçları ----
        ["M.Settings"] = ("Settings...", "Ayarlar..."),
        ["M.ToVertical"] = ("Switch to vertical layout", "Dikey düzene geç"),
        ["M.ToHorizontal"] = ("Switch to horizontal layout", "Yatay düzene geç"),
        ["M.Compact"] = ("Compact view", "Kompakt görünüm"),
        ["M.Lock"] = ("Lock position", "Konumu kilitle"),
        ["M.ClickThrough"] = ("Enable click-through", "Tıklama geçirmeyi aç"),
        ["M.ResetPosition"] = ("Reset position", "Konumu sıfırla"),
        ["M.Hide"] = ("Hide overlay", "Overlay'i gizle"),
        ["M.Metrics"] = ("Metrics", "Göstergeler"),
        ["M.Exit"] = ("Exit", "Çıkış"),
        ["Tip.CpuNotFound"] = ("CPU not found", "CPU bulunamadı"),
        ["Tip.GpuNotFound"] = ("GPU not found", "GPU bulunamadı"),
        ["Tip.Vram"] = ("GPU memory (VRAM)", "GPU belleği (VRAM)"),
        ["Tip.Ram"] = ("System memory", "Sistem belleği"),
        ["Tip.NoNetwork"] = ("No active network connection found", "Etkin ağ bağlantısı bulunamadı"),
        ["Tip.Download"] = ("Download", "İndirme"),
        ["Tip.Upload"] = ("Upload", "Yükleme"),
        ["Tip.CpuNeedsAdmin"] = ("Run the app as administrator for CPU temperature.",
            "CPU sıcaklığı için uygulamayı yönetici olarak çalıştırın."),
        ["Tip.CpuNoSensor"] = ("CPU temperature sensor could not be read.", "CPU sıcaklık sensörü okunamadı."),
        ["Tip.CpuNeedsPawnIo"] = ("The PawnIO driver is required for CPU temperature (https://pawnio.eu).",
            "CPU sıcaklığı için PawnIO sürücüsü gerekli (https://pawnio.eu)."),

        // ---- Tepsi menüsü ----
        ["T.Settings"] = ("Settings...", "Ayarlar..."),
        ["T.Hide"] = ("Hide overlay", "Overlay'i gizle"),
        ["T.Show"] = ("Show overlay", "Overlay'i göster"),
        ["T.ClickThrough"] = ("Click-through", "Tıklama geçirme"),
        ["T.Lock"] = ("Lock position", "Konumu kilitle"),
        ["T.CheckUpdates"] = ("Check for updates ({0})", "Güncellemeleri denetle ({0})"),
        ["T.Exit"] = ("Exit", "Çıkış"),

        // ---- Bildirimler ve iletiler ----
        ["N.NoAdmin.Title"] = ("No administrator rights", "Yönetici yetkisi yok"),
        ["N.NoAdmin.Text"] = ("CPU temperature may not be readable. Run the app as administrator.",
            "CPU sıcaklığı okunamayabilir. Uygulamayı yönetici olarak çalıştırın."),
        ["N.NoPawnIo.Title"] = ("PawnIO driver not found", "PawnIO sürücüsü bulunamadı"),
        ["N.NoPawnIo.Text"] = ("The PawnIO driver is required for CPU temperature: https://pawnio.eu",
            "CPU sıcaklığı için PawnIO sürücüsü gerekir: https://pawnio.eu"),
        ["N.ClickThroughOn"] = ("Click-through is on", "Tıklama geçirme açık"),
        ["N.ClickThroughOn.Text"] = ("The overlay ignores the mouse. Turn it off with {0} or the tray menu.",
            "Overlay fareye tepki vermez. Kapatmak için {0} veya tepsi menüsü."),
        ["N.ClickThroughOff"] = ("Click-through is off", "Tıklama geçirme kapalı"),
        ["N.ClickThroughOn.Hint"] = ("Clicks go to the window below. Turn it off with {0}.",
            "Tıklamalar alttaki pencereye gider. Kapatmak için {0}."),
        ["N.ClickThroughOff.Hint"] = ("The overlay can be dragged again and the right-click menu works.",
            "Overlay yeniden sürüklenebilir ve sağ tık menüsü kullanılabilir."),
        ["N.HotkeyFailed"] = ("⚠ Could not be registered because another application uses it: {0}",
            "⚠ Başka bir uygulama kullandığı için kaydedilemedi: {0}"),
        ["N.HighTemp"] = ("High temperature", "Yüksek sıcaklık"),
        ["N.HighTemp.Text"] = ("{0} temperature is {1:0}°C — above the critical threshold ({3}°C) for {2} s.",
            "{0} sıcaklığı {1:0}°C — {2} sn'dir kritik eşiğin ({3}°C) üzerinde."),
        ["N.Unexpected"] = ("An unexpected error occurred:\n\n{0}", "Beklenmeyen bir hata oluştu:\n\n{0}"),

        // ---- Güncelleme ----
        ["U.CheckFailed"] = ("Could not check for updates:\n\n{0}", "Güncelleme denetlenemedi:\n\n{0}"),
        ["U.UpToDate"] = ("dHud is up to date ({0}).", "dHud güncel ({0})."),
        ["U.Available"] = ("dHud {0} is available", "dHud {0} yayında"),
        ["U.ClickToUpdate"] = ("Click to update.", "Güncellemek için tıklayın."),
        ["U.OpenPage"] = ("New version: {0} (currently {1}).\n\nOpen the release page?",
            "Yeni sürüm: {0} (şu an {1}).\n\nSürüm sayfası açılsın mı?"),
        ["U.Install"] = ("New version: {0} (currently {1}).\n\nDownload, install and restart dHud?",
            "Yeni sürüm: {0} (şu an {1}).\n\nİndirilip kurulsun ve dHud yeniden başlatılsın mı?"),
        ["U.InstallFailed"] = ("The update could not be installed:\n\n{0}", "Güncelleme kurulamadı:\n\n{0}"),
        ["U.NoAsset"] = ("This release has no downloadable file.", "Bu sürümde indirilebilir dosya yok."),
        ["U.BadDownload"] = ("The downloaded file is invalid.", "İndirilen dosya geçersiz."),

        // ---- Başlangıç yöneticisi ----
        ["St.NoPath"] = ("Could not determine the application path.", "Uygulama yolu belirlenemedi."),
        ["St.NeedAdmin"] = ("The app must run as administrator to create a Task Scheduler entry.",
            "Görev Zamanlayıcı kaydı oluşturmak için uygulamanın yönetici olarak çalışması gerekir."),
        ["St.DeleteFailed"] = ("The scheduled task could not be deleted. Run as administrator and try again.\n\n",
            "Zamanlanmış görev silinemedi. Yönetici olarak çalıştırıp tekrar deneyin.\n\n"),
        ["St.CreateFailed"] = ("The scheduled task could not be created.\n\n", "Zamanlanmış görev oluşturulamadı.\n\n"),
    };
}
