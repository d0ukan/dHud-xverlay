using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace dHud.Mobile.Services;

/// <summary>
/// PC'nin IP adresi değiştiyse (yönlendirici yeniden dağıttı vb.) PC'yi yerel ağda arar: UDP yayını ile
/// "dHud nerede?" diye sorar, yalnızca eşleştirme anahtarını bilen PC cevap verir.
/// </summary>
public static class Discovery
{
	private const int DiscoveryPort = 47822;
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

	/// <summary>PC bulunursa yeni adresiyle güncellenmiş eşleşme bilgisini döner; bulunamazsa null.</summary>
	public static async Task<PairingInfo?> FindAsync(PairingInfo info, CancellationToken ct)
	{
		var tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("dHud:" + info.Token)))[..16];
		var message = Encoding.UTF8.GetBytes("dHud? " + tag);

		using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
		udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

		// Genel yayın ve bilinen adreslerin alt ağı (a.b.c.255) için ayrı ayrı sor.
		var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
		foreach (var host in info.Hosts)
		{
			if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
			{
				var b = ip.GetAddressBytes();
				targets.Add(new IPAddress([b[0], b[1], b[2], 255]));
			}
		}

		foreach (var target in targets)
		{
			try
			{
				await udp.SendAsync(message, new IPEndPoint(target, DiscoveryPort), ct);
			}
			catch (SocketException)
			{
				// Bu hedefe gönderilemedi; diğerlerini dene.
			}
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(Timeout);
		try
		{
			while (true)
			{
				var reply = await udp.ReceiveAsync(timeout.Token);
				using var doc = JsonDocument.Parse(reply.Buffer);
				var root = doc.RootElement;
				if (!root.TryGetProperty("tag", out var echoed) || echoed.GetString() != tag)
				{
					continue;
				}

				var port = root.TryGetProperty("port", out var p) && p.TryGetInt32(out var value) ? value : info.Port;
				var address = reply.RemoteEndPoint.Address.ToString();
				var hosts = new[] { address }.Concat(info.Hosts.Where(h => h != address)).ToArray();
				return info with { Hosts = hosts, Port = port };
			}
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			return null; // süre doldu, PC cevap vermedi
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
