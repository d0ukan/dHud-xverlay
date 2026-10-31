using System.IO;
using System.Text.Json;

using DHud.Helpers;

namespace DHud.Services;

/// <summary>Bir profil/yedek dosyasının içeriği (.dhudprofile, JSON).</summary>
public sealed class ProfileFile
{
    public int Format { get; set; } = ProfileService.CurrentFormat;
    public string Name { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public DateTime SavedUtc { get; set; }

    /// <summary>Tüm ayarlar (görünüm, metrikler, fan eğrileri, widget ayarları, uyarılar ...).</summary>
    public AppSettings? Settings { get; set; }

    /// <summary>Notlar, yapılacaklar, gün sayaçları, başlatıcı kısayolları ve widget konumları (yalnızca tam yedekte).</summary>
    public WidgetData? Widgets { get; set; }

    /// <summary>Dosya telefon eşleştirme anahtarını içeriyor mu (sahibi aynı telefonla bağlanabilsin diye).</summary>
    public bool HasPairingKey { get; set; }
}

public sealed record ProfileInfo(string Name, DateTime SavedUtc, string Path);

/// <summary>Profil dosyası okunamadı veya uygulanamaz. <see cref="Reason"/>: "bad" (bozuk/yanlış dosya), "newer" (daha yeni dHud'dan), "big".</summary>
public sealed class ProfileException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Ayar profilleri ve yedekleme. Profil = tüm ayarların adlandırılmış kopyası (uygulama içinde saklanır); dışa aktarılan dosya aynı
/// biçimdedir, böylece başka bir bilgisayara taşınıp içe aktarılabilir. Uygulanırken GÜVENLİK için iki şey korunur: fan kontrolü
/// açık/kapalı durumu (başka bilgisayarda fan kontrolü kullanıcı onayı olmadan açılmasın) ve telefon eşleştirme anahtarı
/// (yalnızca kullanıcı isterse dosyadan alınır).
/// </summary>
public sealed class ProfileService(SettingsService settings, WidgetService widgets, string appVersion, string? folder = null)
{
    public const int CurrentFormat = 1;
    public const int MaxProfiles = 20;
    public const int MaxNameLength = 40;
    public const string Extension = ".dhudprofile";
    private const long MaxFileBytes = 20 * 1024 * 1024;

    private string Folder { get; } = folder ?? AppPaths.File("profiles");

    // Henüz taşınmamış bir notun konumu NaN olabilir; hem yazarken hem okurken izin verilir.
    private static readonly JsonSerializerOptions Json = new(SettingsService.JsonOptions)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        PropertyNameCaseInsensitive = true
    };

    // ---- Uygulama içi profiller ----

    public IReadOnlyList<ProfileInfo> List()
    {
        var result = new List<ProfileInfo>();
        try
        {
            if (!Directory.Exists(Folder))
            {
                return result;
            }

            foreach (var path in Directory.EnumerateFiles(Folder, "*" + Extension))
            {
                try
                {
                    var file = Read(path);
                    result.Add(new ProfileInfo(string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileNameWithoutExtension(path) : file.Name, file.SavedUtc, path));
                }
                catch (ProfileException)
                {
                    // Bozuk dosya listede görünmez.
                }
            }
        }
        catch (IOException)
        {
            // Klasör okunamadı.
        }

        return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string CleanName(string name)
    {
        name = name.Trim();
        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
    }

    /// <summary>Mevcut ayarları verilen adla profil olarak kaydeder (aynı ad varsa üzerine yazar). Notlar ve telefon anahtarı dahil edilmez.</summary>
    public ProfileInfo Save(string name)
    {
        name = CleanName(name);
        if (name.Length == 0)
        {
            throw new ProfileException("name");
        }

        var existing = List().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null && List().Count >= MaxProfiles)
        {
            throw new ProfileException("many");
        }

        var path = existing?.Path ?? Path.Combine(Folder, SafeFileName(name) + Extension);
        var file = Capture(name, includeWidgetData: false, includePairingKey: false);
        Directory.CreateDirectory(Folder);
        WriteFile(path, file);
        return new ProfileInfo(name, file.SavedUtc, path);
    }

