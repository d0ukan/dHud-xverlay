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

    // Kimlikle yönetilen genel widget'lar (pano, yapılacaklar, sistem, ses, takvim, gün sayacı, başlatıcı).
    private readonly Dictionary<string, WidgetWindowBase> _extras = [];

    private static readonly Dictionary<string, Func<WidgetWindowBase>> ExtraFactories = new()
    {
        [WidgetIds.Clipboard] = () => new ClipboardWidgetWindow(),
        [WidgetIds.Todo] = () => new TodoWidgetWindow(),
        [WidgetIds.SysInfo] = () => new SysInfoWidgetWindow(),
        [WidgetIds.Audio] = () => new AudioWidgetWindow(),
        [WidgetIds.Calendar] = () => new CalendarWidgetWindow(),
        [WidgetIds.Countdown] = () => new CountdownWidgetWindow(),
        [WidgetIds.Launcher] = () => new LauncherWidgetWindow()
    };

    /// <summary>Ayar veya not listesi değişti: pencereleri buna göre eşitler ve görünüm ayarlarını yeniler.</summary>
    public void Apply()
    {
        var s = settings.Current;

        Sync(s.WidgetClock, ref _clock, () => new ClockWidgetWindow());
        Sync(s.WidgetTools, ref _tools, () => new ToolsWidgetWindow());
        Sync(s.WidgetStopwatch, ref _stopwatch, () => new StopwatchWidgetWindow());
        Sync(s.WidgetWeather, ref _weather, () => new WeatherWidgetWindow());

        foreach (var (id, factory) in ExtraFactories)
        {
            var want = s.Extra(id).Enabled;
            if (want && !_extras.ContainsKey(id))
            {
                var window = factory();
                _extras[id] = window;
                window.Show();
            }
            else if (!want && _extras.Remove(id, out var old))
            {
                old.Close();
            }
        }

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

        // Not yığını (klasör sekmeleri) veya serbest mod.
        var noteStack = settings.Current.NotesStacked;
        if (_wasStacked && !noteStack)
        {
            // Yığından çıkış: her not bulunduğu yerde kalır, konumu ve boyutu kendi alanlarına yazılır.
            foreach (var note in widgets.Data.Notes)
            {
                if (_notes.TryGetValue(note.Id, out var w))
                {
                    note.Left = w.Left + 8;
                    note.Top = w.Top + 8;
                    note.Width = widgets.Data.StackWidth;
                    note.Height = widgets.Data.StackHeight;
                }
            }

            widgets.ScheduleSave();
        }

        _wasStacked = noteStack;
        foreach (var window in _notes.Values)
        {
            window.Refresh();
        }

        if (noteStack)
        {
            LayoutNotes();
            ResolveStack();
        }

        _clock?.ApplyOptions();
        _tools?.ApplyOptions();
        _stopwatch?.ApplyOptions();
        _weather?.ApplyOptions();
        _weather?.Reload();
        (_extras.GetValueOrDefault(WidgetIds.Calendar) as CalendarWidgetWindow)?.Reload();
        foreach (var window in _extras.Values)
        {
            window.ApplyOptions();
        }

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
            case var id when _extras.TryGetValue(id, out var extra): extra.MoveToCorner(corner); break;
            case "notes" when Stacked:
                PlaceStackAt(corner);
                break;
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
        foreach (var window in _extras.Values)
        {
            yield return window;
        }
        foreach (var window in _notes.Values)
        {
            yield return window;
        }
    }

    // ---- Not yığını (klasör sekmeleri) ----

    private bool _wasStacked;

    private bool Stacked => settings.Current.NotesStacked;

    private List<StickyNoteWindow> NoteWindowsInOrder() =>
        widgets.Data.Notes.Select(n => _notes.GetValueOrDefault(n.Id)).OfType<StickyNoteWindow>().ToList();

    private Point StackAnchor()
    {
        var d = widgets.Data;
        if (d.NotesStackPos is { } p && !double.IsNaN(p.Left) && !double.IsNaN(p.Top))
        {
            return new Point(p.Left, p.Top);
        }

        var wa = SystemParameters.WorkArea;
        return new Point(wa.Right - d.StackWidth - 40, wa.Top + 90);
    }

    private Rect StackRect(Point anchor)
    {
        var count = Math.Max(1, NoteWindowsInOrder().Count);
        return new Rect(anchor.X, anchor.Y, widgets.Data.StackWidth, widgets.Data.StackHeight + (count - 1) * StickyNoteWindow.TabStep);
    }

    /// <summary>Notları yığın düzenine dizer: her not bir öncekinin bir sekme altında; sondaki (öndeki) en altta ve en üstte durur.</summary>
    public void LayoutNotes()
    {
        if (!Stacked)
        {
            return;
        }

        var anchor = StackAnchor();
        var d = widgets.Data;
        var windows = NoteWindowsInOrder();
        for (var i = 0; i < windows.Count; i++)
        {
            windows[i].ApplyStack(anchor.X, anchor.Y + i * StickyNoteWindow.TabStep, d.StackWidth, d.StackHeight);
        }

        // Z sırası = sekme sırası (arkadaki üstte kalan şeritleriyle görünür).
        foreach (var window in windows)
        {
            window.BringToTop();
        }
    }

    /// <summary>Tıklanan sekmedeki notu öne alır: sıranın sonuna taşınır, diğerlerinin şeritleri yerinde kalır.</summary>
    public void BringNoteToFront(StickyNote note)
    {
        if (widgets.Data.Notes.Remove(note))
        {
            widgets.Data.Notes.Add(note);
            widgets.ScheduleSave();
        }

        LayoutNotes();
        if (_notes.TryGetValue(note.Id, out var window))
        {
            window.Activate();
        }
    }

    /// <summary>Sekmeyi sürüklemek tüm yığını taşır.</summary>
    public void MoveStackBy(Point delta)
    {
        var anchor = StackAnchor();
        widgets.Data.NotesStackPos = new WidgetPos { Left = anchor.X + delta.X, Top = anchor.Y + delta.Y };
        widgets.ScheduleSave();
        LayoutNotes();
        ResolveStack();
    }

    /// <summary>Yığının oturacağı yeri bulur: ızgara açıksa en yakın boş hücreler, değilse yalnızca ekran içi ve çakışmasız.</summary>
    private (Point Anchor, Rect? Preview)? StackTarget(Point anchor)
    {
        var windows = NoteWindowsInOrder();
        if (windows.Count == 0 || !windows[0].IsLoaded || windows[0].WorkAreaRect() is not Rect work)
        {
            return null;
        }

        var union = StackRect(anchor);
        var others = AllWindows().Where(w => w is not StickyNoteWindow && w.IsLoaded && w.IsVisible).Select(w => w.CardRect).ToList();
        if (App.Current.OverlayRect is Rect overlay)
        {
            others.Add(overlay);
        }

        if (settings.Current.NotesOptions.SnapToEdges)
        {
            var spec = WidgetGrid.Make(work);
            var span = WidgetGrid.SpanOf(union.Size, spec);
            var preferred = WidgetGrid.NearestCell(union.TopLeft, span, spec);
            var cell = WidgetGrid.FindFree(preferred, span, WidgetGrid.Occupied(others, spec), spec) ?? preferred;
            var snapOrigin = WidgetGrid.CardOrigin(cell.C, cell.R, spec);
            return (snapOrigin, new Rect(snapOrigin, union.Size));
        }

        var inside = new Point(
            Math.Clamp(union.Left, work.Left, Math.Max(work.Left, work.Right - union.Width)),
            Math.Clamp(union.Top, work.Top, Math.Max(work.Top, work.Bottom - union.Height)));
        var rect = new Rect(inside, union.Size);
        if (others.Any(o => Overlaps(o, rect)) && FindFreeSpot(rect, others, work) is Point free)
        {
            return (free, null);
        }

        return (inside, null);
    }

    /// <summary>Yığını (tek blok olarak) başka widget/overlay ile çakışmayacak ve ekranda kalacak şekilde yerleştirir.</summary>
    public void ResolveStack()
    {
        if (!Stacked || _resolving || StackTarget(StackAnchor()) is not { } target)
        {
            return;
        }

        var anchor = StackAnchor();
        if (Math.Abs(target.Anchor.X - anchor.X) > 0.5 || Math.Abs(target.Anchor.Y - anchor.Y) > 0.5)
        {
            widgets.Data.NotesStackPos = new WidgetPos { Left = target.Anchor.X, Top = target.Anchor.Y };
            widgets.ScheduleSave();
            LayoutNotes();
        }
    }

    private void PlaceStackAt(WidgetCorner corner)
    {
        var windows = NoteWindowsInOrder();
        if (windows.Count == 0 || windows[0].WorkAreaRect() is not Rect work)
        {
            return;
        }

        var size = StackRect(StackAnchor()).Size;
        const double margin = 16;
        var x = corner switch
        {
            WidgetCorner.TopLeft or WidgetCorner.BottomLeft => work.Left + margin,
            WidgetCorner.TopRight or WidgetCorner.BottomRight => work.Right - size.Width - margin,
            _ => work.Left + (work.Width - size.Width) / 2
        };
        var y = corner switch
        {
            WidgetCorner.TopLeft or WidgetCorner.TopRight => work.Top + margin,
            WidgetCorner.BottomLeft or WidgetCorner.BottomRight => work.Bottom - size.Height - margin,
            _ => work.Top + (work.Height - size.Height) / 2
        };
        widgets.Data.NotesStackPos = new WidgetPos { Left = x, Top = y };
        widgets.ScheduleSave();
        LayoutNotes();
        ResolveStack();
    }

    /// <summary>
    /// Açılışta "hep üstte" kapalı widget'lar tam ekran/maksimize bir uygulamanın arkasında kalıyordu (bar kapalıyken görünmüyorlardı).
    /// Hepsini, diğer pencerelerin önüne (odak almadan) bir kez getirir; not yığınında sekme sırası korunur.
    /// </summary>
    public void RaiseAll()
    {
        foreach (var window in AllWindows().Where(w => w is not StickyNoteWindow).ToList())
        {
            window.BringToTop();
        }

        if (Stacked)
        {
            LayoutNotes();
        }
        else
        {
            foreach (var note in _notes.Values)
            {
                note.BringToTop();
            }
        }
    }

    /// <summary>Tüm widget'ları (ve yığını) yeniden yerleştirir; ör. üstteki bar geri gelince onun yerinden kaymaları için.</summary>
    public void ResolveAll()
    {
        foreach (var window in AllWindows().Where(w => w is not StickyNoteWindow).ToList())
        {
            Resolve(window);
        }

        ResolveStack();
        if (!Stacked)
        {
            foreach (var window in _notes.Values)
            {
                Resolve(window);
            }
        }
    }
    private SnapPreviewWindow? _preview;

    private List<Rect> OtherRects(WidgetWindowBase window)
    {
        var others = AllWindows().Where(w => !ReferenceEquals(w, window) && w.IsLoaded && w.IsVisible).Select(w => w.CardRect).ToList();
        if (App.Current.OverlayRect is Rect overlay)
        {
            others.Add(overlay);
        }

        return others;
    }

    /// <summary>Izgara modunda widget'ın oturacağı hücre: bulunduğu yere en yakın, başka widget/overlay hücrelerine girmeyen konum.</summary>
    private (WidgetGrid.Spec Spec, (int W, int H) Span, (int C, int R) Cell)? GridTarget(WidgetWindowBase window)
    {
        if (window.WorkAreaRect() is not Rect work)
        {
            return null;
        }

        var spec = WidgetGrid.Make(work);
        var self = window.CardRect;
        var span = WidgetGrid.SpanOf(self.Size, spec);
        var preferred = WidgetGrid.NearestCell(self.TopLeft, span, spec);
        var occupied = WidgetGrid.Occupied(OtherRects(window), spec);
        return (spec, span, WidgetGrid.FindFree(preferred, span, occupied, spec) ?? preferred);
    }

    /// <summary>
    /// Widget'ı yerleştirir: ızgara modunda en yakın boş hücreye oturur; serbest modda (ızgara kapalı) yalnızca başka kartın/overlay'in
    /// üstüne binmemesi sağlanır. Boş yer bulunamazsa olduğu gibi kalır.
    /// </summary>
    public void Resolve(WidgetWindowBase window)
    {
        if (_resolving || !window.IsLoaded)
        {
            return;
        }

        if (window is StickyNoteWindow && Stacked)
        {
            ResolveStack(); // yığın tek blok olarak yerleşir
            return;
        }

        _resolving = true;
        try
        {
            if (window.SnapToGrid)
            {
                if (GridTarget(window) is { } target)
                {
                    var origin = WidgetGrid.CardOrigin(target.Cell.C, target.Cell.R, target.Spec);
                    var now = window.CardRect.TopLeft;
                    if (Math.Abs(origin.X - now.X) > 0.5 || Math.Abs(origin.Y - now.Y) > 0.5)
                    {
                        window.SetCardTopLeft(origin);
                    }
                }

                return;
            }

            if (window.WorkAreaRect() is not Rect work)
            {
                return;
            }

            var others = OtherRects(window);
            var self = window.CardRect;
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

    /// <summary>Sürüklerken widget'ın bırakılınca oturacağı hücreleri gösterir.</summary>
    public void Preview(WidgetWindowBase window)
    {
        if (window is StickyNoteWindow && Stacked)
        {
            // Sürüklenen sekmeden yığının sol üstünü geri hesapla ve yığının oturacağı hücreleri göster.
            var index = Math.Max(0, NoteWindowsInOrder().FindIndex(w => ReferenceEquals(w, window)));
            var guess = new Point(window.CardRect.Left, window.CardRect.Top - index * StickyNoteWindow.TabStep);
            if (StackTarget(guess) is { Preview: Rect rect })
            {
                _preview ??= new SnapPreviewWindow();
                _preview.ShowAt(rect);
            }

            return;
        }

        if (GridTarget(window) is { } target)
        {
            _preview ??= new SnapPreviewWindow();
            _preview.ShowAt(new Rect(WidgetGrid.CardOrigin(target.Cell.C, target.Cell.R, target.Spec), window.CardRect.Size));
        }
    }

    public void HidePreview() => _preview?.Hide();
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
        _preview?.Close();
        _preview = null;
        _clock?.Close();
        _clock = null;
        _tools?.Close();
        _tools = null;
        _stopwatch?.Close();
        _stopwatch = null;
        _weather?.Close();
        _weather = null;
        foreach (var window in _extras.Values)
        {
            window.Close();
        }

        _extras.Clear();
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
