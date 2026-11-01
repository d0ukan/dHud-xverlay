using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

using DHud.Helpers;

namespace DHud.Services;

/// <summary>Bir widget'ın (veya not grubunun) görünüm ve davranış ayarı.</summary>
public sealed class WidgetOptions
{
    /// <summary>Widget açık mı (yeni widget'lar için; eski beş widget kendi ayarını kullanır).</summary>
    public bool Enabled { get; set; }

    /// <summary>Kartın zemin görünürlüğü (yazılar hep net kalır). 0.2–1.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Diğer pencerelerin üstünde dursun.</summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Sürüklenerek taşınamasın.</summary>
    public bool LockPosition { get; set; }

    /// <summary>Bırakılınca ekran kenarına/köşesine yapışsın (ekran dışına çıkmama her zaman geçerlidir).</summary>
    public bool SnapToEdges { get; set; } = true;

    /// <summary>
    /// Kullanıcının çekerek verdiği boyut, ızgara hücresi cinsinden (genişlik × yükseklik). 0 = otomatik (içeriğin kendi boyutu).
    /// Hücre cinsinden saklanır: farklı çözünürlükte de kartlar aynı hücre düzenine oturur.
    /// </summary>
    public int SpanW { get; set; }

    public int SpanH { get; set; }

    public WidgetOptions Normalized()
    {
        Opacity = double.IsNaN(Opacity) ? 1.0 : Math.Clamp(Opacity, 0.2, 1.0);
        SpanW = SpanW is >= 1 and <= 1000 && SpanH is >= 1 and <= 1000 ? SpanW : 0;
        SpanH = SpanW == 0 ? 0 : SpanH;
        return this;
    }
}

/// <summary>Genel (kimlikle yönetilen) widget'ların kimlikleri.</summary>
public static class WidgetIds
{
    public const string Clipboard = "clipboard";
    public const string Todo = "todo";
    public const string SysInfo = "sysinfo";
    public const string Audio = "audio";
    public const string Calendar = "calendar";
    public const string Countdown = "countdown";
    public const string Launcher = "launcher";
    public const string Media = "media";
    public const string TaskMan = "taskman";
    public const string Market = "market";
    public const string Battery = "battery";
    public const string Notepad = "notepad";

    public static readonly string[] Extra = [Clipboard, Todo, SysInfo, Audio, Calendar, Countdown, Launcher, Media, TaskMan, Market, Battery, Notepad];
}

/// <summary>Widget'ı ekranın neresine yerleştirileceği.</summary>
public enum WidgetCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}

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

/// <summary>Yapılacaklar listesi maddesi.</summary>
public sealed class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool Done { get; set; }

    /// <summary>Alt görevler (tek seviye; alt görevin kendi alt görevi olmaz).</summary>
    public List<TodoItem> Subtasks { get; set; } = [];

    /// <summary>Kendisi ve alt görevleriyle kopya; yeni kimlikler alır, hepsi yapılmamış başlar.</summary>
    public TodoItem Duplicate() => new()
    {
        Text = Text,
        Subtasks = Subtasks.Select(s => new TodoItem { Text = s.Text }).ToList()
    };
}

/// <summary>Gün sayacı: bir tarihe kaç gün kaldı / kaç gün geçti.</summary>
public sealed class Countdown
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
}

/// <summary>Hızlı başlatıcıdaki bir kısayol.</summary>
public sealed class LauncherItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

/// <summary>Bir widget kapanınca (<see cref="Closed"/>) altındaki kartın (<see cref="Moved"/>) yukarı kaydırıldığının kaydı; geri açılınca eski yerine dönmek için.</summary>
public sealed class ShiftRecord
{
    public string Closed { get; set; } = string.Empty;
    public string Moved { get; set; } = string.Empty;

    /// <summary>Kaydırılan miktar (DIP, pozitif = yukarı).</summary>
    public double Dy { get; set; }

    /// <summary>Kaydırmadan sonra kartın üst konumu; kullanıcı sonradan taşıdıysa uyuşmaz.</summary>
    public double NewTop { get; set; }
}

/// <summary>Hızlı not defterinin bir sekmesi.</summary>
public sealed class NotepadTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>widgets.json içeriği: notlar, hatırlatıcılar, listeler ve widget konumları.</summary>
public sealed class WidgetData
{
    /// <summary>Borsa widget'ının izlediği semboller (Yahoo Finance biçimi: BTC-USD, AAPL, USDTRY=X). null: henüz hiç seçilmedi (varsayılanlar konur).</summary>
    public List<string>? Markets { get; set; }

