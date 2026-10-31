using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace dHud.Mobile.Services;

/// <summary>Ana ekran widget'ının göstereceği son değerler.</summary>
public sealed record WidgetData(float? Cpu, float? Gpu, float? Fps, string Pc, long Timestamp);

/// <summary>Widget için son ölçümü saklar (uygulama kapalıyken de widget son değeri gösterebilsin).</summary>
public static class WidgetCache
{
	private const string Key = "widget-data";

	public static void Save(PcSnapshot s)
	{
		try
		{
			var data = new WidgetData(s.Cpu?.Temperature, s.Gpu?.Temperature, s.Fps, s.Pc ?? string.Empty,
				DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			Preferences.Set(Key, JsonSerializer.Serialize(data));
		}
		catch (Exception)
		{
			// Widget önbelleği yazılamadı; widget eski değeri gösterir.
		}
	}

	public static WidgetData? Load()
	{
		try
		{
			var json = Preferences.Get(Key, string.Empty);
			return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<WidgetData>(json);
		}
		catch (Exception)
		{
			return null;
		}
	}
}

/// <summary>Uygulamadan bağımsız, tek seferlik bağlantı: PC'den bir ölçüm alır (widget yenilemesi için).</summary>
public static class WidgetFetcher
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public static async Task<PcSnapshot?> FetchAsync()
	{
		var info = PairingStore.Load();
		if (info is null)
		{
			return null;
		}

		foreach (var host in info.Hosts)
		{
			try
			{
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
				using var tcp = new TcpClient { NoDelay = true };
				await tcp.ConnectAsync(host, info.Port, cts.Token);

				var stream = tcp.GetStream();
				var hello = JsonSerializer.Serialize(new { hello = "dHud", token = info.Token }) + "\n";
				await stream.WriteAsync(Encoding.UTF8.GetBytes(hello), cts.Token);

				using var reader = new StreamReader(stream, Encoding.UTF8);
				for (var i = 0; i < 6; i++)
				{
					var line = await reader.ReadLineAsync(cts.Token);
					if (line is null)
					{
						break;
					}

					using var doc = JsonDocument.Parse(line);
					var root = doc.RootElement;
					if (root.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
					{
						return null; // eşleştirme geçersiz
					}

					if (root.TryGetProperty("ok", out _) || root.TryGetProperty("alert", out _) || root.TryGetProperty("ack", out _))
					{
						continue;
					}

					return root.Deserialize<PcSnapshot>(Json);
				}
			}
			catch (Exception)
			{
				// Bu adres yanıt vermedi; sıradakini dene.
			}
		}

		return null;
	}
}

/// <summary>Uygulama açıkken gelen her ölçümü widget'a da iletir (çok sık güncellenmesin diye seyreltilir).</summary>
public static class WidgetBridge
{
	private static DateTime _last = DateTime.MinValue;

	public static void Push(PcSnapshot s)
	{
		if (DateTime.UtcNow - _last < TimeSpan.FromSeconds(15))
		{
			return;
		}

		_last = DateTime.UtcNow;
		WidgetCache.Save(s);
#if ANDROID
		try
		{
			DHudWidget.UpdateAll(Android.App.Application.Context);
		}
		catch (Exception)
		{
			// Widget yoksa ya da güncellenemediyse uygulama etkilenmez.
		}
#endif
	}

	public static string Describe(WidgetData? data)
	{
		if (data is null)
		{
			return string.Empty;
		}

		var time = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp).ToLocalTime();
		return time.ToString("HH:mm", CultureInfo.CurrentCulture);
	}
}
