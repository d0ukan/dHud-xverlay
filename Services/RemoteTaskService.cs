using System.Text.Json;

namespace DHud.Services;

/// <summary>
/// Telefondan yapılacaklar ve hatırlatıcı yönetimi: madde/alt görev ekle, işaretle, sil; hatırlatıcı kur.
/// Komutlar ANA İŞ PARÇACIĞINDA uygulanır (widget verisini değiştirir). Telefon eşleşmiş olmalıdır (anahtar zaten doğrulanır);
/// ayrıca "Telefon görevleri yönetebilir" ayarı açık olmalıdır.
/// Komutlar: <c>todoAdd</c>{text,parent?}, <c>todoToggle</c>{id,done?}, <c>todoDelete</c>{id}, <c>reminderAdd</c>{text,minutes}, <c>reminderDelete</c>{id}.
/// </summary>
public sealed class RemoteTaskService(WidgetService widgets, SettingsService settings)
{
    public const int MaxTextLength = 200;
    public const int MaxReminderMinutes = 24 * 60;

    public static bool IsTaskCommand(string? cmd) => cmd is "todoAdd" or "todoToggle" or "todoDelete" or "reminderAdd" or "reminderDelete";

    /// <summary>Dönüş: null = başarılı; aksi halde hata kodu (<c>disabled</c>, <c>bad</c>, <c>full</c>, <c>nofound</c>).</summary>
    public string? Apply(JsonElement command)
    {
        if (!settings.Current.MobileTasks)
        {
            return "disabled";
        }

        var data = widgets.Data;
        switch (Str(command, "cmd"))
        {
            case "todoAdd":
            {
                var text = Clean(Str(command, "text"));
                if (text.Length == 0)
                {
                    return "bad";
                }

                var parentId = Str(command, "parent");
                if (parentId.Length > 0)
                {
                    var parent = data.Todos.FirstOrDefault(t => t.Id == parentId);
                    if (parent is null)
                    {
                        return "nofound";
                    }

                    if (parent.Subtasks.Count >= WidgetService.MaxSubtasks)
                    {
                        return "full";
                    }

                    parent.Subtasks.Add(new TodoItem { Text = text });
                    parent.Done = false;   // yeni yapılmamış iş: ana görev tamam sayılmaz (widget'taki davranışla aynı)
                }
                else
                {
                    if (data.Todos.Count >= WidgetService.MaxTodos)
                    {
                        return "full";
                    }

                    data.Todos.Add(new TodoItem { Text = text });
                }

                return Changed();
            }

            case "todoToggle":
            {
                var id = Str(command, "id");
                var done = command.TryGetProperty("done", out var d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? (bool?)(d.ValueKind == JsonValueKind.True)
                    : null;
                foreach (var todo in data.Todos)
                {
                    if (todo.Id == id)
                    {
                        todo.Done = done ?? !todo.Done;
                        foreach (var sub in todo.Subtasks)
                        {
                            sub.Done = todo.Done;   // ana görevi işaretlemek alt görevleri de işaretler (widget'taki gibi)
                        }

                        return Changed();
                    }

                    if (todo.Subtasks.FirstOrDefault(s => s.Id == id) is { } subtask)
                    {
                        subtask.Done = done ?? !subtask.Done;
                        return Changed();
                    }
                }

                return "nofound";
            }

            case "todoDelete":
            {
                var id = Str(command, "id");
                if (data.Todos.RemoveAll(t => t.Id == id) > 0)
                {
                    return Changed();
                }

                foreach (var todo in data.Todos)
                {
                    if (todo.Subtasks.RemoveAll(s => s.Id == id) > 0)
                    {
                        return Changed();
                    }
                }

                return "nofound";
            }

            case "reminderAdd":
            {
                var text = Clean(Str(command, "text"));
                if (text.Length == 0
                    || !command.TryGetProperty("minutes", out var m) || m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out var minutes)
                    || minutes < 1 || minutes > MaxReminderMinutes)
                {
                    return "bad";
                }

                return widgets.AddReminder(text, DateTime.UtcNow.AddMinutes(minutes), "reminder") is null ? "full" : Changed();
            }

            case "reminderDelete":
            {
                var id = Str(command, "id");
                if (data.Reminders.FirstOrDefault(r => r.Id == id && r.Kind == "reminder") is not { } reminder)
                {
                    return "nofound";
                }

                widgets.RemoveReminder(reminder);
                return Changed();
            }

            default:
                return "bad";
        }
    }

    /// <summary>Telefona gönderilecek görev özeti (ANA İŞ PARÇACIĞINDA çağrılmalı). Kapalıysa <c>enabled=false</c> ve boş listeler.</summary>
    public object BuildState()
    {
        var enabled = settings.Current.MobileTasks;
        var data = widgets.Data;
        return new
        {
            enabled,
            todos = !enabled ? new List<object>() : data.Todos.Select(t => new
            {
                id = t.Id,
                text = t.Text,
                done = t.Done,
                subs = t.Subtasks.Select(s => new { id = s.Id, text = s.Text, done = s.Done }).ToList()
            }).ToList<object>(),
            reminders = !enabled ? new List<object>() : data.Reminders
                .Where(r => r.Kind == "reminder")
                .OrderBy(r => r.DueUtc)
                .Select(r => new { id = r.Id, text = r.Text, due = new DateTimeOffset(DateTime.SpecifyKind(r.DueUtc, DateTimeKind.Utc)).ToUnixTimeSeconds() })
                .ToList<object>()
        };
    }

    private string? Changed()
    {
        widgets.ScheduleSave();
        widgets.RaiseTodosChangedExternally();
        return null;
    }

    private static string Clean(string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
