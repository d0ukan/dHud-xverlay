using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CpuGpuOverlay.Services;

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

    public bool ShowUsage { get; set; } = true;
    public bool ShowSparkline { get; set; }
    public bool ShowClock { get; set; }
    public bool ShowPower { get; set; }
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
    public int WarnTemperature { get; set; } = 60;
    public int HotTemperature { get; set; } = 80;

    /// <summary>Sıcaklık <see cref="AlertSeconds"/> boyunca kritik eşiğin üstünde kalırsa bildirim göster.</summary>
    public bool AlertEnabled { get; set; } = true;
    public int AlertSeconds { get; set; } = 10;

    /// <summary>Açılışta GitHub Releases'te yeni sürüm var mı diye bak.</summary>
    public bool AutoUpdateCheck { get; set; } = true;

    public StartupMethod PreferredStartupMethod { get; set; } = StartupMethod.TaskScheduler;
}

/// <summary>
/// Ayarları %AppData%\CpuGpuOverlay\settings.json dosyasında saklar.
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
        "CpuGpuOverlay",
        "settings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>Görünümü etkileyen bir ayar değiştiğinde tetiklenir.</summary>
    public event EventHandler? Changed;

    public void Load()
    {
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
        s.WarnTemperature = Math.Clamp(s.WarnTemperature, 30, 100);
        s.HotTemperature = Math.Clamp(s.HotTemperature, s.WarnTemperature + 1, 110);
        s.UpdateIntervalMs = Math.Clamp(s.UpdateIntervalMs, 500, 5000);
        s.AlertSeconds = Math.Clamp(s.AlertSeconds, 3, 120);
        s.PingHost = string.IsNullOrWhiteSpace(s.PingHost) ? "1.1.1.1" : s.PingHost.Trim();
    }
}
