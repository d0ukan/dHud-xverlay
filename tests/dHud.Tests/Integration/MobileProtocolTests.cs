using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>
/// Telefon bağlantısı: gerçek TCP (loopback, boş port) üzerinden. Hem protokol sözleşmesi (alan adları, Android modeliyle uyum)
/// hem de güvenlik: yetkisiz veya bozuk istemci hiçbir şey yaptıramamalı.
/// </summary>
[Trait(Cat.Name, Cat.Integration)]
public sealed class MobileProtocolTests : IDisposable
{
    private const string Token = "test-token-123";

    private readonly TempDataDir _dir = new();
    private readonly SettingsService _settings;
    private readonly HardwareMonitorService _monitor = new();
    private readonly FanControlService _fanControl;
    private readonly RemoteFanService _remote;
    private readonly MobileLinkService _link;
    private readonly List<TcpClient> _clients = [];

    public MobileProtocolTests()
    {
        _settings = new SettingsService(_dir.File("settings.json"));
        _settings.Load();
        _settings.Current.MobileEnabled = true;
        _settings.Current.MobileToken = Token;

        _fanControl = new FanControlService(_monitor, _settings);
        _remote = new RemoteFanService(_monitor, _settings, _fanControl);
        _link = new MobileLinkService(_monitor, _settings, port: 0, discoveryPort: -1)
        {
            FanProvider = () => _remote.BuildState(),
            CommandHandler = command => Task.FromResult(_remote.Apply(command))
        };
        _link.Apply();
        Assert.True(_link.IsRunning, _link.LastError);
        Assert.NotEqual(0, _link.Port);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _link.Dispose();
        _fanControl.Dispose();
        _dir.Dispose();
    }

    private sealed class Peer(TcpClient tcp)
    {
        private readonly StreamReader _reader = new(tcp.GetStream(), Encoding.UTF8);

        public TcpClient Tcp { get; } = tcp;

        public async Task SendAsync(string line)
        {
            await Tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        }

        /// <summary>Bir satır okur; süre dolarsa veya bağlantı kapanırsa null.</summary>
        public async Task<string?> ReadAsync(int milliseconds = 3000)
        {
            using var cts = new CancellationTokenSource(milliseconds);
            try
            {
                return await _reader.ReadLineAsync(cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                return null;
            }
        }

        public async Task<JsonElement?> ReadJsonAsync(int milliseconds = 3000) =>
            await ReadAsync(milliseconds) is { } line ? JsonDocument.Parse(line).RootElement.Clone() : null;
    }

    private async Task<Peer> ConnectAsync(string token = Token, bool sayHello = true)
    {
        var tcp = new TcpClient { NoDelay = true };
        _clients.Add(tcp);
        await tcp.ConnectAsync(IPAddress.Loopback, _link.Port);
        var peer = new Peer(tcp);
        if (sayHello)
        {
            await peer.SendAsync(JsonSerializer.Serialize(new { hello = "dHud", token }));
        }

        return peer;
    }

    /// <summary>Bağlanır, "ok" yanıtını doğrular. Sunucu ilk anlık görüntüyü hemen yollayabilir; ondan önce tüm komutlar için hazırdır.</summary>
    private async Task<Peer> PairedAsync()
    {
        var peer = await ConnectAsync();
        var hello = await peer.ReadJsonAsync();
        Assert.True(hello?.GetProperty("ok").GetBoolean());
        await WaitForClientsAsync(1 + _clients.Count - 1);
        return peer;
    }

    private async Task WaitForClientsAsync(int count)
    {
        for (var i = 0; i < 100 && _link.ClientCount < count; i++)
        {
            await Task.Delay(20);
        }

        Assert.True(_link.ClientCount >= count, $"{count} istemci bekleniyordu, {_link.ClientCount} var");
    }

    private static SensorSnapshot Snapshot() => new(
        new HardwareReading("Test CPU", Temperature: 57, Load: 18, ClockMhz: 4350, PowerWatts: 61, FanRpm: 1120, CoreMaxLoad: 46, CoreAvgLoad: 19),
        new HardwareReading("Test GPU", Temperature: 61, Load: 42, MemoryUsedGb: 5.1f, MemoryTotalGb: 10f),
        new HardwareReading("Memory", Load: 46, MemoryUsedGb: 14.7f, MemoryTotalGb: 32f),
        new NetworkReading("Ethernet", 4_300_000, 380_000, 14),
        new DiskReading("C:", 61, 568, 931));

