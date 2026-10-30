using System.Diagnostics;
using System.Net.NetworkInformation;

namespace DHud.Services;

public sealed record NetworkReading(
    string? AdapterName,
    double? DownloadBytesPerSecond,
    double? UploadBytesPerSecond,
    long? PingMs);

/// <summary>
/// İnternete çıkan ağ bağlantısının (varsayılan ağ geçidi olan, çalışır durumdaki fiziksel/sanal
/// arabirimler) indirme/yükleme hızını ölçer ve ayrı bir döngüde ping gecikmesini takip eder.
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);
    private const int PingTimeoutMs = 1500;

    private readonly Dictionary<string, (long Received, long Sent)> _previous = [];
    private readonly Stopwatch _clock = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _pingLoop;
    private long? _pingMs;

    /// <summary>Ping atılacak adres; çalışırken değiştirilebilir.</summary>
    public string PingHost { get; set; } = "1.1.1.1";

    public bool PingEnabled { get; set; } = true;

    /// <summary>Kapalıyken arabirimler taranmaz ve hız ölçülmez (yalnızca ping gösteriliyorsa CPU/bellek tasarrufu).</summary>
    public bool Enabled { get; set; } = true;

    public void Start()
    {
        _pingLoop ??= Task.Run(() => PingLoopAsync(_cts.Token));
    }

    /// <summary>Son çağrıdan bu yana geçen sürede aktarılan veriyi hıza çevirir.</summary>
    public NetworkReading Sample()
    {
        if (!Enabled)
        {
            _previous.Clear();
            _clock.Restart();
            return new NetworkReading(null, null, null, PingEnabled ? _pingMs : null);
        }

        var elapsed = _clock.Elapsed.TotalSeconds;
        _clock.Restart();

        long received = 0, sent = 0;
        string? name = null;
        var hasPrevious = elapsed > 0;

        foreach (var nic in GetInternetInterfaces())
        {
            IPInterfaceStatistics stats;
            try
            {
                stats = nic.GetIPStatistics();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            if (_previous.TryGetValue(nic.Id, out var prev))
            {
                // Sayaçlar arabirim yeniden başlatılınca sıfırlanabilir; negatif farkı yok say.
                received += Math.Max(0, stats.BytesReceived - prev.Received);
                sent += Math.Max(0, stats.BytesSent - prev.Sent);
            }
            else
            {
                hasPrevious = false; // yeni arabirim: ilk ölçümde hız hesaplanamaz
            }

            _previous[nic.Id] = (stats.BytesReceived, stats.BytesSent);
            name ??= nic.Description;
        }

        if (name is null)
        {
            return new NetworkReading(null, null, null, PingEnabled ? _pingMs : null);
        }

        return new NetworkReading(
            name,
            hasPrevious ? received / elapsed : null,
            hasPrevious ? sent / elapsed : null,
            PingEnabled ? _pingMs : null);
    }

    private static IEnumerable<NetworkInterface> GetInternetInterfaces()
    {
        NetworkInterface[] all;
        try
        {
            all = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            yield break;
        }

        foreach (var nic in all)
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            // Varsayılan ağ geçidi olmayan arabirimler (Hyper-V/WSL iç ağları vb.) internete çıkmaz.
            bool hasGateway;
            try
            {
                hasGateway = nic.GetIPProperties().GatewayAddresses
                    .Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any));
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            if (hasGateway)
            {
                yield return nic;
            }
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        using var ping = new Ping();
        using var timer = new PeriodicTimer(PingInterval);
        do
        {
            if (!PingEnabled)
            {
                _pingMs = null;
                continue;
            }

            try
            {
                var reply = await ping.SendPingAsync(PingHost, TimeSpan.FromMilliseconds(PingTimeoutMs), cancellationToken: ct)
                    .ConfigureAwait(false);
                _pingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is PingException or ArgumentException or InvalidOperationException)
            {
                // Çözümlenemeyen adres veya ağ yok.
                _pingMs = null;
            }
        }
        while (await WaitAsync(timer, ct).ConfigureAwait(false));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _pingLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Döngü zaten iptal edildi.
        }

        _cts.Dispose();
    }
}
