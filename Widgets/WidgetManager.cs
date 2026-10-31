using DHud.Services;

namespace DHud.Widgets;

/// <summary>Ayarlara göre widget pencerelerini açar/kapatır. Yalnızca arayüz thread'inden kullanılır.</summary>
public sealed class WidgetManager(SettingsService settings, WidgetService widgets)
{
    private ClockWidgetWindow? _clock;
    private ToolsWidgetWindow? _tools;
    private readonly Dictionary<string, StickyNoteWindow> _notes = [];

    /// <summary>Ayar veya not listesi değişti: pencereleri buna göre eşitler.</summary>
    public void Apply()
    {
        var s = settings.Current;

        Sync(s.WidgetClock, ref _clock, () => new ClockWidgetWindow());
        Sync(s.WidgetTools, ref _tools, () => new ToolsWidgetWindow());

        // Silinen veya gizlenen notların pencereleri kapanır.
        var wanted = s.NotesVisible ? widgets.Data.Notes.Select(n => n.Id).ToHashSet() : [];
        foreach (var id in _notes.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            _notes[id].Close();
            _notes.Remove(id);
        }

        if (!s.NotesVisible)
        {
            return;
        }

        foreach (var note in widgets.Data.Notes)
        {
            if (!_notes.ContainsKey(note.Id))
            {
                var window = new StickyNoteWindow(note);
                _notes[note.Id] = window;
                window.Show();
            }
        }
    }

    /// <summary>Yeni boş not oluşturur ve gösterir (notlar gizliyse önce gösterilir).</summary>
    public void NewNote()
    {
        if (widgets.AddNote() is null)
        {
            App.Current.NotifyTray(Loc.T("W.NoteTitle"), Loc.T("W.TooManyNotes", WidgetService.MaxNotes));
            return;
        }

        settings.Current.NotesVisible = true;
        settings.NotifyChanged(); // App.OnSettingsChanged -> Apply()
    }

    public void CloseAll()
    {
        _clock?.Close();
        _clock = null;
        _tools?.Close();
        _tools = null;
        foreach (var window in _notes.Values)
        {
            window.Close();
        }

        _notes.Clear();
    }

    private static void Sync<T>(bool wanted, ref T? window, Func<T> create) where T : System.Windows.Window
    {
        if (wanted && window is null)
        {
            window = create();
            window.Show();
        }
        else if (!wanted && window is not null)
        {
            window.Close();
            window = null;
        }
    }
}
