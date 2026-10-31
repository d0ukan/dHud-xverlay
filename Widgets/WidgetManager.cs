using System.Windows;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Ayarlara göre widget pencerelerini açar/kapatır. Yalnızca arayüz thread'inden kullanılır.</summary>
public sealed class WidgetManager(SettingsService settings, WidgetService widgets)
{
    private ClockWidgetWindow? _clock;
    private ToolsWidgetWindow? _tools;
    private StopwatchWidgetWindow? _stopwatch;
    private WeatherWidgetWindow? _weather;
    private readonly Dictionary<string, StickyNoteWindow> _notes = [];

    /// <summary>Ayar veya not listesi değişti: pencereleri buna göre eşitler ve görünüm ayarlarını yeniler.</summary>
    public void Apply()
    {
        var s = settings.Current;

        Sync(s.WidgetClock, ref _clock, () => new ClockWidgetWindow());
        Sync(s.WidgetTools, ref _tools, () => new ToolsWidgetWindow());
        Sync(s.WidgetStopwatch, ref _stopwatch, () => new StopwatchWidgetWindow());
        Sync(s.WidgetWeather, ref _weather, () => new WeatherWidgetWindow());

        // Silinen veya gizlenen notların pencereleri kapanır.
        var wanted = s.NotesVisible ? widgets.Data.Notes.Select(n => n.Id).ToHashSet() : [];
        foreach (var id in _notes.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            _notes[id].Close();
            _notes.Remove(id);
        }

        if (s.NotesVisible)
        {
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

        _clock?.ApplyOptions();
        _tools?.ApplyOptions();
        _stopwatch?.ApplyOptions();
        _weather?.ApplyOptions();
        _weather?.Reload();
        foreach (var window in _notes.Values)
        {
            window.ApplyOptions();
        }
    }

    /// <summary>Yeni boş not oluşturur ve gösterir (notlar gizliyse önce gösterilir).</summary>
    public void NewNote()
    {
        if (widgets.AddNote() is not { } note)
        {
            App.Current.NotifyTray(Loc.T("W.NoteTitle"), Loc.T("W.TooManyNotes", WidgetService.MaxNotes));
            return;
        }

        note.Pinned = settings.Current.NotesOptions.AlwaysOnTop;
        settings.Current.NotesVisible = true;
        settings.NotifyChanged(); // App.OnSettingsChanged -> Apply()
    }

    /// <summary>"Notlar hep üstte" ayarı: tüm notların iğnesini birden değiştirir.</summary>
    public void PinAllNotes(bool pinned)
    {
        foreach (var note in widgets.Data.Notes)
        {
            note.Pinned = pinned;
        }

        widgets.ScheduleSave();
        Apply();
    }

    /// <summary>Bir widget'ı (veya tüm notları, kademeli) ekranın bir köşesine/ortasına yerleştirir.</summary>
    public void PlaceAt(string widget, WidgetCorner corner)
    {
        switch (widget)
        {
            case "clock": _clock?.MoveToCorner(corner); break;
            case "tools": _tools?.MoveToCorner(corner); break;
            case "stopwatch": _stopwatch?.MoveToCorner(corner); break;
            case "weather": _weather?.MoveToCorner(corner); break;
            case "notes":
                var index = 0;
                foreach (var window in _notes.Values)
                {
                    window.MoveToCorner(corner, index++);
                }

                break;
        }
    }

    private const double Gap = 6;
    private bool _resolving;

    private IEnumerable<WidgetWindowBase> AllWindows()
    {
        if (_clock is not null) yield return _clock;
        if (_tools is not null) yield return _tools;
        if (_stopwatch is not null) yield return _stopwatch;
        if (_weather is not null) yield return _weather;
        foreach (var window in _notes.Values)
        {
            yield return window;
        }
    }

    /// <summary>
    /// Widget'lar birbirinin ve overlay'in üstüne binmesin: <paramref name="window"/> başka bir kartla (veya overlay ile) çakışıyorsa
    /// en yakın boş konuma (komşunun sağına/soluna/altına/üstüne, ekran sınırı içinde) kaydırılır. Boş yer bulunamazsa olduğu gibi kalır.
    /// </summary>
    public void Resolve(WidgetWindowBase window)
    {
        if (_resolving || !window.IsLoaded || window.WorkAreaRect() is not Rect work)
        {
            return;
        }

        var others = AllWindows().Where(w => !ReferenceEquals(w, window) && w.IsLoaded && w.IsVisible).Select(w => w.CardRect).ToList();
        if (App.Current.OverlayRect is Rect overlay)
        {
            others.Add(overlay);
        }

        var self = window.CardRect;
        if (others.Count == 0 || !others.Any(o => Overlaps(o, self)))
        {
            return;
        }

        _resolving = true;
        try
        {
            for (var round = 0; round < 4 && others.Any(o => Overlaps(o, self)); round++)
            {
                if (FindFreeSpot(self, others, work) is not Point target)
                {
                    break;
                }

                window.SetCardTopLeft(target);
                self = window.CardRect;
            }
        }
        finally
        {
            _resolving = false;
        }
    }

    /// <summary>
    /// <paramref name="self"/> boyutundaki kartı, <paramref name="others"/> ile çakışmayacak ve <paramref name="work"/> içinde kalacak
    /// en yakın konuma taşır (komşunun sağı/solu/altı/üstü). Böyle bir konum yoksa null.
    /// </summary>
    public static Point? FindFreeSpot(Rect self, IReadOnlyList<Rect> others, Rect work)
    {
        Point? best = null;
        var bestDistance = double.MaxValue;
        foreach (var o in others)
        {
            var candidates = new[]
            {
                new Point(o.Right + Gap, self.Top),                 // sağına
                new Point(o.Left - Gap - self.Width, self.Top),     // soluna
                new Point(self.Left, o.Bottom + Gap),               // altına
                new Point(self.Left, o.Top - Gap - self.Height)     // üstüne
            };
            foreach (var raw in candidates)
            {
                var p = new Point(
                    Math.Clamp(raw.X, work.Left, Math.Max(work.Left, work.Right - self.Width)),
                    Math.Clamp(raw.Y, work.Top, Math.Max(work.Top, work.Bottom - self.Height)));
                var rect = new Rect(p, self.Size);
                if (others.Any(x => Overlaps(x, rect)))
                {
                    continue;
                }

                var distance = Math.Abs(p.X - self.Left) + Math.Abs(p.Y - self.Top);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = p;
                }
            }
        }

        return best;
    }
    public static bool Overlaps(Rect a, Rect b) =>
        a.Left < b.Right - 0.5 && a.Right > b.Left + 0.5 && a.Top < b.Bottom - 0.5 && a.Bottom > b.Top + 0.5;

    public void CloseAll()
    {
        _clock?.Close();
        _clock = null;
        _tools?.Close();
        _tools = null;
        _stopwatch?.Close();
        _stopwatch = null;
        _weather?.Close();
        _weather = null;
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
