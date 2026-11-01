using DHud.Services;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Yapılacaklar: alt görevler, çoğaltma ve metin olarak kopyalama.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class TodoSubtaskTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static TodoItem Sample() => new()
    {
        Text = "Taşınma",
        Done = true,
        Subtasks = [new TodoItem { Text = "Koli al", Done = true }, new TodoItem { Text = "Eşyaları sar" }]
    };

    [Fact]
    public void A_duplicate_has_new_ids_and_every_item_starts_unchecked()
    {
        var original = Sample();
        var copy = original.Duplicate();

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Taşınma", copy.Text);
        Assert.False(copy.Done);
        Assert.Equal(["Koli al", "Eşyaları sar"], copy.Subtasks.Select(s => s.Text));
        Assert.All(copy.Subtasks, s => Assert.False(s.Done));
        Assert.Empty(copy.Subtasks.Select(s => s.Id).Intersect(original.Subtasks.Select(s => s.Id)));
        Assert.True(original.Done && original.Subtasks[0].Done);   // asıl görev değişmedi
    }

    [Fact]
    public void Copy_as_text_is_a_markdown_style_checklist()
    {
        Assert.Equal("- [x] Taşınma\n  - [x] Koli al\n  - [ ] Eşyaları sar".Replace("\n", Environment.NewLine), TodoWidgetWindow.ToText(Sample()));
        Assert.Equal("- [ ] Tek", TodoWidgetWindow.ToText(new TodoItem { Text = "Tek" }));
    }

    [Fact]
    public void Sub_tasks_survive_saving_and_loading() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        service.Data.Todos.Add(Sample());
        service.ScheduleSave();
        service.Flush();

        var reloaded = new WidgetService(path);
        reloaded.Load();
        var todo = Assert.Single(reloaded.Data.Todos);
        Assert.Equal(["Koli al", "Eşyaları sar"], todo.Subtasks.Select(s => s.Text));
        Assert.True(todo.Subtasks[0].Done);
    });

    [Fact]
    public void A_widgets_file_from_before_sub_tasks_still_loads() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        File.WriteAllText(path, """{"Todos":[{"Id":"a","Text":"eski görev","Done":false}]}""");
        var service = new WidgetService(path);
        service.Load();
        var todo = Assert.Single(service.Data.Todos);
        Assert.Equal("eski görev", todo.Text);
        Assert.Empty(todo.Subtasks);
    });

    [Fact]
    public void Imported_data_is_limited_flat_and_null_safe() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var many = string.Join(',', Enumerable.Range(0, 100).Select(i => $$"""{"Text":"s{{i}}","Subtasks":[{"Text":"derin"}]}"""));
        File.WriteAllText(path, "{\"Todos\":[{\"Text\":\"x\",\"Subtasks\":[" + many + "]},{\"Text\":null,\"Subtasks\":null},null]}");
        var service = new WidgetService(path);
        service.Load();

        Assert.Equal(2, service.Data.Todos.Count);
        Assert.Equal(WidgetService.MaxSubtasks, service.Data.Todos[0].Subtasks.Count);
        Assert.All(service.Data.Todos[0].Subtasks, s => Assert.Empty(s.Subtasks));   // tek seviye
        Assert.Equal(string.Empty, service.Data.Todos[1].Text);
        Assert.Empty(service.Data.Todos[1].Subtasks);
    });
}
