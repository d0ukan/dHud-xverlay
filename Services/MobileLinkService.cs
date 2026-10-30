using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DHud.Services;

/// <summary>
/// dHud mobil uygulaması için yerel ağda dinleyen küçük TCP sunucusu. Yalnızca ayardan açılınca çalışır.
/// Protokol (satır tabanlı JSON): istemci <c>{"hello":"dHud","token":"..."}</c> gönderir, sunucu <c>{"ok":true,...}</c>
/// yanıtlar ve ardından her ölçümde bir satır anlık görüntü yollar. Yanlış anahtarla gelen bağlantı kapatılır.
/// </summary>
public sealed class MobileLinkService : IDisposable
{
    public const int Port = 47821;

    private const int ProtocolVersion = 1;
    private const int MaxClients = 8;
    private const int MaxHelloBytes = 512;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HardwareMonitorService _monitor;
    private readonly SettingsService _settings;
    private readonly object _gate = new();
    private readonly List<Client> _clients = [];

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public MobileLinkService(HardwareMonitorService monitor, SettingsService settings)
    {
        _monitor = monitor;
        _settings = settings;
        _monitor.SnapshotUpdated += OnSnapshot;
    }

    /// <summary>Dinleme başlatılamadıysa nedeni (ör. port başka uygulamada kullanımda).</summary>
    public string? LastError { get; private set; }

    public bool IsRunning => _listener is not null;

    public int ClientCount
    {
        get
        {
            lock (_gate)
            {
                return _clients.Count;
            }
        }
    }

