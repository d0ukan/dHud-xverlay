using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>
/// Telefondaki Widget'lar sekmesi, gerçek TCP üzerinden: PC'nin sürümü hello yanıtında gelir, veri yalnızca izleyen telefona gider,
/// izleme bitince durur, widget komutları ayar kapalıyken reddedilir. PC'nin gönderdiği JSON Android uygulamasının kendi modeline çözülür (sözleşme).
/// </summary>
[Trait(Cat.Name, Cat.Integration)]
public sealed class WidgetsProtocolTests : IDisposable
{
    private const string Token = "widgets-token";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly TempDataDir _dir = new();
    private readonly SettingsService _settings;
    private readonly HardwareMonitorService _monitor = new();
    private readonly MobileLinkService _link;
    private readonly List<TcpClient> _clients = [];
    private readonly List<(int Count, bool Joined)> _watchEvents = [];

    public WidgetsProtocolTests()
    {
        _settings = new SettingsService(_dir.File("settings.json"));
        _settings.Load();
        _settings.Current.MobileEnabled = true;
        _settings.Current.MobileToken = Token;
        _link = new MobileLinkService(_monitor, _settings, port: 0, discoveryPort: -1)
        {
            CommandHandler = command => Task.FromResult<string?>(command.GetProperty("cmd").GetString() == "media" ? null : "bad")
        };
        _link.WatchersChanged += (count, joined) =>
        {
            lock (_watchEvents)
            {
                _watchEvents.Add((count, joined));
            }
        };
        _link.Apply();
        Assert.True(_link.IsRunning, _link.LastError);
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _link.Dispose();
        _dir.Dispose();
    }

