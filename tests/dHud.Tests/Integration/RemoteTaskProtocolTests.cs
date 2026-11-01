using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>
/// Telefondan görev yönetimi, gerçek TCP üzerinden: telefon bağlanınca görev özetini alır, PC'de değişince yenisi gelir,
/// komutlar işleyiciye ulaşır ve yanıtlanır. PC'nin gönderdiği JSON Android uygulamasının kendi modeline çözülür (sözleşme).
/// </summary>
[Trait(Cat.Name, Cat.Integration)]
public sealed class RemoteTaskProtocolTests : IDisposable
{
    private const string Token = "task-token";

    private readonly TempDataDir _dir = new();
    private readonly SettingsService _settings;
    private readonly HardwareMonitorService _monitor = new();
    private readonly MobileLinkService _link;
    private readonly List<TcpClient> _clients = [];
    private readonly List<JsonElement> _received = [];
    private object _state = new { enabled = true, todos = new List<object>(), reminders = new List<object>() };

    public RemoteTaskProtocolTests()
    {
        _settings = new SettingsService(_dir.File("settings.json"));
        _settings.Load();
        _settings.Current.MobileEnabled = true;
        _settings.Current.MobileToken = Token;
        _link = new MobileLinkService(_monitor, _settings, port: 0, discoveryPort: -1)
        {
            TasksProvider = () => _state,
            CommandHandler = command =>
            {
                lock (_received)
                {
                    _received.Add(command.Clone());
                }

                var cmd = command.GetProperty("cmd").GetString();
                return Task.FromResult<string?>(cmd == "todoAdd" ? null : "bad");
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

    /// <summary>Verilen anahtarı içeren ilk satırı (3 sn içinde) döndürür.</summary>
    private static async Task<JsonElement> ReadUntilAsync(StreamReader reader, string property)
    {
        using var cts = new CancellationTokenSource(3000);
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

    [Fact]
    public async Task A_phone_gets_the_task_summary_right_after_connecting_and_the_android_model_can_read_it()
    {
        _state = new
        {
            enabled = true,
            todos = new List<object> { new { id = "a1", text = "Taşınma", done = false, subs = new[] { new { id = "b1", text = "Koli al", done = true } } } },
            reminders = new List<object> { new { id = "r1", text = "İlaç", due = 1_900_000_000L } }
        };

        var (_, reader) = await ConnectAsync();
        var message = await ReadUntilAsync(reader, "tasks");

        // Android uygulamasının kendi model sınıflarıyla çözülür.
        var tasks = message.GetProperty("tasks").Deserialize<dHud.Mobile.Services.TasksState>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(tasks.Enabled);
        var todo = Assert.Single(tasks.Todos!);
        Assert.Equal("Taşınma", todo.Text);
        Assert.False(todo.Done);
        var sub = Assert.Single(todo.Subs!);
        Assert.True(sub.Done);
        Assert.Equal("İlaç", Assert.Single(tasks.Reminders!).Text);
        Assert.Equal(1_900_000_000L, tasks.Reminders![0].Due);
    }

    [Fact]
    public async Task A_change_on_the_pc_is_pushed_to_connected_phones_but_an_identical_summary_is_not_resent()
    {
        var (_, reader) = await ConnectAsync();
        await ReadUntilAsync(reader, "tasks");

        _state = new { enabled = true, todos = new List<object> { new { id = "x", text = "yeni", done = false, subs = Array.Empty<object>() } }, reminders = new List<object>() };
        _link.SendTasks();
        var pushed = await ReadUntilAsync(reader, "tasks");
        Assert.Equal("yeni", pushed.GetProperty("tasks").GetProperty("todos")[0].GetProperty("text").GetString());

        // Aynı özet tekrar gönderilmez (not yazarken boşuna trafik olmasın): yalnızca zorlayınca gelir.
        _link.SendTasks();
        using var quiet = new CancellationTokenSource(600);
        var again = await Record.ExceptionAsync(async () =>
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(quiet.Token);
                if (line is not null && line.Contains("\"tasks\""))
                {
                    return;
                }
            }
        });
        Assert.IsAssignableFrom<OperationCanceledException>(again);

        _link.SendTasks(force: true);
        await ReadUntilAsync(reader, "tasks");
    }

    [Fact]
    public async Task A_task_command_reaches_the_handler_and_is_acknowledged()
    {
        var (tcp, reader) = await ConnectAsync();
        await ReadUntilAsync(reader, "tasks");

        await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes("""{"cmd":"todoAdd","text":"telefondan"}""" + "\n"));
        var ack = await ReadUntilAsync(reader, "ack");
        Assert.True(ack.GetProperty("ack").GetProperty("ok").GetBoolean());
        Assert.Equal("todoAdd", ack.GetProperty("ack").GetProperty("cmd").GetString());
        lock (_received)
        {
            Assert.Equal("telefondan", Assert.Single(_received).GetProperty("text").GetString());
        }

        await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes("""{"cmd":"reminderAdd","text":"x","minutes":5}""" + "\n"));
        var failed = await ReadUntilAsync(reader, "ack");
        Assert.False(failed.GetProperty("ack").GetProperty("ok").GetBoolean());
        Assert.Equal("bad", failed.GetProperty("ack").GetProperty("err").GetString());
    }

    [Fact]
    public async Task An_unpaired_client_never_receives_tasks_nor_can_it_send_commands()
    {
        var tcp = new TcpClient { NoDelay = true };
        _clients.Add(tcp);
        await tcp.ConnectAsync(IPAddress.Loopback, _link.Port);
        await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { hello = "dHud", token = "yanlis" }) + "\n"));
        using var reader = new StreamReader(tcp.GetStream(), Encoding.UTF8);
        using var cts = new CancellationTokenSource(3000);
        var line = await reader.ReadLineAsync(cts.Token);
        Assert.Contains("\"ok\":false", line);
        Assert.DoesNotContain("tasks", line);

        _link.SendTasks(force: true);
        lock (_received)
        {
            Assert.Empty(_received);
        }
    }
}