    /// <summary>Kapanan widget'lar yüzünden yukarı kaydırılmış kartlar (geri açılınca eski yerine dönerler).</summary>
    public List<ShiftRecord> Shifts { get; set; } = [];

    /// <summary>Hızlı not defteri sekmeleri ve açık olan sekme.</summary>
    public List<NotepadTab> NotepadTabs { get; set; } = [];
    public string NotepadActive { get; set; } = string.Empty;

    /// <summary>Genel widget konumları (kimlik → konum): pano, yapılacaklar, sistem, ses, takvim, gün sayacı, başlatıcı.</summary>
    public Dictionary<string, WidgetPos> Positions { get; set; } = [];
    public List<TodoItem> Todos { get; set; } = [];
    public List<Countdown> Countdowns { get; set; } = [];
    public List<LauncherItem> Launchers { get; set; } = [];

    public WidgetPos? ClockPos { get; set; }
    public WidgetPos? ToolsPos { get; set; }
    public WidgetPos? StopwatchPos { get; set; }

    /// <summary>Not yığınının (klasör) sol üst köşesi ve ortak boyutu.</summary>
    public WidgetPos? NotesStackPos { get; set; }
    public double StackWidth { get; set; } = 260;
    public double StackHeight { get; set; } = 210;
    public WidgetPos? WeatherPos { get; set; }
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
    public const int MaxTodos = 60;
    public const int MaxSubtasks = 20;
    public const int MaxCountdowns = 20;
    public const int MaxLaunchers = 12;
    public const int MaxMarkets = 12;
    public const int MaxNotepadTabs = 12;
    public const int MaxNotepadLength = 100_000;

    // Henüz taşınmamış bir notun konumu NaN'dir; bu sayı JSON'a yazılamazsa tüm dosya kaydedilemezdi.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly string _filePath;

    /// <param name="filePath">Test için; boşsa %AppData%\dHud\widgets.json.</param>
    public WidgetService(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.File("widgets.json");
    }

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _dirty;
    private int _focusRounds;

    public WidgetData Data { get; private set; } = new();

    /// <summary>Kronometre (bellekte tutulur; uygulama kapanınca sıfırlanır). Çalışırken gösterimi pencere yeniler.</summary>
    public System.Diagnostics.Stopwatch Watch { get; } = new();

    /// <summary>Kronometre tur süreleri (kümülatif).</summary>
    public List<TimeSpan> Laps { get; } = [];

    public const int MaxLaps = 99;

    public void WatchLap()
    {
        if (Watch.IsRunning && Laps.Count < MaxLaps)
        {
            Laps.Add(Watch.Elapsed);
        }
    }

    public void WatchReset()
    {
        Watch.Reset();
        Laps.Clear();
    }

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
        // Ana dosya bozuksa (ör. yazarken elektrik gitti) bir önceki sağlam kopyadan (.bak) devam edilir.
        Data = TryRead(_filePath) ?? TryRead(_filePath + ".bak") ?? new WidgetData();

        NormalizeData();

