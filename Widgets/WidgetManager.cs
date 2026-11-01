using System.Windows;
using System.Windows.Input;
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
        [WidgetIds.Launcher] = () => new LauncherWidgetWindow(),
        [WidgetIds.Media] = () => new MediaWidgetWindow(),
        [WidgetIds.TaskMan] = () => new TaskManWidgetWindow(),
        [WidgetIds.Market] = () => new MarketWidgetWindow(),
        [WidgetIds.Battery] = () => new BatteryWidgetWindow(),
        [WidgetIds.Notepad] = () => new NotepadWidgetWindow()
    };

    private bool _suspended;

    /// <summary>Widget'lar geçici gizli mi (tam ekran uygulama açıkken)?</summary>
    public bool Suspended => _suspended;

    /// <summary>
    /// Tam ekran uygulama açıkken tüm widget'ları gizler / çıkınca geri getirir. Gizliyken yeni açılan widget'lar da gösterilmez;
    /// geri gelirken pencereler odak çalmaz (not pencereleri normalde odak alır).
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        if (_suspended == suspended)
        {
            return;
        }

        _suspended = suspended;
        foreach (var window in AllWindows().ToList())
        {
            if (suspended)
            {
                window.Hide();
            }
            else
            {
                ShowQuietly(window);
            }
        }

        if (!suspended)
        {
            RaiseAll();
        }
    }

    private static void ShowQuietly(WidgetWindowBase window)
    {
        if (window.IsVisible)
        {
            return;
        }

        var activated = window.ShowActivated;
        window.ShowActivated = false;
        window.Show();
        window.ShowActivated = activated;
    }

    /// <summary>Yeni açılan pencereyi gösterir; widget'lar geçici gizliyse (tam ekran) bekletir.</summary>
    private void ShowNew(WidgetWindowBase window)
    {
        if (!_suspended)
        {
            window.Show();
        }
    }

    /// <summary>Ayar veya not listesi değişti: pencereleri buna göre eşitler ve görünüm ayarlarını yeniler.</summary>
    public void Apply()
    {
        var s = settings.Current;

        // Kapanan widget'ların altındakiler yukarı kayar; açılanların altındakiler eski yerine döner (pencereler değişmeden önce).
        var wantedIds = WantedIds(s);
        var present = PresentIds();
        var closing = present.Where(id => !wantedIds.Contains(id)).ToList();
        var opening = wantedIds.Where(id => !present.Contains(id)).ToList();
        if (!_applied)
        {
            // Açılışta kayıtlar zaten tutarlıdır; açık olan bir widget'a ait bayat kayıt varsa (ayar elle değişmiş) silinir.
            widgets.Data.Shifts.RemoveAll(r => wantedIds.Contains(r.Closed));
        }
        else if (s.CloseWidgetGaps)
        {
            RestoreGaps(opening);
            CloseGaps(closing);
        }

        _applied = true;

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
                ShowNew(window);
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
                    ShowNew(window);
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

    // ---- Boşluk kapatma: kapanan widget'ın altındakiler yukarı kayar, geri açılınca eski yerine döner ----

    private bool _applied;

    /// <summary>Kayabilen bir kart: kimliği, dikdörtgeni, monitör çalışma alanı ve dikey kaydırma işlemi.</summary>
    private sealed record Slot(string Id, Func<Rect> Rect, Func<Rect?> Work, Action<double> MoveBy, Action Settle);

    private HashSet<string> WantedIds(AppSettings s)
    {
        var ids = new HashSet<string>();
        if (s.WidgetClock) ids.Add("clock");
        if (s.WidgetTools) ids.Add("tools");
        if (s.WidgetStopwatch) ids.Add("stopwatch");
        if (s.WidgetWeather) ids.Add("weather");
        foreach (var id in ExtraFactories.Keys.Where(id => s.Extra(id).Enabled))
        {
            ids.Add(id);
        }

        return ids;
    }

    private HashSet<string> PresentIds()
    {
        var ids = new HashSet<string>(_extras.Keys);
        if (_clock is not null) ids.Add("clock");
        if (_tools is not null) ids.Add("tools");
        if (_stopwatch is not null) ids.Add("stopwatch");
        if (_weather is not null) ids.Add("weather");
        return ids;
    }

    private IEnumerable<(string Id, WidgetWindowBase Window)> FixedWindows()
    {
        if (_clock is not null) yield return ("clock", _clock);
        if (_tools is not null) yield return ("tools", _tools);
        if (_stopwatch is not null) yield return ("stopwatch", _stopwatch);
        if (_weather is not null) yield return ("weather", _weather);
        foreach (var (id, window) in _extras)
        {
            yield return (id, window);
        }
    }

    private Slot WindowSlot(string id, WidgetWindowBase window) => new(
        id,
        () => window.CardRect,
        window.WorkAreaRect,
        dy => window.Top += dy,
        () => Resolve(window));

    /// <summary>Şu an kayabilen kartlar: widget pencereleri, serbest notlar veya (yığın modunda) tüm not yığını tek blok olarak.</summary>
    private List<Slot> Slots()
    {
        var slots = FixedWindows().Where(w => w.Window.IsLoaded).Select(w => WindowSlot(w.Id, w.Window)).ToList();
        if (Stacked)
        {
            var first = NoteWindowsInOrder().FirstOrDefault(w => w.IsLoaded);
            if (first is not null)
            {
                slots.Add(new Slot("notes:stack",
                    () => StackRect(StackAnchor()),
                    first.WorkAreaRect,
                    dy =>
                    {
                        var anchor = StackAnchor();
                        widgets.Data.NotesStackPos = new WidgetPos { Left = anchor.X, Top = anchor.Y + dy };
                        widgets.ScheduleSave();
                        LayoutNotes();
                    },
                    ResolveStack));
            }
        }
        else
        {
            slots.AddRange(_notes.Where(n => n.Value.IsLoaded).Select(n => WindowSlot("note:" + n.Key, n.Value)));
        }

        return slots;
    }

    private void CloseGaps(List<string> closing)
    {
        if (closing.Count == 0)
        {
            return;
        }

        var slots = Slots();
        var gone = closing.ToHashSet();
        var rects = slots.Where(s => gone.Contains(s.Id)).ToDictionary(s => s.Id, s => s.Rect());
        var others = slots.Where(s => !gone.Contains(s.Id)).ToList();
        var obstacles = App.Current.OverlayRect is Rect overlay ? [overlay] : new List<Rect>();

        // Üstteki önce kapanmış gibi işlenir: böylece alttaki kapanan kendi sütununu zaten yukarı çekmiş olur.
        foreach (var (id, rect) in rects.OrderBy(r => r.Value.Top))
        {
            var items = others.Select(s => new WidgetGaps.Item(s.Id, s.Rect(), s.Work())).ToList();
            if (WidgetGaps.Compute(rect, items, obstacles, Gap) is not { } shift)
            {
                continue;
            }

            foreach (var moved in shift.Moved)
            {
                others.First(s => s.Id == moved.Id).MoveBy(-shift.Dy);
                widgets.Data.Shifts.Add(new ShiftRecord { Closed = id, Moved = moved.Id, Dy = shift.Dy, NewTop = moved.Rect.Top - shift.Dy });
            }
        }

        widgets.ScheduleSave();
    }

    private void RestoreGaps(List<string> opening)
    {
        if (opening.Count == 0 || widgets.Data.Shifts.Count == 0)
        {
            return;
        }

        var slots = Slots();
        var restored = new HashSet<string>();
        foreach (var id in opening)
        {
            foreach (var movedId in WidgetGaps.Restore(
                         widgets.Data.Shifts, id,
                         slotId => slots.FirstOrDefault(s => s.Id == slotId)?.Rect().Top,
                         (slotId, dy) => slots.First(s => s.Id == slotId).MoveBy(dy)))
            {
                restored.Add(movedId);
            }
        }

        // Geri dönen kartlar tam eski yerinde durur; yalnızca o arada oraya başka bir şey gelmişse en yakın boş yere otururlar.
        foreach (var slotId in restored)
        {
            var slot = slots.First(s => s.Id == slotId);
            var rect = slot.Rect();
            if (slots.Any(o => o.Id != slotId && WidgetManager.Overlaps(o.Rect(), rect)))
            {
                slot.Settle();
            }
        }

        widgets.ScheduleSave();
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

    /// <summary>Verilen pencere dışındaki kartların (ve bar'ın) dikdörtgenleri; boyutlandırırken üstüne binilmesin diye.</summary>
    public List<Rect> OtherCards(WidgetWindowBase window) => OtherRects(window);

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
    private (WidgetGrid.Spec Spec, (int W, int H) Span, (int C, int R) Cell, List<WidgetGrid.Guide> Guides)? GridTarget(WidgetWindowBase window, bool align = false)
    {
        if (window.WorkAreaRect() is not Rect work)
        {
            return null;
        }

        var spec = WidgetGrid.Make(work);
        var self = window.CardRect;
        var span = WidgetGrid.SpanOf(self.Size, spec);
        var preferred = WidgetGrid.NearestCell(self.TopLeft, span, spec);
        var others = OtherRects(window);
        var occupied = WidgetGrid.Occupied(others, spec);
        var cell = WidgetGrid.FindFree(preferred, span, occupied, spec) ?? preferred;
        var guides = new List<WidgetGrid.Guide>();

        // Kullanıcı sürüklüyorsa kenarlar komşuların kenar çizgilerine yapışır (Alt basılıyken kapalı).
        if (align && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            var neighbors = others.Select(o => WidgetGrid.CellsOf(o, spec)).ToList();
            cell = WidgetGrid.AlignCell(cell, span, neighbors, occupied, spec, WidgetGrid.AlignTolerance);
            guides = WidgetGrid.GuidesFor(new WidgetGrid.CellRect(cell.C, cell.R, span.W, span.H), neighbors, spec);
        }

        return (spec, span, cell, guides);
    }

    // ---- Hizalama kılavuzları ----

    private readonly List<SnapPreviewWindow> _guideWindows = [];

    /// <summary>Hizalanan kenarları gösteren ince çizgileri çizer (en çok 8).</summary>
    public void ShowGuides(IEnumerable<WidgetGrid.Guide> guides)
    {
        var list = guides.Take(8).ToList();
        while (_guideWindows.Count < list.Count)
        {
            _guideWindows.Add(new SnapPreviewWindow(line: true));
        }

        for (var i = 0; i < _guideWindows.Count; i++)
        {
            if (i >= list.Count)
            {
                _guideWindows[i].Hide();
                continue;
            }

            var g = list[i];
            _guideWindows[i].ShowAt(g.Vertical
                ? new Rect(g.Pos - 1, g.From, 2, Math.Max(2, g.To - g.From))
                : new Rect(g.From, g.Pos - 1, Math.Max(2, g.To - g.From), 2));
        }
    }

    public void HideGuides()
    {
        foreach (var window in _guideWindows)
        {
            window.Hide();
        }
    }

    /// <summary>
    /// Widget'ı yerleştirir: ızgara modunda en yakın boş hücreye oturur; serbest modda (ızgara kapalı) yalnızca başka kartın/overlay'in
    /// üstüne binmemesi sağlanır. Boş yer bulunamazsa olduğu gibi kalır.
    /// </summary>
    public void Resolve(WidgetWindowBase window, bool align = false)
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
                if (GridTarget(window, align) is { } target)
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

        if (GridTarget(window, align: true) is { } target)
        {
            _preview ??= new SnapPreviewWindow();
            _preview.ShowAt(new Rect(WidgetGrid.CardOrigin(target.Cell.C, target.Cell.R, target.Spec), window.CardRect.Size));
            ShowGuides(target.Guides);
        }
    }

    public void HidePreview()
    {
        _preview?.Hide();
        HideGuides();
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
        _preview?.Close();
        _preview = null;
        foreach (var guide in _guideWindows)
        {
            guide.Close();
        }

        _guideWindows.Clear();
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

    private void Sync<T>(bool wanted, ref T? window, Func<T> create) where T : WidgetWindowBase
    {
        if (wanted && window is null)
        {
            window = create();
            ShowNew(window);
        }
        else if (!wanted && window is not null)
        {
            window.Close();
            window = null;
        }
    }
}
