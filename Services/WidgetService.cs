using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace DHud.Services;

/// <summary>Widget penceresinin ekrandaki konumu.</summary>
public sealed class WidgetPos
{
    public double Left { get; set; }
    public double Top { get; set; }
}

/// <summary>Bir yapışkan not. <see cref="Color"/> paletteki sıra numarasıdır.</summary>
public sealed class StickyNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public int Color { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 200;

    /// <summary>true: diğer pencerelerin üstünde durur.</summary>
    public bool Pinned { get; set; } = true;
}

/// <summary>Zamanı gelince bildirim veren hatırlatıcı veya zamanlayıcı. Kind: reminder / timer.</summary>
public sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public DateTime DueUtc { get; set; }
    public string Kind { get; set; } = "reminder";
}

/// <summary>widgets.json içeriği: notlar, hatırlatıcılar ve widget konumları.</summary>
public sealed class WidgetData
{
    public WidgetPos? ClockPos { get; set; }
    public WidgetPos? ToolsPos { get; set; }
    public List<StickyNote> Notes { get; set; } = [];
    public List<Reminder> Reminders { get; set; } = [];
}

public enum PomodoroPhase
{
    Off,
    Focus,
    ShortBreak,
    LongBreak
}

/// <summary>
/// Widget'ların ortak verisi ve zamanlayıcısı. Notlar, hatırlatıcılar ve konumlar %AppData%\dHud\widgets.json dosyasında
/// (yalnızca bu bilgisayarda) saklanır; yazma, art arda değişikliklerde birleştirilir. Yalnızca arayüz thread'inden kullanılır.
/// </summary>
public sealed class WidgetService
{
    public const int PomodoroFocusMinutes = 25;
    public const int PomodoroShortBreakMinutes = 5;
    public const int PomodoroLongBreakMinutes = 15;
    public const int PomodoroRoundsBeforeLongBreak = 4;
    public const int MaxNotes = 30;
    public const int MaxReminders = 50;
    public const int MaxNoteLength = 20000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dHud", "widgets.json");

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _dirty;
    private int _focusRounds;

    public WidgetData Data { get; private set; } = new();

    public PomodoroPhase Phase { get; private set; }
    public DateTime PhaseEndUtc { get; private set; }
    public int FocusRounds => _focusRounds;

    /// <summary>Bir hatırlatıcı/zamanlayıcının/pomodoro aşamasının zamanı geldi.</summary>
    public event Action<Reminder>? ReminderFired;

    /// <summary>Her saniye tetiklenir (geri sayımları yenilemek için).</summary>
    public event Action? Ticked;

    /// <summary>Hatırlatıcı listesi veya pomodoro durumu değişti.</summary>
    public event Action? RemindersChanged;

