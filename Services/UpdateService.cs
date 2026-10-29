using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CpuGpuOverlay.Services;

/// <summary>Bulunan yeni sürümün bilgileri.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? DownloadUrl, string Notes);

/// <summary>
/// GitHub Releases üzerinden güncelleme denetler ve indirir. Depo public olmalıdır (token gerekmez).
/// Release'te <see cref="AssetName"/> adlı tek dosyalık exe bulunur.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "d0ukan/dHud-xverlay";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    private const string AssetName = "CpuGpuOverlay.exe";
    private const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>Yeniden başlatılan örneğin eski örneğin mutex'ini bırakmasını beklemesi için argüman.</summary>
    public const string RestartedArgument = "--restarted";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Çalışan uygulamanın sürümü (ör. 1.0.0).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static string CurrentVersionText => $"v{CurrentVersion}";

    /// <summary>
    /// Yalnızca release'in tek dosyalık exe'si kendini değiştirebilir. Geliştirme derlemesinde
    /// (yanında CpuGpuOverlay.dll olan) kullanıcı yalnızca bilgilendirilir.
    /// </summary>
    public static bool CanSelfUpdate =>
        Environment.ProcessPath is not null &&
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "CpuGpuOverlay.dll"));

    /// <summary>Daha yeni bir sürüm varsa bilgisini döner; yoksa veya ağ hatasında null.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(LatestApi, ct);
        response.EnsureSuccessStatusCode();

        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (release?.TagName is null || !TryParseVersion(release.TagName, out var latest) || latest <= CurrentVersion)
        {
            return null;
        }

        var asset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase));
        return new UpdateInfo(latest, release.TagName, release.HtmlUrl ?? ReleasesUrl,
            asset?.DownloadUrl, release.Body ?? string.Empty);
    }

    /// <summary>
    /// Yeni exe'yi indirir ve çalışan exe'nin yerine koyar (çalışan exe yeniden adlandırılabilir).
    /// Başarılı olursa çağıran uygulamayı yeniden başlatmalıdır.
    /// </summary>
    public async Task InstallAsync(UpdateInfo update, CancellationToken ct = default)
    {
        if (update.DownloadUrl is null || Environment.ProcessPath is not { } exePath)
        {
            throw new InvalidOperationException("Bu sürümde indirilebilir dosya yok.");
        }

        var newPath = exePath + ".new";
        var oldPath = exePath + ".old";

        await using (var source = await Http.GetStreamAsync(update.DownloadUrl, ct))
        await using (var target = File.Create(newPath))
        {
            await source.CopyToAsync(target, ct);
        }

        // Boş/yarım dosya çalışan exe'nin yerine geçmesin.
        if (new FileInfo(newPath).Length < 100_000)
        {
            File.Delete(newPath);
            throw new InvalidOperationException("İndirilen dosya geçersiz.");
        }

        if (File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        File.Move(exePath, oldPath);
        try
        {
            File.Move(newPath, exePath);
        }
        catch
        {
            File.Move(oldPath, exePath);
            throw;
        }
    }

    /// <summary>Önceki güncellemeden kalan .old dosyasını temizler.</summary>
    public static void CleanupOldFiles()
    {
        try
        {
            if (Environment.ProcessPath is { } exePath)
            {
                File.Delete(exePath + ".old");
                File.Delete(exePath + ".new");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Eski güncelleme dosyası silinemedi: {ex.Message}");
        }
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0)
        {
            text = text[..dash];
        }

        if (Version.TryParse(text, out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            return true;
        }

        version = new Version();
        return false;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"dHud/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
    }
}