    public void Delete(ProfileInfo profile)
    {
        try
        {
            File.Delete(profile.Path);
        }
        catch (IOException)
        {
            // Silinemedi (dosya kullanımda); liste yenilenince yine görünür.
        }
    }

    // ---- Yakalama / dışa aktarma ----

    /// <summary>Şu anki ayarların (ve istenirse widget verisinin) kopyası.</summary>
    public ProfileFile Capture(string name, bool includeWidgetData, bool includePairingKey)
    {
        var copy = Clone(settings.Current);
        if (!includePairingKey)
        {
            copy.MobileToken = string.Empty;
        }

        WidgetData? data = null;
        if (includeWidgetData)
        {
            data = widgets.CloneData();
            data.Reminders = []; // zamanlayıcılar/hatırlatıcılar anlık şeylerdir; taşınmaz
        }

        return new ProfileFile
        {
            Name = CleanName(name),
            AppVersion = appVersion,
            SavedUtc = DateTime.UtcNow,
            Settings = copy,
            Widgets = data,
            HasPairingKey = includePairingKey && !string.IsNullOrEmpty(copy.MobileToken)
        };
    }

    /// <summary>Tüm ayarları (istenirse notlar/listeler ve telefon anahtarı ile) bir dosyaya yazar.</summary>
    public void Export(string path, bool includeWidgetData, bool includePairingKey) =>
        WriteFile(path, Capture(Environment.MachineName, includeWidgetData, includePairingKey));

    // ---- Okuma / uygulama ----

    /// <summary>Dosyayı okur ve doğrular. Hata olursa <see cref="ProfileException"/>.</summary>
    public static ProfileFile Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                throw new ProfileException("big");
            }

            var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path), Json);
            if (file?.Settings is null)
            {
                throw new ProfileException("bad");
            }

            if (file.Format > CurrentFormat)
            {
                throw new ProfileException("newer");
            }

            return file;
        }
        catch (ProfileException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ProfileException("bad");
        }
    }

    /// <summary>
    /// Profili/yedeği uygular. Fan kontrolünün açık/kapalı durumu ve (istenmedikçe) telefon anahtarı mevcut hâliyle korunur.
    /// Dönüş: dosyadaki widget verisi uygulandıysa true (pencereler yeniden kurulmalı).
    /// </summary>
    public bool Apply(ProfileFile file, bool includeWidgetData, bool includePairingKey)
    {
        if (file.Settings is null)
        {
            throw new ProfileException("bad");
        }

        var current = settings.Current;
        var incoming = Clone(file.Settings);
        incoming.FanControlEnabled = current.FanControlEnabled;
        incoming.MobileToken = includePairingKey && file.HasPairingKey && !string.IsNullOrEmpty(file.Settings.MobileToken)
            ? file.Settings.MobileToken
            : current.MobileToken;
        settings.Replace(incoming);

        if (includeWidgetData && file.Widgets is not null)
        {
            var data = JsonSerializer.Deserialize<WidgetData>(JsonSerializer.Serialize(file.Widgets, Json), Json) ?? new WidgetData();
            data.Reminders = [];
            widgets.ReplaceData(data);
            return true;
        }

        return false;
    }

    // ---- Yardımcılar ----

    private static AppSettings Clone(AppSettings source) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, Json), Json) ?? new AppSettings();

    private static void WriteFile(string path, ProfileFile file)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Profil adından geçerli dosya adı üretir (yasak karakterler ve ayrılmış adlar temizlenir).</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (cleaned.Length == 0)
        {
            cleaned = "profil";
        }

        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3"];
        return reserved.Contains(cleaned.ToUpperInvariant()) ? "_" + cleaned : cleaned;
    }
}
