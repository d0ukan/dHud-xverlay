using System.Text.Json;
using System.Web;

namespace dHud.Mobile.Services;

/// <summary>Eşleştirilmiş PC'nin adresleri, portu ve anahtarı.</summary>
public sealed record PairingInfo(string[] Hosts, int Port, string Token, string Name);

public static class PairingStore
{
	private const string Key = "pairing";
	private const int DefaultPort = 47821;

	public static PairingInfo? Load()
	{
		try
		{
			var json = Preferences.Get(Key, string.Empty);
			return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<PairingInfo>(json);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static void Save(PairingInfo info) => Preferences.Set(Key, JsonSerializer.Serialize(info));

	public static void Clear() => Preferences.Remove(Key);

	/// <summary>PC'deki QR kodun içeriğini çözer: dhud://pair?h=ip1,ip2&amp;p=47821&amp;t=anahtar&amp;n=PC-adı</summary>
	public static PairingInfo? Parse(string text)
	{
		if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "dhud")
		{
			return null;
		}

		var query = HttpUtility.ParseQueryString(uri.Query);
		var hosts = (query["h"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var token = query["t"];
		if (hosts.Length == 0 || string.IsNullOrEmpty(token))
		{
			return null;
		}

		var port = int.TryParse(query["p"], out var p) ? p : DefaultPort;
		return new PairingInfo(hosts, port, token, query["n"] ?? hosts[0]);
	}

	/// <summary>QR okunamadığında elle girilen adres ("192.168.0.26" veya "192.168.0.26:47821") ve kod.</summary>
	public static PairingInfo? FromManual(string address, string token)
	{
		address = address.Trim();
		token = token.Trim();
		if (address.Length == 0 || token.Length == 0)
		{
			return null;
		}

		var port = DefaultPort;
		var colon = address.LastIndexOf(':');
		if (colon > 0 && int.TryParse(address[(colon + 1)..], out var p))
		{
			port = p;
			address = address[..colon];
		}

		return new PairingInfo([address], port, token, address);
	}
}