        _saveTimer.Tick += (_, _) => Flush();
        _tick.Tick += (_, _) => Tick();
        UpdateTick();
    }

    /// <summary>Okunan/aktarılan verideki eksik ve sınır dışı değerleri düzeltir.</summary>
    private void NormalizeData()
    {
        Data.Notes ??= [];
        Data.Reminders ??= [];
        Data.Positions ??= [];
        Data.StackWidth = Math.Clamp(double.IsNaN(Data.StackWidth) ? 260 : Data.StackWidth, 176, 900);
        Data.StackHeight = Math.Clamp(double.IsNaN(Data.StackHeight) ? 210 : Data.StackHeight, 116, 900);
        Data.Todos = (Data.Todos ?? []).Where(t => t is not null).Take(MaxTodos).ToList();
        foreach (var todo in Data.Todos)
        {
            todo.Text ??= string.Empty;
            todo.Subtasks = (todo.Subtasks ?? []).Where(s => s is not null).Take(MaxSubtasks).ToList();
            foreach (var sub in todo.Subtasks)
            {
                sub.Text ??= string.Empty;
                sub.Subtasks = [];
            }
        }

        Data.Countdowns = (Data.Countdowns ?? []).Take(MaxCountdowns).ToList();
        Data.Launchers = (Data.Launchers ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Path)).Take(MaxLaunchers).ToList();
        if (Data.Markets is not null)
        {
            Data.Markets = Data.Markets.Select(MarketService.CleanSymbol).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxMarkets).ToList();
        }

        Data.NotepadTabs = (Data.NotepadTabs ?? []).Where(t => t is not null).Take(MaxNotepadTabs).ToList();
        foreach (var tab in Data.NotepadTabs)
        {
            tab.Title = (tab.Title ?? string.Empty).Trim();
            tab.Text = tab.Text is { Length: > MaxNotepadLength } ? tab.Text[..MaxNotepadLength] : tab.Text ?? string.Empty;
            if (string.IsNullOrEmpty(tab.Id))
            {
                tab.Id = Guid.NewGuid().ToString("N");
            }
        }

        Data.NotepadActive ??= string.Empty;

        Data.Shifts = (Data.Shifts ?? [])
            .Where(r => r is not null && !string.IsNullOrEmpty(r.Closed) && !string.IsNullOrEmpty(r.Moved)
                        && double.IsFinite(r.Dy) && r.Dy > 0 && double.IsFinite(r.NewTop))
            .Take(300).ToList();
        Data.Notes = Data.Notes.Take(MaxNotes).ToList();
        foreach (var note in Data.Notes)
        {
            note.Text ??= string.Empty;
            note.Width = Math.Clamp(double.IsNaN(note.Width) ? 240 : note.Width, 160, 900);
            note.Height = Math.Clamp(double.IsNaN(note.Height) ? 200 : note.Height, 100, 900);
            note.Color = Math.Clamp(note.Color, 0, 5);
        }
    }

    /// <summary>Tüm widget verisini (notlar, listeler, hatırlatıcılar, konumlar) verilenle değiştirir; içe aktarma için.</summary>
    public void ReplaceData(WidgetData data)
    {
        Data = data;
        NormalizeData();
        UpdateTick();
        ScheduleSave();
        Flush();
        RemindersChanged?.Invoke();
    }

    /// <summary>Verinin bir kopyası (dışa aktarma / profil için).</summary>
    public WidgetData CloneData() =>
        JsonSerializer.Deserialize<WidgetData>(JsonSerializer.Serialize(Data, JsonOptions), JsonOptions) ?? new WidgetData();

    /// <summary>Dışa/içe aktarmada kullanılan JSON seçenekleri (NaN konumları da yazılabilsin).</summary>
    internal static JsonSerializerOptions TransferOptions => JsonOptions;

    private static WidgetData? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<WidgetData>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget verisi okunamadı ({Path.GetFileName(path)}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saniyelik zamanlayıcı yalnızca bir hatırlatıcı/zamanlayıcı veya pomodoro varken çalışır;
    /// hiçbiri yokken uygulama bu yüzden boşuna uyanmaz.
    /// </summary>
    private void UpdateTick()
    {
        var needed = Data.Reminders.Count > 0 || Phase != PomodoroPhase.Off;
        if (needed && !_tick.IsEnabled)
        {
            _tick.Start();
        }
        else if (!needed && _tick.IsEnabled)
        {
            _tick.Stop();
        }
    }

    /// <summary>Widget verisi değişti (her kayıt isteğinde tetiklenir; telefondaki görev listesini güncel tutmak için dinlenir).</summary>
    public event Action? DataChanged;

    /// <summary>Yapılacaklar listesi widget dışından (telefon) değişti; açık widget yeniden çizilmeli.</summary>
    public event Action? TodosChangedExternally;

    public void RaiseTodosChangedExternally() => TodosChangedExternally?.Invoke();

    /// <summary>Değişiklik var; kısa bir gecikmeyle diske yazılır.</summary>
    public void ScheduleSave()
    {
        DataChanged?.Invoke();
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

            // Önce geçici dosyaya yaz, sonra dosyayı tek hamlede değiştir (eskisi .bak olarak kalır):
            // yazarken elektrik gitse bile notlar ya eski ya yeni haliyle sağlam kalır, yarım dosya oluşmaz.
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Data, JsonOptions));
            if (File.Exists(_filePath))
            {
                File.Replace(temp, _filePath, _filePath + ".bak");
            }
            else
            {
                File.Move(temp, _filePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Widget verisi yazılamadı: {ex.Message}");
            DiagLog.Add("widgets", ex);
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
        UpdateTick();
        RemindersChanged?.Invoke();
        return reminder;
    }

    public void RemoveReminder(Reminder reminder)
    {
        if (Data.Reminders.Remove(reminder))
        {
            ScheduleSave();
            UpdateTick();
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
        UpdateTick();
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
        UpdateTick();
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
            UpdateTick();
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
