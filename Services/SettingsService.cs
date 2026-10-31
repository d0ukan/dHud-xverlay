using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DHud.Services;

public enum StartupMethod
{
    TaskScheduler,
    Registry
}

public enum OverlayLayout
{
    Vertical,
    Horizontal
}

public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>Overlay'in sağ kenarı. Göstergeler eklenip çıkarılınca sağ kenar sabit kalır, sol kenar kayar.</summary>
    public double? Right { get; set; }

    // ---- Görünüm ----

    /// <summary>Arayüz dili. İlk açılışta İngilizce.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.English;

    public OverlayLayout Layout { get; set; } = OverlayLayout.Vertical;

    /// <summary>Daha küçük yazı, daha dar boşluklar ve kısa birimler ("52°").</summary>
    public bool Compact { get; set; }

    /// <summary>Arka plan panelinin opaklığı (0.2 - 1.0). Yazılar her zaman tam opak kalır.</summary>
    public double BackgroundOpacity { get; set; } = 0.70;

    /// <summary>Overlay boyut çarpanı (0.7 - 2.0).</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Overlay diğer pencerelerin üstünde kalsın mı.</summary>
    public bool AlwaysOnTop { get; set; } = true;

    // ---- Göstergeler ----

    /// <summary>CPU / GPU satırları. Kapalı donanım okunmaz (CPU ve bellek tasarrufu).</summary>
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;

    public bool ShowUsage { get; set; } = true;
    public bool ShowSparkline { get; set; }
    public bool ShowClock { get; set; }
    public bool ShowPower { get; set; }

    /// <summary>Fan hızı (RPM). Anakart sensörleri için yönetici yetkisi gerekir.</summary>
    public bool ShowFan { get; set; }
    public bool ShowVram { get; set; }
    public bool ShowRam { get; set; }

    public bool ShowNetwork { get; set; }
    public bool ShowPing { get; set; } = true;

    /// <summary>true: Mbps/kbps (internet paketleri gibi), false: MB/s / KB/s.</summary>
    public bool NetworkInBits { get; set; }

    public string PingHost { get; set; } = "1.1.1.1";

    /// <summary>Sensör okuma aralığı (500 - 5000 ms).</summary>
    public int UpdateIntervalMs { get; set; } = 1000;

    // ---- Davranış ----

    /// <summary>Fare tıklamaları overlay'den geçip alttaki pencereye gider.</summary>
    public bool ClickThrough { get; set; }

    public bool LockPosition { get; set; }
    public bool SnapToEdges { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;

    // ---- Renkler ve uyarılar ----

    public bool ColorByTemperature { get; set; } = true;
    // Eşikler CPU ve GPU için ayrıdır. 0 = henüz ayarlanmamış (eski ayar dosyası): Normalize eski değerden / varsayılandan doldurur.
    public int CpuWarnTemperature { get; set; }
    public int CpuHotTemperature { get; set; }
    public int GpuWarnTemperature { get; set; }
    public int GpuHotTemperature { get; set; }

    // Eski tek eşik ayarları (v1.0.6 ve öncesi): okunur, yeni eşiklere taşınır ve bir daha yazılmaz.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WarnTemperature { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HotTemperature { get; set; }

    /// <summary>Verilen donanım için uyarı (sarı) ve kritik (kırmızı) eşikleri.</summary>
    public (int Warn, int Hot) Thresholds(bool gpu) =>
        gpu ? (GpuWarnTemperature, GpuHotTemperature) : (CpuWarnTemperature, CpuHotTemperature);

    /// <summary>Sıcaklık <see cref="AlertSeconds"/> boyunca kritik eşiğin üstünde kalırsa bildirim göster.</summary>
    public bool AlertEnabled { get; set; } = true;
    public int AlertSeconds { get; set; } = 10;

    /// <summary>Açılışta GitHub Releases'te yeni sürüm var mı diye bak.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    /// <summary>Uygulama açık kaldığı sürece kaç saatte bir yeni sürüm aranır (seçenekler: <see cref="UpdateCheckChoices"/>).</summary>
    public int UpdateCheckHours { get; set; } = 6;

    public static readonly int[] UpdateCheckChoices = [1, 3, 6, 12, 24];

    /// <summary>Fan hızlarını yazılımla kontrol et. Varsayılan kapalı; kapalıyken fanlara dokunulmaz.</summary>
    public bool FanControlEnabled { get; set; }

    /// <summary>Fan kanalı başına ayar (anahtar: sensör kimliği). Kayıt olmayan fan "Otomatik"tir.</summary>
    public Dictionary<string, FanSetting> Fans { get; set; } = [];

    /// <summary>Kullanıcının kaydettiği, adlandırılmış fan eğrileri (tüm fanlar için ortak).</summary>
    public List<SavedCurve> CurvePresets { get; set; } = [];

    /// <summary>Tüm fanların ayarını saklayan profiller ("Oyun", "Gece" vb.).</summary>
    public List<FanProfile> FanProfiles { get; set; } = [];

    /// <summary>Oyun FPS'ini ölç (ETW). Mobil uygulamaya da gönderilir. Yönetici yetkisi gerekir.</summary>
    public bool FpsEnabled { get; set; }

    /// <summary>Oyun açıkken ekranın köşesinde küçük bir FPS sayacı göster.</summary>
    public bool FpsOverlay { get; set; } = true;

    /// <summary>Mobil uygulamanın yerel ağdan bağlanmasına izin ver. Varsayılan kapalı.</summary>
    public bool MobileEnabled { get; set; }

    /// <summary>QR ile paylaşılan eşleştirme anahtarı; ilk açılışta üretilir.</summary>
    public string MobileToken { get; set; } = string.Empty;

    public StartupMethod PreferredStartupMethod { get; set; } = StartupMethod.TaskScheduler;
}