    private async Task<(TcpClient Tcp, StreamReader Reader)> ConnectAsync()
    {
        var tcp = new TcpClient { NoDelay = true };
        _clients.Add(tcp);
        await tcp.ConnectAsync(IPAddress.Loopback, _link.Port);
        await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { hello = "dHud", token = Token }) + "\n"));
        return (tcp, new StreamReader(tcp.GetStream(), Encoding.UTF8));
    }

    private static async Task SendAsync(TcpClient tcp, object command) =>
        await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command) + "\n"));

    private static async Task<JsonElement> ReadUntilAsync(StreamReader reader, string property, int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new IOException("bağlantı kapandı");
            var root = JsonDocument.Parse(line).RootElement.Clone();
            if (root.TryGetProperty(property, out _))
            {
                return root;
            }
        }
    }

    private static async Task<bool> NothingArrivesAsync(StreamReader reader, string property, int ms = 700)
    {
        try
        {
            await ReadUntilAsync(reader, property, ms);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    [Fact]
    public async Task The_hello_reply_carries_the_pc_version_so_the_phone_can_keep_in_step()
    {
        var (_, reader) = await ConnectAsync();
        var hello = await ReadUntilAsync(reader, "ok");

        Assert.True(hello.GetProperty("ok").GetBoolean());
        var app = hello.GetProperty("app").GetString();
        Assert.True(Version.TryParse(app, out var version), app);
        Assert.Equal(UpdateService.CurrentVersionText.TrimStart('v'), version.ToString());
    }

    [Fact]
    public async Task Widget_data_goes_only_to_phones_that_watch_and_stops_when_they_stop()
    {
        var (watcher, watcherReader) = await ConnectAsync();
        var (_, idleReader) = await ConnectAsync();
        await ReadUntilAsync(watcherReader, "ok");
        await ReadUntilAsync(idleReader, "ok");

        await SendAsync(watcher, new { cmd = "watch", on = true });
        var ack = await ReadUntilAsync(watcherReader, "ack");
        Assert.True(ack.GetProperty("ack").GetProperty("ok").GetBoolean());
        Assert.Equal(1, _link.WatcherCount);

        _link.SendWidgets(new { ctl = true, marker = 1 });
        var received = await ReadUntilAsync(watcherReader, "widgets");
        Assert.Equal(1, received.GetProperty("widgets").GetProperty("marker").GetInt32());
        Assert.True(await NothingArrivesAsync(idleReader, "widgets"), "izlemeyen telefon veri almamalı");

        await SendAsync(watcher, new { cmd = "watch", on = false });
        await ReadUntilAsync(watcherReader, "ack");
        Assert.Equal(0, _link.WatcherCount);
        _link.SendWidgets(new { ctl = true, marker = 2 });
        Assert.True(await NothingArrivesAsync(watcherReader, "widgets"), "izleme bitince veri gelmemeli");

        lock (_watchEvents)
        {
            Assert.Equal([(1, true), (0, false)], _watchEvents);
        }
    }

    [Fact]
    public async Task A_phone_that_disconnects_while_watching_stops_the_feed()
    {
        var (tcp, reader) = await ConnectAsync();
        await ReadUntilAsync(reader, "ok");
        await SendAsync(tcp, new { cmd = "watch", on = true });
        await ReadUntilAsync(reader, "ack");
        Assert.Equal(1, _link.WatcherCount);

        tcp.Dispose();
        for (var i = 0; i < 40 && _link.WatcherCount > 0; i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal(0, _link.WatcherCount);
    }

    [Fact]
    public async Task Widget_commands_reach_the_handler_and_are_acknowledged()
    {
        var (tcp, reader) = await ConnectAsync();
        await ReadUntilAsync(reader, "ok");
        await SendAsync(tcp, new { cmd = "media", action = "toggle" });
        var ack = await ReadUntilAsync(reader, "ack");

        Assert.Equal("media", ack.GetProperty("ack").GetProperty("cmd").GetString());
        Assert.True(ack.GetProperty("ack").GetProperty("ok").GetBoolean());
    }

    // ---- Widget servisi (komutlar) ----

    private RemoteWidgetService Service(WidgetService widgets) => new(widgets, _settings, new MediaBridge());

    private static JsonElement Cmd(object value) => JsonSerializer.SerializeToElement(value);

    private static Task StaRunAsync(Func<Task> work)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                work().GetAwaiter().GetResult();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    [Fact]
    public async Task Widget_commands_are_refused_when_the_setting_is_off() => await StaRunAsync(async () =>
    {
        _settings.Current.MobileWidgets = false;
        var service = Service(new WidgetService(_dir.File("w1.json")));
        Assert.Equal("disabled", await service.ApplyAsync(Cmd(new { cmd = "media", action = "toggle" })));
        Assert.Equal("disabled", await service.ApplyAsync(Cmd(new { cmd = "launch", id = "x" })));
        Assert.Equal("disabled", await service.ApplyAsync(Cmd(new { cmd = "clipSend", text = "hi" })));
    });

    [Fact]
    public async Task Launching_only_works_for_items_in_the_launcher_and_never_takes_a_path_from_the_phone() => await StaRunAsync(async () =>
    {
        _settings.Current.MobileWidgets = true;
        var widgets = new WidgetService(_dir.File("w2.json"));
        widgets.Data.Launchers.Add(new LauncherItem { Id = "gone", Name = "Gone", Path = @"C:\definitely\not\here\app.exe" });
        var service = Service(widgets);

        Assert.Equal("nofound", await service.ApplyAsync(Cmd(new { cmd = "launch", id = "nope" })));
        Assert.Equal("nofound", await service.ApplyAsync(Cmd(new { cmd = "launch", id = @"C:\Windows\notepad.exe" })));   // yol verilemez, yalnızca kimlik
        Assert.Equal("missing", await service.ApplyAsync(Cmd(new { cmd = "launch", id = "gone" })));
        Assert.Equal("nofound", await service.ApplyAsync(Cmd(new { cmd = "launch" })));
    });

    [Fact]
    public async Task Bad_media_actions_and_empty_clipboard_text_are_rejected() => await StaRunAsync(async () =>
    {
        _settings.Current.MobileWidgets = true;
        var service = Service(new WidgetService(_dir.File("w3.json")));
        Assert.Equal("bad", await service.ApplyAsync(Cmd(new { cmd = "media", action = "format-c" })));
        Assert.Equal("bad", await service.ApplyAsync(Cmd(new { cmd = "media" })));
        Assert.Equal("bad", await service.ApplyAsync(Cmd(new { cmd = "clipSend", text = "   " })));
        Assert.Equal("bad", await service.ApplyAsync(Cmd(new { cmd = "unknown" })));
    });

    // ---- Veri (feed) ve Android sözleşmesi ----

    private static dHud.Mobile.Services.WidgetsState Read(object payload) =>
        JsonSerializer.Deserialize<dHud.Mobile.Services.WidgetsState>(JsonSerializer.Serialize(payload, Web), Web)!;

    [Fact]
    public async Task The_feed_is_empty_but_for_a_flag_when_the_setting_is_off() => await StaRunAsync(async () =>
    {
        _settings.Current.MobileWidgets = false;
        using var feed = new MobileWidgetsFeed(new WidgetService(_dir.File("w4.json")), _settings, new MediaBridge());
        var state = Read(await feed.BuildAsync());

        Assert.False(state.Ctl);
        Assert.Null(state.Media);
        Assert.Null(state.Launch);
    });

    [Fact]
    public async Task The_feed_json_is_readable_by_the_android_models_and_big_parts_are_sent_only_once() => await StaRunAsync(async () =>
    {
        _settings.Current.MobileWidgets = true;
        var widgets = new WidgetService(_dir.File("w5.json"));
        widgets.Data.Launchers.Add(new LauncherItem { Id = "l1", Name = "Notepad", Path = @"C:\Windows\notepad.exe" });
        widgets.Data.Launchers.Add(new LauncherItem { Id = "l2", Name = "Calc", Path = @"C:\Windows\System32\calc.exe" });
        _settings.Current.Extra(WidgetIds.Clipboard).Enabled = true;

        var history = new ClipboardHistory();
        history.AddText("merhaba");
        history.AddText(new string('x', 500));
        using var feed = new MobileWidgetsFeed(widgets, _settings, new MediaBridge())
        {
            ClipboardProvider = () => history.Items,
            IconProvider = path => path.Contains("notepad") ? [137, 80, 78, 71] : null
        };

        feed.SetWatching(true);
        try
        {
            var first = Read(await feed.BuildAsync());

            Assert.True(first.Ctl);
            Assert.Equal(["Notepad", "Calc"], first.Launch!.Items!.Select(i => i.Name));
            Assert.Equal(["l1"], first.Launch.Icons!.Keys);                      // yalnızca simgesi olan
            Assert.True(first.Clip!.On);
            Assert.Equal(2, first.Clip.Items!.Count);
            Assert.Equal(300, first.Clip.Items[0].T!.Length);                    // uzun metin kırpılır
            Assert.Equal("merhaba", first.Clip.Items[1].T);
            Assert.NotNull(first.Proc!.Rows);
            Assert.NotNull(first.Battery);

            var second = Read(await feed.BuildAsync());
            Assert.Null(second.Launch!.Icons);                                   // simge ikinci kez gönderilmez
            Assert.Equal(2, second.Launch.Items!.Count);

            feed.ForceFull();                                                    // yeni telefon katıldı
            var third = Read(await feed.BuildAsync());
            Assert.Equal(["l1"], third.Launch!.Icons!.Keys);
        }
        finally
        {
            feed.SetWatching(false);
        }
    });

    [Fact]
    public async Task The_clipboard_section_says_when_the_pc_widget_is_off() => await StaRunAsync(async () =>
    {
        _settings.Current.MobileWidgets = true;
        _settings.Current.Extra(WidgetIds.Clipboard).Enabled = false;
        using var feed = new MobileWidgetsFeed(new WidgetService(_dir.File("w6.json")), _settings, new MediaBridge());
        var state = Read(await feed.BuildAsync());

        Assert.False(state.Clip!.On);
        Assert.Empty(state.Clip.Items!);
        Assert.Null(state.Market);   // Borsa kapalıyken internete çıkılmaz, bölüm boş
    });
}
