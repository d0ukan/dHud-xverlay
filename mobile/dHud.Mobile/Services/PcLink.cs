using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace dHud.Mobile.Services;

/// <summary>
/// PC'deki dHud'a TCP ile bağlanır, gelen anlık görüntüleri okur ve grafik için geçmişi tutar.
/// Bağlantı koparsa kendiliğinden yeniden dener; PC'nin birden fazla adresi varsa sırayla dener.
/// </summary>
public sealed class PcLink
{
	public const int HistoryLength = 300;

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan DataTimeout = TimeSpan.FromSeconds(8);

	private CancellationTokenSource? _cts;
	private volatile NetworkStream? _stream;
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	public LinkState State { get; private set; } = LinkState.Idle;
	public PcSnapshot? Latest { get; private set; }

	public List<float?> CpuTemp { get; } = [];
	public List<float?> GpuTemp { get; } = [];

	/// <summary>Durum veya veri değişince ana iş parçacığında tetiklenir.</summary>
	public event Action? Changed;

	/// <summary>PC bir sıcaklık uyarısı gönderdiğinde (ana iş parçacığında) uyarı metniyle tetiklenir.</summary>
	public event Action<string>? AlertReceived;

	/// <summary>PC bir komutu yanıtladığında (ana iş parçacığında): komut adı, başarı, hata kodu (disabled/fanctl/nofan/bad).</summary>
	public event Action<string, bool, string?>? CommandResult;

	public void Start(PairingInfo info)
	{
		Stop();
		_cts = new CancellationTokenSource();
		_ = RunAsync(info, _cts.Token);
	}

	public void Stop()
	{
		_cts?.Cancel();
		_cts = null;
		Latest = null;
		CpuTemp.Clear();
		GpuTemp.Clear();
		SetState(LinkState.Idle);
	}

	private async Task RunAsync(PairingInfo info, CancellationToken ct)
	{
		var delay = TimeSpan.FromSeconds(1);
		var failedRounds = 0;
		while (!ct.IsCancellationRequested)
		{
			SetState(LinkState.Connecting);

			var connected = false;
			foreach (var host in info.Hosts)
			{
				try
				{
					var result = await SessionAsync(host, info, ct);
					connected = result == SessionResult.WasConnected;
					if (result == SessionResult.Rejected)
					{
						SetState(LinkState.Rejected);
						return;
					}
				}
				catch (Exception) when (!ct.IsCancellationRequested)
				{
					// Bu adres yanıt vermedi; sıradakini dene.
				}
				catch (Exception)
				{
					return;
				}

				if (connected)
				{
					break;
				}
			}

			failedRounds = connected ? 0 : failedRounds + 1;

			// Birkaç denemedir bağlanamıyorsa PC'nin IP'si değişmiş olabilir: ağda ara ve yeni adresi kaydet.
			if (failedRounds >= 2)
			{
				PairingInfo? found = null;
				try
				{
					found = await Discovery.FindAsync(info, ct);
				}
				catch (Exception) when (!ct.IsCancellationRequested)
				{
					// Yayın gönderilemedi (ağ yok); normal yeniden denemeye devam.
				}
				catch (Exception)
				{
					return;
				}

				if (found is not null)
				{
					info = found;
					PairingStore.Save(info);
					failedRounds = 0;
					delay = TimeSpan.FromSeconds(1);
					continue;
				}
			}

			SetState(LinkState.Offline);
			try
			{
				await Task.Delay(delay, ct);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			// Başarısız denemelerde bekleme süresi 10 saniyeye kadar uzar; bağlanınca başa döner.
			delay = connected ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 10));
		}
	}

	private enum SessionResult
	{
		Failed,
		WasConnected,
		Rejected
	}

	private async Task<SessionResult> SessionAsync(string host, PairingInfo info, CancellationToken ct)
	{
		using var tcp = new TcpClient { NoDelay = true };
		using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
		{
			connectCts.CancelAfter(ConnectTimeout);
			await tcp.ConnectAsync(host, info.Port, connectCts.Token);
		}

		var stream = tcp.GetStream();
		var hello = JsonSerializer.Serialize(new { hello = "dHud", token = info.Token }) + "\n";
		await stream.WriteAsync(Encoding.UTF8.GetBytes(hello), ct);
		_stream = stream;
		try
		{
			return await ReadLoopAsync(stream, ct);
		}
		finally
		{
			_stream = null;
		}
	}

	/// <summary>PC'ye bir komut satırı yollar (ör. fan değişikliği). Bağlı değilse false. Sonuç <see cref="CommandResult"/> ile gelir.</summary>
	public async Task<bool> SendCommandAsync(object command)
	{
		var stream = _stream;
		if (stream is null)
		{
			return false;
		}

		var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command, Json) + "\n");
		await _writeLock.WaitAsync();
		try
		{
			await stream.WriteAsync(bytes);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async Task<SessionResult> ReadLoopAsync(NetworkStream stream, CancellationToken ct)
	{
		using var reader = new StreamReader(stream, Encoding.UTF8);
		var gotData = false;
		while (true)
		{
			string? line;
			try
			{
				using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				readCts.CancelAfter(DataTimeout);
				line = await reader.ReadLineAsync(readCts.Token);
			}
			catch (OperationCanceledException) when (!ct.IsCancellationRequested)
			{
				return gotData ? SessionResult.WasConnected : SessionResult.Failed; // veri akışı durdu
			}

			if (line is null)
			{
				return gotData ? SessionResult.WasConnected : SessionResult.Failed;
			}

			using var doc = JsonDocument.Parse(line);
			if (doc.RootElement.TryGetProperty("alert", out var alert))
			{
				var text = alert.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
				if (!string.IsNullOrEmpty(text))
				{
					MainThread.BeginInvokeOnMainThread(() => AlertReceived?.Invoke(text));
				}

				continue;
			}

			if (doc.RootElement.TryGetProperty("ack", out var ack))
			{
				var cmd = ack.TryGetProperty("cmd", out var c) ? c.GetString() ?? string.Empty : string.Empty;
				var success = ack.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
				var err = ack.TryGetProperty("err", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
				MainThread.BeginInvokeOnMainThread(() => CommandResult?.Invoke(cmd, success, err));
				continue;
			}

			if (doc.RootElement.TryGetProperty("ok", out var ok))
			{
				if (!ok.GetBoolean())
				{
					return SessionResult.Rejected;
				}

				continue;
			}

			var snapshot = doc.RootElement.Deserialize<PcSnapshot>(Json);
			if (snapshot is null)
			{
				continue;
			}

			gotData = true;
			MainThread.BeginInvokeOnMainThread(() => OnSnapshot(snapshot));
		}
	}

	private void OnSnapshot(PcSnapshot snapshot)
	{
		Latest = snapshot;
		AddHistory(CpuTemp, snapshot.Cpu?.Temperature);
		AddHistory(GpuTemp, snapshot.Gpu?.Temperature);
		State = LinkState.Connected;
		Changed?.Invoke();
	}

	private static void AddHistory(List<float?> history, float? value)
	{
		history.Add(value);
		if (history.Count > HistoryLength)
		{
			history.RemoveAt(0);
		}
	}

	private void SetState(LinkState state)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (State != state)
			{
				State = state;
				Changed?.Invoke();
			}
		});
	}
}
