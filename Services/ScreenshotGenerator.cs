using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DHud.Widgets;

namespace DHud.Services;

/// <summary>
/// README için ekran görüntüleri üretir: <c>dHud.exe --screenshots &lt;klasör&gt;</c>. Pencereler EKRANA ÇIKMADAN, demo verilerle
/// doğrudan görüntü dosyasına çizilir; kullanıcının masaüstü, ayarları, notları ve gerçek ölçümleri kullanılmaz ve görünmez.
/// Her görüntü İngilizce ve Türkçe (<c>-tr</c> ekli) üretilir.
/// </summary>
internal static class ScreenshotGenerator
{
    private const double Dpi = 144;
    private const double Scale = Dpi / 96;
    private const double Pad = 24; // her görüntünün etrafındaki gölge payı (DIP)

    private static readonly SensorSnapshot Demo = BuildDemoSnapshot(0);

    public static async Task RunAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var app = App.Current;

        foreach (var language in new[] { AppLanguage.English, AppLanguage.Turkish })
        {
            Loc.Current = language;
            app.Settings.Current.Language = language;

            // Tarih/saat biçimleri de görüntünün diliyle uyumlu olsun.
            var culture = new CultureInfo(language == AppLanguage.English ? "en-US" : "tr-TR");
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = culture;
            PrepareDemoData(app);
            var suffix = language == AppLanguage.English ? string.Empty : "-tr";

            Save(outDir, "overview" + suffix, await Overview());
            Save(outDir, "widgets" + suffix, await MoreWidgets());
            Save(outDir, "settings-appearance" + suffix, Settings("AppearanceTab"));
            Save(outDir, "settings-widgets" + suffix, Settings("WidgetsTab"));
            Save(outDir, "settings-profiles" + suffix, Settings("ProfilesTab"));
            if (language == AppLanguage.English)
            {
                ProbeSearch(outDir);
            }
        }
    }

    // ---- Demo verisi ----

    private static SensorSnapshot BuildDemoSnapshot(int step)
    {
        // Sıcaklık/yük küçük dalgalanır (mini grafik dolu görünsün).
        var wave = Math.Sin(step / 3.0);
        return new SensorSnapshot(
            new HardwareReading("AMD Ryzen 7 5800X3D", Temperature: (float)(57 + wave * 3), Load: (float)(18 + wave * 9), ClockMhz: 4350, PowerWatts: 61, FanRpm: 1120,
                CoreMaxLoad: (float)(46 + wave * 10), CoreAvgLoad: 19),
            new HardwareReading("NVIDIA GeForce RTX 3080", Temperature: (float)(61 + wave * 2), Load: (float)(42 + wave * 12), ClockMhz: 1740, PowerWatts: 188, FanRpm: 1380,
                MemoryUsedGb: 5.1f, MemoryTotalGb: 10f),
            new HardwareReading("Memory", Load: 46, MemoryUsedGb: 14.7f, MemoryTotalGb: 32f),
            new NetworkReading("Ethernet", 4_300_000, 380_000, 14),
            new DiskReading("C:", 61, 568, 931));
    }

    private static void PrepareDemoData(App app)
    {
        static string L(string en, string tr) => Loc.Current == AppLanguage.Turkish ? tr : en;
        app.Widgets.ReplaceData(new WidgetData()); // önceki dilin demo verisini temizle
        var s = app.Settings.Current;
        s.ShowOverlay = true;
        s.Layout = OverlayLayout.Horizontal;
        s.ShowCpu = s.ShowGpu = s.ShowRam = s.ShowNetwork = s.ShowPing = true;
        s.ShowUsage = s.ShowSparkline = s.ShowClock = s.ShowPower = s.ShowFan = s.ShowVram = true;
        s.ShowDisk = false;
        s.Scale = 1.0;
        s.WidgetClock = s.WidgetWeather = s.WidgetStopwatch = s.WidgetTools = true;
        s.NotesVisible = true;
        foreach (var id in WidgetIds.Extra)
        {
            s.Extra(id).Enabled = true;
        }

        s.WeatherPlace = "Istanbul";
        s.ClockZone = "GMT Standard Time";

        // Demo takvim dosyası (bugünden itibaren birkaç etkinlik)
        var day = DateTime.Today;
        string Ics(DateTime start, string title, bool allDay = false) =>
            allDay
                ? $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART;VALUE=DATE:{start:yyyyMMdd}\r\nDTEND;VALUE=DATE:{start.AddDays(1):yyyyMMdd}\r\nEND:VEVENT\r\n"
                : $"BEGIN:VEVENT\r\nSUMMARY:{title}\r\nDTSTART:{start:yyyyMMdd'T'HHmmss}\r\nDTEND:{start.AddHours(1):yyyyMMdd'T'HHmmss}\r\nEND:VEVENT\r\n";
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n"
                  + Ics(day.AddHours(10), L("Team meeting", "Ekip toplantısı"))
                  + Ics(day.AddHours(15).AddMinutes(30), L("Dentist", "Diş hekimi"))
                  + Ics(day.AddDays(1).AddHours(9), L("Project review", "Proje değerlendirmesi"))
                  + Ics(day.AddDays(2), L("Holiday", "Tatil"), allDay: true)
                  + "END:VCALENDAR\r\n";
        var icsPath = Path.Combine(DHud.Helpers.AppPaths.DataDir, "demo.ics");
        Directory.CreateDirectory(DHud.Helpers.AppPaths.DataDir);
        File.WriteAllText(icsPath, ics);
        s.CalendarSource = icsPath;

        var w = app.Widgets.Data;
        foreach (var (text, color) in new[]
                 {
                     (L("Shopping list\nmilk, bread, coffee", "Alışveriş listesi\nsüt, ekmek, kahve"), 1), (L("Ideas\nnew widget for…", "Fikirler\nyeni widget için…"), 2), (L("Rent is due on the 5th", "Kira ayın 5'inde"), 5)
                 })
        {
            var note = app.Widgets.AddNote()!;
            note.Text = text;
            note.Color = color;
        }

        w.Todos.AddRange([new TodoItem { Text = L("Back up photos", "Fotoğrafları yedekle") }, new TodoItem { Text = L("Reply to Ayşe", "Ayşe'ye cevap yaz") }, new TodoItem { Text = L("Pay the electricity bill", "Elektrik faturasını öde"), Done = true }]);
        w.Countdowns.AddRange([
            new Countdown { Name = L("Holiday", "Tatil"), Date = DateTime.Today.AddDays(23) },
            new Countdown { Name = L("Exam", "Sınav"), Date = DateTime.Today.AddDays(6) },
            new Countdown { Name = L("Birthday", "Doğum günü"), Date = DateTime.Today.AddDays(-2) }]);
        foreach (var exe in new[] { @"C:\Windows\notepad.exe", @"C:\Windows\System32\calc.exe", @"C:\Windows\System32\mspaint.exe", @"C:\Windows\explorer.exe" })
        {
            if (File.Exists(exe))
            {
                w.Launchers.Add(new LauncherItem { Name = Path.GetFileNameWithoutExtension(exe), Path = exe });
            }
        }

        app.Widgets.AddReminder(L("Stand up and stretch", "Kalk ve esne"), DateTime.UtcNow.AddMinutes(18), "reminder");
        app.Widgets.AddReminder(L("Tea timer", "Çay zamanlayıcı"), DateTime.UtcNow.AddMinutes(4).AddSeconds(30), "timer");
        ClipboardWidgetWindow.SeedDemo(["https://github.com/d0ukan/dHud-xverlay", L("Meeting at 15:30, room B", "Toplantı 15:30, B odası"), "192.168.0.26"]);

        // Hazır profiller (Profiller sekmesinde görünsün)
        app.Profiles.Save("Gaming");
        app.Profiles.Save("Work");
    }

    // ---- Kompozisyonlar ----

    private static async Task<BitmapSource> Overview()
    {
        var app = App.Current;

        var bar = new MainWindow();
        for (var i = 0; i < 40; i++)
        {
            bar.UpdateReadings(BuildDemoSnapshot(i));
        }

        bar.RenderForScreenshot(Demo);
        var system = new SysInfoWidgetWindow();
        system.RenderDemo(Demo);
        var clock = new ClockWidgetWindow();
        var weather = new WeatherWidgetWindow();
        weather.ShowDemo(new WeatherData(
            new WeatherNow(17.4, 15.9, 2, 14, true),
            [new WeatherDay(DateTime.Today, 12, 18, 2), new WeatherDay(DateTime.Today.AddDays(1), 11, 16, 61), new WeatherDay(DateTime.Today.AddDays(2), 10, 15, 3)],
            DateTime.UtcNow));
        var calendar = new CalendarWidgetWindow();
        await Task.Delay(700); // takvim dosyası arka planda okunur

        var notes = app.Widgets.Data.Notes.Select(n => new StickyNoteWindow(n)).ToList();

        var items = new List<(BitmapSource Image, Point At)>();
        var barImage = Snap(Root(bar));
        items.Add((barImage, new Point(Center(1500, barImage), 14)));
        items.Add((Snap(Root(system)), new Point(30, 80)));
        items.Add((Snap(Root(clock)), new Point(372, 80)));
        items.Add((Snap(Root(weather)), new Point(372, 196)));
        items.Add((Snap(Root(calendar)), new Point(705, 80)));
        for (var i = 0; i < notes.Count; i++)
        {
            items.Add((Snap(Root(notes[i]), new Size(notes[i].Width, notes[i].Height)), new Point(1060, 80 + i * StickyNoteWindow.TabStep)));
        }

        var result = Compose(new Size(1500, 520), items);
        CloseAll(bar, system, clock, weather, calendar);
        CloseAll(notes.ToArray());
        return result;
    }

    private static Task<BitmapSource> MoreWidgets()
    {
        var todo = new TodoWidgetWindow();
        var countdown = new CountdownWidgetWindow();
        var launcher = new LauncherWidgetWindow();
        var clipboard = new ClipboardWidgetWindow();
        var audio = new AudioWidgetWindow();
        var tools = new ToolsWidgetWindow();
        var stopwatch = new StopwatchWidgetWindow();

        var items = new List<(BitmapSource Image, Point At)>
        {
            (Snap(Root(todo)), new Point(20, 20)),
            (Snap(Root(countdown)), new Point(330, 20)),
            (Snap(Root(clipboard)), new Point(650, 20)),
            (Snap(Root(tools)), new Point(970, 20)),
            (Snap(Root(launcher)), new Point(20, 330)),
            (Snap(Root(audio)), new Point(390, 330)),
            (Snap(Root(stopwatch)), new Point(690, 330))
        };
        var result = Compose(new Size(1330, 500), items);
        CloseAll(todo, countdown, launcher, clipboard, audio, tools, stopwatch);
        return Task.FromResult(result);
    }

    private static BitmapSource Settings(string tabName)
    {
        var window = new SettingsWindow();
        if (window.FindName(tabName) is RadioButton tab)
        {
            tab.IsChecked = true;
        }

        // Bu bilgisayara özgü durum satırları ve düğmeler görüntüye girmesin.
        foreach (var name in new[] { "StatusBox", "FooterBar" })
        {
            if (window.FindName(name) is FrameworkElement element)
            {
                element.Visibility = Visibility.Collapsed;
            }
        }

        if (window.FindName("TabScroll") is ScrollViewer scroll)
        {
            scroll.MaxHeight = double.PositiveInfinity; // tüm içerik görünsün
        }

        var root = Root(window);
        root.Width = window.Width - root.Margin.Left - root.Margin.Right; // pencere genişliğinde (sekmeye göre daralmasın)
        var image = Snap(root, new Size(window.Width, double.PositiveInfinity));
        var result = Compose(new Size(image.PixelWidth / Scale - 2 * Pad + 40, image.PixelHeight / Scale - 2 * Pad + 40), [(image, new Point(20, 20))]);
        CloseAll(window);
        return result;
    }

    /// <summary>Ayar aramasını birkaç sorguyla dener ve sonucu <c>search-probe.txt</c> dosyasına yazar (README görüntüleriyle ilgisi yok; kendi kendine sınama).</summary>
    private static void ProbeSearch(string outDir)
    {
        var window = new SettingsWindow();
        var method = typeof(SettingsWindow).GetMethod("FindSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var lines = new List<string>();
        foreach (var query in new[] { "ping", "fan", "opacity", "note", "calendar", "profile", "snap", "xyzzy" })
        {
            var hits = ((System.Collections.IEnumerable)method.Invoke(window, [query])!).Cast<object>().ToList();
            lines.Add($"[{query}] {hits.Count} sonuç");
            foreach (var hit in hits.Take(6))
            {
                var type = hit.GetType();
                var tab = (RadioButton)type.GetProperty("Tab")!.GetValue(hit)!;
                lines.Add($"   {tab.Content} > {type.GetProperty("Text")!.GetValue(hit)}");
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "search-probe.txt"), lines);
        CloseAll(window);
    }

    // ---- Çizim yardımcıları ----

    private static FrameworkElement Root(Window window) => (FrameworkElement)window.Content;

    private static double Center(double width, BitmapSource image) => (width - (image.PixelWidth / Scale - 2 * Pad)) / 2;

    private static BitmapSource Snap(FrameworkElement root, Size? available = null)
    {
        var size = available ?? new Size(double.PositiveInfinity, double.PositiveInfinity);

        // İki geçiş: kaynaklar (yazı boyutları vb.) ilk ölçümden sonra oturur; bekleyen işler de işlenir.
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(size);
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
            Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Elemanın etrafında Pad kadar boşluk bırakılır ki gölge efektleri kesilmesin.
        var w = root.ActualWidth;
        var h = root.ActualHeight;
        var whole = new Rect(0, 0, w + 2 * Pad, h + 2 * Pad);
        var brush = new VisualBrush(root)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(-Pad, -Pad, w + 2 * Pad, h + 2 * Pad),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = whole,
            Stretch = Stretch.Fill
        };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(brush, null, whole);
        }

        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(whole.Width * Scale)), Math.Max(1, (int)Math.Ceiling(whole.Height * Scale)), Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Kartları koyu, hafif renk geçişli bir "masaüstü" zemini üzerine yerleştirir.</summary>
    private static BitmapSource Compose(Size size, IEnumerable<(BitmapSource Image, Point At)> items)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(Color.FromRgb(0x10, 0x12, 0x1A), Color.FromRgb(0x1D, 0x22, 0x33), 35);
            dc.DrawRectangle(background, null, new Rect(size));
            var glow = new RadialGradientBrush(Color.FromArgb(0x30, 0x3B, 0x82, 0xF6), Colors.Transparent)
            {
                Center = new Point(0.15, 0.1),
                GradientOrigin = new Point(0.15, 0.1),
                RadiusX = 0.7,
                RadiusY = 0.9
            };
            dc.DrawRectangle(glow, null, new Rect(size));
            foreach (var (image, at) in items)
            {
                dc.DrawImage(image, new Rect(at.X - Pad, at.Y - Pad, image.PixelWidth / Scale, image.PixelHeight / Scale));
            }
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * Scale), (int)Math.Ceiling(size.Height * Scale), Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(string dir, string name, BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(stream);
    }

    private static void CloseAll(params Window[] windows)
    {
        foreach (var window in windows)
        {
            try
            {
                window.Close();
            }
            catch (InvalidOperationException)
            {
                // Hiç gösterilmemiş pencere; bırakılır.
            }
        }
    }
}