    // ---- Sözleşme ----

    [Fact]
    public async Task A_paired_client_receives_snapshots_that_the_android_models_can_read()
    {
        var peer = await PairedAsync();
        _monitor.PublishForTest(Snapshot());

        JsonElement? json;
        do
        {
            json = await peer.ReadJsonAsync();
        }
        while (json is { } j && !j.TryGetProperty("cpu", out _));

        Assert.NotNull(json);
        // Android uygulamasının kendi model sınıflarıyla (Web seçenekleri: camelCase) çözülür.
        var snapshot = json.Value.Deserialize<dHud.Mobile.Services.PcSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(57, snapshot.Cpu!.Temperature);
        Assert.Equal(46, snapshot.Cpu.CoreMaxLoad);
        Assert.Equal("Test GPU", snapshot.Gpu!.Name);
        Assert.Equal(5.1f, snapshot.Gpu.MemoryUsedGb);
        Assert.Equal(46, snapshot.Ram!.Load);
        Assert.Equal(4_300_000, snapshot.Net!.DownloadBytesPerSecond);
        Assert.Equal(61, snapshot.Disk!.UsedPercent);
        Assert.NotNull(snapshot.FanCtl);
        Assert.False(snapshot.FanCtl!.Enabled);
        Assert.False(snapshot.FanCtl.Remote);
        Assert.Empty(snapshot.Fans ?? []);
    }

    [Fact]
    public async Task Alerts_reach_the_phone_with_their_kind()
    {
        var peer = await PairedAsync();
        _link.SendAlert("CPU çok sıcak", "temp");
        _link.SendAlert("Çay hazır", "reminder");

        var kinds = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var json = await peer.ReadJsonAsync();
            Assert.True(json?.TryGetProperty("alert", out var alert));
            kinds.Add(json!.Value.GetProperty("alert").GetProperty("kind").GetString()!);
        }

        Assert.Equal(["temp", "reminder"], kinds);
    }

    // ---- Güvenlik ----

    [Fact]
    public async Task A_wrong_token_is_refused_and_disconnected()
    {
        var peer = await ConnectAsync(token: "yanlis");
        var reply = await peer.ReadJsonAsync();
        Assert.False(reply?.GetProperty("ok").GetBoolean());
        Assert.Null(await peer.ReadAsync()); // bağlantı kapandı
        Assert.Equal(0, _link.ClientCount);
    }

    [Fact]
    public async Task A_command_sent_without_pairing_is_never_executed()
    {
        var peer = await ConnectAsync(sayHello: false);
        await peer.SendAsync("""{"cmd":"fanControl","enabled":true}""");
        var reply = await peer.ReadJsonAsync();

        Assert.False(reply?.GetProperty("ok").GetBoolean());
        Assert.False(_settings.Current.FanControlEnabled);
        Assert.Equal(0, _link.ClientCount);
    }

    [Fact]
    public async Task An_oversized_hello_line_is_dropped()
    {
        var peer = await ConnectAsync(sayHello: false);
        await peer.Tcp.GetStream().WriteAsync(new byte[2000].Select(_ => (byte)'a').ToArray());
        Assert.Null((await peer.ReadAsync()) is { } line && line.Contains("\"ok\":true") ? line : null);
        Assert.Equal(0, _link.ClientCount);
    }

    [Fact]
    public async Task The_ninth_device_is_refused()
    {
        var peers = new List<Peer>();
        for (var i = 0; i < 8; i++)
        {
            var peer = await ConnectAsync();
            Assert.True((await peer.ReadJsonAsync())?.GetProperty("ok").GetBoolean());
            peers.Add(peer);
        }

        await WaitForClientsAsync(8);
        var ninth = await ConnectAsync();
        Assert.False((await ninth.ReadJsonAsync())?.GetProperty("ok").GetBoolean());
        Assert.Equal(8, _link.ClientCount);
    }