    public void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                Data = JsonSerializer.Deserialize<WidgetData>(File.ReadAllText(_filePath), JsonOptions) ?? new WidgetData();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget verisi okunamadı: {ex.Message}");
            Data = new WidgetData();
        }

        Data.Notes ??= [];
        Data.Reminders ??= [];
        Data.Notes = Data.Notes.Take(MaxNotes).ToList();
        foreach (var note in Data.Notes)
        {
            note.Text ??= string.Empty;
            note.Width = Math.Clamp(double.IsNaN(note.Width) ? 240 : note.Width, 160, 900);
            note.Height = Math.Clamp(double.IsNaN(note.Height) ? 200 : note.Height, 100, 900);
            note.Color = Math.Clamp(note.Color, 0, 5);
        }

        _saveTimer.Tick += (_, _) => Flush();
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
    }

    /// <summary>Değişiklik var; kısa bir gecikmeyle diske yazılır.</summary>
    public void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Bekleyen değişikliği hemen yazar (kapanışta da çağrılır).</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget verisi yazılamadı: {ex.Message}");
        }
    }

    // ---- Notlar ----

    public StickyNote? AddNote()
    {
        if (Data.Notes.Count >= MaxNotes)
        {
            return null;
        }

        // Her yeni not biraz kaydırılarak açılır ki öncekinin tam üstüne binmesin; renkler sırayla döner.
        var note = new StickyNote { Color = Data.Notes.Count % 6 };
        Data.Notes.Add(note);
        ScheduleSave();
        return note;
    }

    public void RemoveNote(StickyNote note)
    {
        Data.Notes.Remove(note);
        ScheduleSave();
    }

    // ---- Hatırlatıcılar ve zamanlayıcılar ----

    public Reminder? AddReminder(string text, DateTime dueUtc, string kind)
    {
        if (Data.Reminders.Count >= MaxReminders)
        {
            return null;
        }

        var reminder = new Reminder { Text = text.Trim(), DueUtc = dueUtc, Kind = kind };
        Data.Reminders.Add(reminder);
        ScheduleSave();
        RemindersChanged?.Invoke();
        return reminder;
    }

    public void RemoveReminder(Reminder reminder)
    {
        if (Data.Reminders.Remove(reminder))
        {
            ScheduleSave();
            RemindersChanged?.Invoke();
        }
    }

    /// <summary>"HH:mm" biçimindeki saati bugünkü (geçtiyse yarınki) zamana çevirir.</summary>
    public static bool TryParseClock(string text, DateTime nowLocal, out DateTime dueLocal)
    {
        dueLocal = default;
        if (!TimeSpan.TryParse(text.Trim().Replace('.', ':'), out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            return false;
        }

        dueLocal = nowLocal.Date + time;
        if (dueLocal <= nowLocal)
        {
            dueLocal = dueLocal.AddDays(1);
        }

        return true;
    }

    // ---- Pomodoro ----

    public void StartPomodoro()
    {
        _focusRounds = 0;
        EnterPhase(PomodoroPhase.Focus);
    }

    public void StopPomodoro()
    {
        Phase = PomodoroPhase.Off;
        RemindersChanged?.Invoke();
    }

    /// <summary>Geçerli aşamayı atlayıp sonrakine geçer.</summary>
    public void SkipPomodoro()
    {
        if (Phase != PomodoroPhase.Off)
        {
            NextPhase(announce: false);
        }
    }

    private void EnterPhase(PomodoroPhase phase)
    {
        Phase = phase;
        var minutes = phase switch
        {
            PomodoroPhase.Focus => PomodoroFocusMinutes,
            PomodoroPhase.ShortBreak => PomodoroShortBreakMinutes,
            _ => PomodoroLongBreakMinutes
        };
        PhaseEndUtc = DateTime.UtcNow.AddMinutes(minutes);
        RemindersChanged?.Invoke();
    }

    private void NextPhase(bool announce)
    {
        var finished = Phase;
        PomodoroPhase next;
        if (finished == PomodoroPhase.Focus)
        {
            _focusRounds++;
            next = _focusRounds % PomodoroRoundsBeforeLongBreak == 0 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
        {
            next = PomodoroPhase.Focus;
        }

        EnterPhase(next);
        if (announce)
        {
            ReminderFired?.Invoke(new Reminder { Kind = "pomodoro", Text = next.ToString() });
        }
    }

    // ---- Zaman ----

    private void Tick()
    {
        var now = DateTime.UtcNow;

        var due = Data.Reminders.Where(r => r.DueUtc <= now).ToList();
        foreach (var reminder in due)
        {
            Data.Reminders.Remove(reminder);
        }

        if (due.Count > 0)
        {
            ScheduleSave();
            RemindersChanged?.Invoke();
            foreach (var reminder in due)
            {
                ReminderFired?.Invoke(reminder);
            }
        }

        if (Phase != PomodoroPhase.Off && now >= PhaseEndUtc)
        {
            NextPhase(announce: true);
        }

        Ticked?.Invoke();
    }
}
