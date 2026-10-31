using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace dHud.Mobile.Services;

public sealed record AppUpdate(string Tag, string ApkUrl);

/// <summary>
/// GitHub Releases'teki en son sürümü kontrol eder (PC uygulamasıyla aynı sürüm numarası) ve yeni APK'yı indirip
/// Android paket yükleyicisine verir. Android sessiz kurulum yapmaz; kullanıcı kurulum ekranında onay verir.
/// </summary>
public static partial class AppUpdater
{
	private const string LatestReleaseUrl = "https://api.github.com/repos/d0ukan/dHud-xverlay/releases/latest";

	[GeneratedRegex(@"^dHud-\d+(\.\d+)*\.apk$")]
	private static partial Regex ApkAsset();

	private static readonly HttpClient Http = CreateClient();

	private static HttpClient CreateClient()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
		client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dHud-Mobile", AppInfo.Current.VersionString));
		client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
		return client;
	}

	/// <summary>Daha yeni bir sürüm ve APK dosyası varsa döner; yoksa null.</summary>
	public static async Task<AppUpdate?> CheckAsync(CancellationToken ct = default)
	{
		using var response = await Http.GetAsync(LatestReleaseUrl, ct);
		response.EnsureSuccessStatusCode();
		using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

		var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
		if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
		    || !Version.TryParse(AppInfo.Current.VersionString, out var current)
		    || latest <= current)
		{
			return null;
		}

		foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
		{
			var name = asset.GetProperty("name").GetString() ?? string.Empty;
			if (ApkAsset().IsMatch(name))
			{
				return new AppUpdate(tag, asset.GetProperty("browser_download_url").GetString()!);
			}
		}

		return null;
	}

	/// <summary>APK'yı indirir. Kurulum için <see cref="Install"/> çağrılır.</summary>
	public static async Task<string> DownloadAsync(AppUpdate update, IProgress<double>? progress, CancellationToken ct = default)
	{
		var directory = Path.Combine(FileSystem.CacheDirectory, "update");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, "dHud.apk");

		using var response = await Http.GetAsync(update.ApkUrl, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		var total = response.Content.Headers.ContentLength ?? 0;

		await using var source = await response.Content.ReadAsStreamAsync(ct);
		await using var target = File.Create(path);
		var buffer = new byte[81920];
		long done = 0;
		int read;
		while ((read = await source.ReadAsync(buffer, ct)) > 0)
		{
			await target.WriteAsync(buffer.AsMemory(0, read), ct);
			done += read;
			if (total > 0)
			{
				progress?.Report((double)done / total);
			}
		}

		return path;
	}

	/// <summary>İndirilen APK'yı Android kurulum ekranında açar. Kurulum izni yoksa ayar ekranını açıp false döner.</summary>
	public static bool Install(string apkPath)
	{
#if ANDROID
		var context = Android.App.Application.Context;
		if (!context.PackageManager!.CanRequestPackageInstalls())
		{
			var settings = new Android.Content.Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
				Android.Net.Uri.Parse("package:" + context.PackageName));
			settings.AddFlags(Android.Content.ActivityFlags.NewTask);
			context.StartActivity(settings);
			return false;
		}

		var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
			context, context.PackageName + ".fileprovider", new Java.IO.File(apkPath));
		var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
		intent.SetDataAndType(uri, "application/vnd.android.package-archive");
		intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.NewTask);
		context.StartActivity(intent);
#endif
		return true;
	}
}