    // ---- Komutlar (ack) ----

    private async Task<(bool Ok, string? Error)> CommandAsync(Peer peer, string json)
    {
        await peer.SendAsync(json);
        while (true)
        {
            var line = await peer.ReadJsonAsync();
            Assert.NotNull(line);
            if (line.Value.TryGetProperty("ack", out var ack))
            {
                var error = ack.TryGetProperty("err", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                return (ack.GetProperty("ok").GetBoolean(), error);
            }
        }
    }

    [Fact]
    public async Task Phone_fan_commands_are_refused_until_the_pc_allows_them()
    {
        var peer = await PairedAsync();

        Assert.Equal((false, "disabled"), await CommandAsync(peer, """{"cmd":"fanControl","enabled":true}"""));
        Assert.False(_settings.Current.FanControlEnabled);

        _settings.Current.MobileFanControl = true;
        Assert.Equal((true, null), await CommandAsync(peer, """{"cmd":"fanControl","enabled":true}"""));
        Assert.True(_settings.Current.FanControlEnabled);
        Assert.Equal((true, null), await CommandAsync(peer, """{"cmd":"fanControl","enabled":false}"""));
        Assert.False(_settings.Current.FanControlEnabled);
    }

    [Fact]
    public async Task Fan_commands_need_fan_control_to_be_on_and_a_known_target()
    {
        var peer = await PairedAsync();
        _settings.Current.MobileFanControl = true;

        Assert.Equal((false, "fanctl"), await CommandAsync(peer, """{"cmd":"fan","id":"x","mode":"auto"}"""));

        _settings.Current.FanControlEnabled = true;
        Assert.Equal((false, "nofan"), await CommandAsync(peer, """{"cmd":"fan","id":"/olmayan/fan","mode":"auto"}"""));
        Assert.Equal((false, "bad"), await CommandAsync(peer, """{"cmd":"profile","name":"yok"}"""));
        Assert.Equal((false, "nofan"), await CommandAsync(peer, """{"cmd":"fanAll","preset":"quiet"}"""));
    }

    [Theory]
    [InlineData("""{"cmd":"rm -rf"}""")]
    [InlineData("""{"cmd":"fan"}""")]
    [InlineData("""{"cmd":"fanControl"}""")]
    [InlineData("""{"cmd":"fanControl","enabled":"yes"}""")]
    [InlineData("""{"nocmd":1}""")]
    [InlineData("[1,2,3]")]
    [InlineData("kesinlikle json degil")]
    public async Task Malformed_commands_get_a_negative_ack_and_never_crash_the_server(string line)
    {
        var peer = await PairedAsync();
        _settings.Current.MobileFanControl = true;
        _settings.Current.FanControlEnabled = true;

        await peer.SendAsync(line);
        var ack = await ReadAckAsync(peer);
        Assert.False(ack);

        // Sunucu hâlâ sağlam: geçerli bir komut yine yanıtlanır.
        Assert.Equal((true, null), await CommandAsync(peer, """{"cmd":"fanControl","enabled":true}"""));
    }

    private static async Task<bool> ReadAckAsync(Peer peer)
    {
        while (true)
        {
            var json = await peer.ReadJsonAsync();
            Assert.NotNull(json);
            if (json.Value.ValueKind == JsonValueKind.Object && json.Value.TryGetProperty("ack", out var ack))
            {
                return ack.GetProperty("ok").GetBoolean();
            }
        }
    }

    [Fact]
    public async Task Fan_state_tells_the_phone_whether_changes_are_allowed()
    {
        var peer = await PairedAsync();
        _settings.Current.MobileFanControl = true;
        _settings.Current.FanControlEnabled = true;
        _monitor.PublishForTest(Snapshot());

        JsonElement? json;
        do
        {
            json = await peer.ReadJsonAsync();
        }
        while (json is { } j && !j.TryGetProperty("fanCtl", out _));

        var ctl = json!.Value.GetProperty("fanCtl");
        Assert.True(ctl.GetProperty("remote").GetBoolean());
        Assert.True(ctl.GetProperty("enabled").GetBoolean());
    }
}