    /// <summary>Ayara göre dinlemeyi başlatır veya durdurur. Ayar her değiştiğinde çağrılabilir.</summary>
    public void Apply()
    {
        if (_settings.Current.MobileEnabled)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>Eşleştirme anahtarını yeniler; bağlı tüm cihazların bağlantısı kesilir ve yeniden eşleşmeleri gerekir.</summary>
    public void ResetPairing()
    {
        _settings.Current.MobileToken = NewToken();
        _settings.Save();
        DisconnectAll();
    }

    /// <summary>
    /// Mobil uygulamanın QR ile okuyacağı adres. Birden fazla yerel adres varsa virgülle ayrılır;
    /// uygulama sırayla dener. Yerel ağ adresi bulunamazsa null.
    /// </summary>
    public string? BuildPairingUri()
    {
        var addresses = GetLocalAddresses();
        if (addresses.Count == 0)
        {
            return null;
        }

        var token = EnsureToken();
        return $"dhud://pair?h={string.Join(',', addresses)}&p={Port}" +
               $"&t={Uri.EscapeDataString(token)}&n={Uri.EscapeDataString(Environment.MachineName)}";
    }

    public List<string> GetLocalAddresses()
    {
        var result = new List<(string Address, bool HasGateway)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var props = nic.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                 && !g.Address.Equals(IPAddress.Any));
                foreach (var unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(unicast.Address))
                    {
                        result.Add((unicast.Address.ToString(), hasGateway));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Yerel adresler okunamadı: {ex}");
        }

        // Ağ geçidi olan (gerçek ağa bağlı) arabirimler önce gelir; sanal adaptörler sona kalır.
        return result.OrderByDescending(a => a.HasGateway).Select(a => a.Address).Distinct().ToList();
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] is >= 16 and <= 31)
               || (b[0] == 192 && b[1] == 168);
    }

    private string EnsureToken()
    {
        if (string.IsNullOrEmpty(_settings.Current.MobileToken))
        {
            _settings.Current.MobileToken = NewToken();
            _settings.Save();
        }

        return _settings.Current.MobileToken;
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        EnsureToken();
        try
        {
            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            _listener = listener;
            _cts = new CancellationTokenSource();
            LastError = null;
            _ = AcceptLoopAsync(listener, _cts.Token);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"Mobil bağlantı dinlenemedi: {ex}");
        }
    }

    private void Stop()
    {
        LastError = null;
        if (_listener is null)
        {
            return;
        }

        _cts?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // Zaten kapanmış.
        }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
        DisconnectAll();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcp = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(tcp, ct);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Kapanış.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Mobil bağlantı kabul döngüsü durdu: {ex}");
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        Client? client = null;
        try
        {
            tcp.NoDelay = true;
            var stream = tcp.GetStream();

            string? hello;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(HelloTimeout);
                hello = await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);
            }

            if (!IsValidHello(hello) || ClientCount >= MaxClients)
            {
                await WriteAsync(stream, Encoding.UTF8.GetBytes("{\"ok\":false}\n"), ct).ConfigureAwait(false);
                return;
            }

            client = new Client(tcp, stream);
            lock (_gate)
            {
                _clients.Add(client);
            }

            var ok = JsonSerializer.SerializeToUtf8Bytes(
                new { ok = true, v = ProtocolVersion, pc = Environment.MachineName }, Json);
            await WriteAsync(stream, Append(ok), ct).ConfigureAwait(false);
            if (_monitor.Latest is { } latest)
            {
                await WriteAsync(stream, Serialize(latest), ct).ConfigureAwait(false);
            }

            // İstemci bağlantıyı kapatana kadar bekle; veri OnSnapshot ile gönderilir.
            var buffer = new byte[64];
            while (await stream.ReadAsync(buffer, ct).ConfigureAwait(false) > 0)
            {
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Mobil istemci bağlantısı sona erdi: {ex.Message}");
        }
        finally
        {
            if (client is not null)
            {
                lock (_gate)
                {
                    _clients.Remove(client);
                }
            }

            tcp.Dispose();
        }
    }

    private bool IsValidHello(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.GetProperty("hello").GetString() != "dHud")
            {
                return false;
            }

            var given = Encoding.UTF8.GetBytes(root.GetProperty("token").GetString() ?? string.Empty);
            var expected = Encoding.UTF8.GetBytes(_settings.Current.MobileToken);
            return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(given, expected);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < MaxHelloBytes)
        {
            if (await stream.ReadAsync(one, ct).ConfigureAwait(false) == 0)
            {
                return null;
            }

            if (one[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            bytes.Add(one[0]);
        }

        return null;
    }

    /// <summary>Her ölçümde (arka plan thread'inden) bağlı cihazlara anlık görüntüyü yollar.</summary>
    private void OnSnapshot(SensorSnapshot snapshot)
    {
        Client[] targets;
        lock (_gate)
        {
            if (_clients.Count == 0)
            {
                return;
            }

            targets = [.. _clients];
        }

        var data = Serialize(snapshot);
        foreach (var client in targets)
        {
            // Önceki gönderim bitmediyse (yavaş cihaz) bu kare atlanır; ölçümler birikmez.
            if (Interlocked.Exchange(ref client.Busy, 1) == 1)
            {
                continue;
            }

            _ = SendAsync(client, data);
        }
    }

    private async Task SendAsync(Client client, byte[] data)
    {
        try
        {
            using var timeout = new CancellationTokenSource(SendTimeout);
            await WriteAsync(client.Stream, data, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Yazılamayan istemci: bağlantıyı kapat; HandleClientAsync listeden çıkarır.
            client.Tcp.Dispose();
        }
        finally
        {
            Volatile.Write(ref client.Busy, 0);
        }
    }

    private static byte[] Serialize(SensorSnapshot s) =>
        Append(JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                v = ProtocolVersion,
                pc = Environment.MachineName,
                ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                cpu = s.Cpu,
                gpu = s.Gpu,
                ram = s.Memory,
                net = s.Network
            }, Json));

    private static byte[] Append(byte[] json)
    {
        var result = new byte[json.Length + 1];
        json.CopyTo(result, 0);
        result[^1] = (byte)'\n';
        return result;
    }

    private static async Task WriteAsync(NetworkStream stream, byte[] data, CancellationToken ct) =>
        await stream.WriteAsync(data, ct).ConfigureAwait(false);

    private void DisconnectAll()
    {
        Client[] all;
        lock (_gate)
        {
            all = [.. _clients];
            _clients.Clear();
        }

        foreach (var client in all)
        {
            client.Tcp.Dispose();
        }
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshot;
        Stop();
    }

    private sealed class Client(TcpClient tcp, NetworkStream stream)
    {
        public TcpClient Tcp { get; } = tcp;
        public NetworkStream Stream { get; } = stream;
        public int Busy;
    }
}