/// <summary>
/// Ayarları %AppData%\dHud\settings.json dosyasında saklar. Eski adla (CpuGpuOverlay) kaydedilmiş ayarlar ilk açılışta taşınır.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dHud",
        "settings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>Görünümü etkileyen bir ayar değiştiğinde tetiklenir.</summary>
    public event EventHandler? Changed;

    public void Load()
    {
        MigrateLegacySettings();

        try
        {
            if (File.Exists(_filePath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // Bozuk dosya uygulamayı durdurmasın; varsayılanlarla devam et.
            Debug.WriteLine($"Ayarlar okunamadı: {ex}");
            Current = new AppSettings();
        }

        Normalize(Current);
        Loc.Current = Current.Language;
    }

    /// <summary>Eski ad (CpuGpuOverlay) klasöründeki ayarları, yeni dosya yoksa kopyalar.</summary>
    private void MigrateLegacySettings()
    {
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CpuGpuOverlay", "settings.json");
            if (!File.Exists(_filePath) && File.Exists(legacy))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.Copy(legacy, _filePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Eski ayarlar taşınamadı: {ex}");
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ayarlar kaydedilemedi: {ex}");
        }
    }

    /// <summary>Ayarları kaydeder ve dinleyicilere değişikliği bildirir.</summary>
    public void NotifyChanged()
    {
        Normalize(Current);
        Loc.Current = Current.Language;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Normalize(AppSettings s)
    {
        s.BackgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0.2, 1.0);
        s.Scale = Math.Clamp(s.Scale, 0.7, 2.0);
        // Eski tek eşiği (varsa) ayarlanmamış CPU/GPU eşiklerine taşı.
        var legacyWarn = s.WarnTemperature ?? 60;
        var legacyHot = s.HotTemperature ?? 80;
        if (s.CpuWarnTemperature == 0) s.CpuWarnTemperature = legacyWarn;
        if (s.CpuHotTemperature == 0) s.CpuHotTemperature = legacyHot;
        if (s.GpuWarnTemperature == 0) s.GpuWarnTemperature = legacyWarn;
        if (s.GpuHotTemperature == 0) s.GpuHotTemperature = legacyHot;
        s.WarnTemperature = null;
        s.HotTemperature = null;

        s.CpuWarnTemperature = Math.Clamp(s.CpuWarnTemperature, 30, 100);
        s.CpuHotTemperature = Math.Clamp(s.CpuHotTemperature, s.CpuWarnTemperature + 1, 110);
        s.GpuWarnTemperature = Math.Clamp(s.GpuWarnTemperature, 30, 100);
        s.GpuHotTemperature = Math.Clamp(s.GpuHotTemperature, s.GpuWarnTemperature + 1, 110);
        s.UpdateIntervalMs = Math.Clamp(s.UpdateIntervalMs, 500, 5000);
        s.Fans ??= [];
        foreach (var fan in s.Fans.Values)
        {
            FanCurve.Sanitize(fan);
        }

        s.FanProfiles ??= [];
        s.FanProfiles = s.FanProfiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.Last())
            .Take(FanCurve.MaxProfiles)
            .ToList();
        foreach (var profile in s.FanProfiles)
        {
            profile.Name = profile.Name.Trim();
            profile.Fans ??= [];
            foreach (var fan in profile.Fans.Values)
            {
                FanCurve.Sanitize(fan);
            }
        }

        s.CurvePresets ??= [];
        s.CurvePresets = s.CurvePresets
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.Last())
            .Take(FanCurve.MaxSavedCurves)
            .ToList();
        foreach (var preset in s.CurvePresets)
        {
            preset.Name = preset.Name.Trim();
            var temp = new FanSetting { Curve = preset.Curve, MinPercent = preset.MinPercent };
            FanCurve.Sanitize(temp);
            preset.Curve = temp.Curve;
            preset.MinPercent = temp.MinPercent;
        }

        s.AlertSeconds = Math.Clamp(s.AlertSeconds, 3, 120);
        if (!AppSettings.UpdateCheckChoices.Contains(s.UpdateCheckHours))
        {
            s.UpdateCheckHours = 6;
        }

        s.PingHost = string.IsNullOrWhiteSpace(s.PingHost) ? "1.1.1.1" : s.PingHost.Trim();
    }
}
