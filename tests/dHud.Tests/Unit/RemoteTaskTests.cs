using System.Text.Json;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Telefondan yapılacak / hatırlatıcı yönetimi: komutlar, sınırlar, izin ve telefona giden özet.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class RemoteTaskTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private (SettingsService Settings, WidgetService Widgets, RemoteTaskService Tasks) Make()
    {
        var settings = new SettingsService(_dir.File("settings.json"));
        settings.Load();
        var widgets = new WidgetService(_dir.File("widgets.json"));
        widgets.Load();
        return (settings, widgets, new RemoteTaskService(widgets, settings));
    }

    private static JsonElement Cmd(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Adding_ticking_and_deleting_a_todo_with_sub_tasks_works() => Sta.Run(() =>
    {
        var (_, widgets, tasks) = Make();
        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = "  Taşınma  " })));
        var todo = Assert.Single(widgets.Data.Todos);
        Assert.Equal("Taşınma", todo.Text);

        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = "Koli al", parent = todo.Id })));
        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = "Sar", parent = todo.Id })));
        Assert.Equal(2, todo.Subtasks.Count);

        // Alt görevi işaretle; ana görev kendiliğinden değişmez.
        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoToggle", id = todo.Subtasks[0].Id })));
        Assert.True(todo.Subtasks[0].Done);
        Assert.False(todo.Done);

        // Ana görevi işaretlemek alt görevleri de işaretler (widget'taki davranış).
        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoToggle", id = todo.Id, done = true })));
        Assert.True(todo.Done && todo.Subtasks.All(s => s.Done));

        // Yeni alt görev eklenince ana görev yeniden açılır.
        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = "Etiketle", parent = todo.Id })));
        Assert.False(todo.Done);

        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoDelete", id = todo.Subtasks[0].Id })));
        Assert.Equal(2, todo.Subtasks.Count);
        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoDelete", id = todo.Id })));
        Assert.Empty(widgets.Data.Todos);
    });

    [Fact]
    public void A_reminder_is_scheduled_the_given_number_of_minutes_ahead_and_can_be_removed() => Sta.Run(() =>
    {
        var (_, widgets, tasks) = Make();
        var before = DateTime.UtcNow;
        Assert.Null(tasks.Apply(Cmd(new { cmd = "reminderAdd", text = "İlaç", minutes = 30 })));
        var reminder = Assert.Single(widgets.Data.Reminders);
        Assert.Equal("reminder", reminder.Kind);
        Assert.InRange((reminder.DueUtc - before).TotalMinutes, 29.9, 30.1);

        Assert.Null(tasks.Apply(Cmd(new { cmd = "reminderDelete", id = reminder.Id })));
        Assert.Empty(widgets.Data.Reminders);
    });

    [Theory]
    [InlineData("""{"cmd":"todoAdd","text":""}""")]
    [InlineData("""{"cmd":"todoAdd","text":"   "}""")]
    [InlineData("""{"cmd":"todoAdd"}""")]
    [InlineData("""{"cmd":"reminderAdd","text":"x","minutes":0}""")]
    [InlineData("""{"cmd":"reminderAdd","text":"x","minutes":100000}""")]
    [InlineData("""{"cmd":"reminderAdd","text":"x","minutes":"5"}""")]
    [InlineData("""{"cmd":"reminderAdd","text":"x"}""")]
    [InlineData("""{"cmd":"nope"}""")]
    public void Invalid_commands_are_rejected_as_bad_and_change_nothing(string json) => Sta.Run(() =>
    {
        var (_, widgets, tasks) = Make();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("bad", tasks.Apply(doc.RootElement));
        Assert.Empty(widgets.Data.Todos);
        Assert.Empty(widgets.Data.Reminders);
    });

    [Fact]
    public void Unknown_ids_are_reported_and_limits_are_enforced() => Sta.Run(() =>
    {
        var (_, widgets, tasks) = Make();
        Assert.Equal("nofound", tasks.Apply(Cmd(new { cmd = "todoToggle", id = "yok" })));
        Assert.Equal("nofound", tasks.Apply(Cmd(new { cmd = "todoDelete", id = "yok" })));
        Assert.Equal("nofound", tasks.Apply(Cmd(new { cmd = "todoAdd", text = "x", parent = "yok" })));
        Assert.Equal("nofound", tasks.Apply(Cmd(new { cmd = "reminderDelete", id = "yok" })));

        for (var i = 0; i < WidgetService.MaxTodos; i++)
        {
            Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = $"t{i}" })));
        }

        Assert.Equal("full", tasks.Apply(Cmd(new { cmd = "todoAdd", text = "fazla" })));
        var first = widgets.Data.Todos[0];
        for (var i = 0; i < WidgetService.MaxSubtasks; i++)
        {
            Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = $"s{i}", parent = first.Id })));
        }

        Assert.Equal("full", tasks.Apply(Cmd(new { cmd = "todoAdd", text = "fazla", parent = first.Id })));

        for (var i = 0; i < WidgetService.MaxReminders; i++)
        {
            Assert.Null(tasks.Apply(Cmd(new { cmd = "reminderAdd", text = $"r{i}", minutes = 10 })));
        }

        Assert.Equal("full", tasks.Apply(Cmd(new { cmd = "reminderAdd", text = "fazla", minutes = 10 })));
    });

    [Fact]
    public void Nothing_works_when_the_setting_is_off_and_the_summary_is_empty() => Sta.Run(() =>
    {
        var (settings, widgets, tasks) = Make();
        widgets.Data.Todos.Add(new TodoItem { Text = "gizli" });
        settings.Current.MobileTasks = false;

        Assert.Equal("disabled", tasks.Apply(Cmd(new { cmd = "todoAdd", text = "x" })));
        Assert.Equal("disabled", tasks.Apply(Cmd(new { cmd = "todoToggle", id = widgets.Data.Todos[0].Id })));
        Assert.Single(widgets.Data.Todos);

        var json = JsonSerializer.Serialize(tasks.BuildState());
        Assert.DoesNotContain("gizli", json);
        Assert.Contains("\"enabled\":false", json);
    });

    [Fact]
    public void Long_text_is_trimmed_and_line_breaks_are_flattened() => Sta.Run(() =>
    {
        var (_, widgets, tasks) = Make();
        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = new string('a', 500) + "\r\nikinci satır" })));
        Assert.Equal(RemoteTaskService.MaxTextLength, widgets.Data.Todos[0].Text.Length);

        Assert.Null(tasks.Apply(Cmd(new { cmd = "todoAdd", text = "bir\r\niki\nüç" })));
        Assert.Equal("bir  iki üç", widgets.Data.Todos[1].Text);
    });

    [Fact]
    public void The_summary_contains_todos_with_sub_tasks_and_only_real_reminders_not_timers() => Sta.Run(() =>
    {
        var (_, widgets, tasks) = Make();
        widgets.Data.Todos.Add(new TodoItem { Text = "ana", Subtasks = [new TodoItem { Text = "alt", Done = true }] });
        widgets.AddReminder("hatırlat", DateTime.UtcNow.AddMinutes(5), "reminder");
        widgets.AddReminder("sayaç", DateTime.UtcNow.AddMinutes(5), "timer");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(tasks.BuildState()));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("enabled").GetBoolean());
        var todo = Assert.Single(root.GetProperty("todos").EnumerateArray());
        Assert.Equal("ana", todo.GetProperty("text").GetString());
        Assert.True(Assert.Single(todo.GetProperty("subs").EnumerateArray()).GetProperty("done").GetBoolean());
        var reminder = Assert.Single(root.GetProperty("reminders").EnumerateArray());
        Assert.Equal("hatırlat", reminder.GetProperty("text").GetString());
        Assert.True(reminder.GetProperty("due").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    });

    [Fact]
    public void Task_commands_are_recognised_so_the_app_can_route_them_away_from_the_fan_service()
    {
        foreach (var cmd in new[] { "todoAdd", "todoToggle", "todoDelete", "reminderAdd", "reminderDelete" })
        {
            Assert.True(RemoteTaskService.IsTaskCommand(cmd));
        }

        foreach (var cmd in new[] { "fan", "fanAll", "fanControl", "profile", "", null })
        {
            Assert.False(RemoteTaskService.IsTaskCommand(cmd));
        }
    }
}
