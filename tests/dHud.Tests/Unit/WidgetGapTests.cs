using System.Windows;
using DHud.Services;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Bir widget kapanınca altındakilerin yukarı kayması ve geri açılınca eski yerine dönmesi.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class WidgetGapTests
{
    private const double Gap = 6;

    private static WidgetGaps.Item Item(string id, double left, double top, double width = 300, double height = 100, double workTop = 0) =>
        new(id, new Rect(left, top, width, height), new Rect(0, workTop, 1920, 1040));

    // Sütun (aralarında 6 boşluk): saat, sistem, hava, yapılacaklar, başlatıcı; her biri 100 yüksekliğinde
    private static List<WidgetGaps.Item> Column() =>
    [
        Item("clock", 1600, 10), Item("sys", 1600, 116), Item("weather", 1600, 222), Item("todo", 1600, 328), Item("launcher", 1600, 434)
    ];

    private static List<WidgetGaps.Item> Without(List<WidgetGaps.Item> items, string id) => items.Where(i => i.Id != id).ToList();

    [Fact]
    public void Everything_below_a_closed_widget_slides_up_by_its_height_plus_gap()
    {
        var items = Column();
        var shift = WidgetGaps.Compute(items.Single(i => i.Id == "todo").Rect, Without(items, "todo"), [], Gap);

        Assert.NotNull(shift);
        Assert.Equal(106, shift.Dy, 3);   // 100 yükseklik + 6 boşluk
        Assert.Equal(["launcher"], shift.Moved.Select(m => m.Id));
    }

    [Fact]
    public void Widgets_above_or_in_another_column_stay_put()
    {
        var items = Column();
        items.Add(Item("other", 1200, 434));
        var shift = WidgetGaps.Compute(items.Single(i => i.Id == "sys").Rect, Without(items, "sys"), [], Gap);

        Assert.NotNull(shift);
        Assert.Equal(["launcher", "todo", "weather"], shift.Moved.Select(m => m.Id).Order());
    }

    [Fact]
    public void Closing_the_last_widget_moves_nothing()
    {
        var items = Column();
        Assert.Null(WidgetGaps.Compute(items.Single(i => i.Id == "launcher").Rect, Without(items, "launcher"), [], Gap));
    }

    [Fact]
    public void The_slide_stops_before_running_into_something_above()
    {
        // Geniş kart kapananın sağına da taşıyor; sağ yarının üstünde kapanmayan başka bir kart var, o yüzden sadece biraz çıkabilir.
        var closed = Item("a", 100, 100, 100, 100);
        var wide = Item("wide", 100, 216, 400, 100);
        var blocker = Item("blocker", 300, 0, 200, 160);   // alt kenarı 160
        var shift = WidgetGaps.Compute(closed.Rect, [wide, blocker], [], Gap);

        Assert.NotNull(shift);
        Assert.Equal(216 - (160 + Gap), shift.Dy, 3);
    }

    [Fact]
    public void The_overlay_bar_and_the_screen_top_limit_the_slide()
    {
        var closed = Item("a", 100, 40, 100, 100, workTop: 30);
        var below = Item("b", 100, 146, 100, 100, workTop: 30);
        var shift = WidgetGaps.Compute(closed.Rect, [below], [new Rect(0, 0, 1920, 36)], Gap);

        Assert.NotNull(shift);
        Assert.Equal(146 - (36 + Gap), shift.Dy, 3);   // barın altında durur

        // Çalışma alanının üstünde zaten yer kalmamışsa hiç kaymaz.
        Assert.Null(WidgetGaps.Compute(Item("c", 100, 20, 100, 100, workTop: 10).Rect, [Item("d", 100, 126, 100, 100, workTop: 10)], [new Rect(0, 0, 1920, 120)], Gap));
    }

    [Fact]
    public void Closing_then_reopening_puts_every_card_back()
    {
        var tops = new Dictionary<string, double> { ["weather"] = 222, ["todo"] = 328, ["launcher"] = 434 };
        var ledger = new List<ShiftRecord>();

        foreach (var id in tops.Keys.ToList())   // sistem kapanır: üçü de 106 yukarı
        {
            ledger.Add(new ShiftRecord { Closed = "sys", Moved = id, Dy = 106, NewTop = tops[id] - 106 });
            tops[id] -= 106;
        }

        var restored = WidgetGaps.Restore(ledger, "sys", id => tops.TryGetValue(id, out var t) ? t : null, (id, dy) => tops[id] += dy);

        Assert.Equal(222, tops["weather"]);
        Assert.Equal(328, tops["todo"]);
        Assert.Equal(434, tops["launcher"]);
        Assert.Equal(3, restored.Count);
        Assert.Empty(ledger);
    }

    [Fact]
    public void Two_closures_restore_in_either_order()
    {
        foreach (var first in new[] { "sys", "todo" })
        {
            var tops = new Dictionary<string, double> { ["launcher"] = 222 };   // başlangıç 434; iki kapanış 106'şar çekti
            var ledger = new List<ShiftRecord>
            {
                new() { Closed = "sys", Moved = "launcher", Dy = 106, NewTop = 328 },
                new() { Closed = "todo", Moved = "launcher", Dy = 106, NewTop = 222 }
            };
            double? Top(string id) => tops.TryGetValue(id, out var t) ? t : null;
            void Move(string id, double dy) => tops[id] += dy;

            WidgetGaps.Restore(ledger, first, Top, Move);
            Assert.Equal(328, tops["launcher"]);
            var rest = Assert.Single(ledger);
            Assert.Equal(first == "sys" ? "todo" : "sys", rest.Closed);
            Assert.Equal(328, rest.NewTop);   // kalan kaydın beklenen konumu da güncellendi

            WidgetGaps.Restore(ledger, rest.Closed, Top, Move);
            Assert.Equal(434, tops["launcher"]);
            Assert.Empty(ledger);
        }
    }

    [Fact]
    public void A_card_the_user_moved_in_between_is_left_alone()
    {
        var tops = new Dictionary<string, double> { ["todo"] = 500 };   // kayıt 222 diyor ama kullanıcı 500'e taşımış
        var ledger = new List<ShiftRecord> { new() { Closed = "sys", Moved = "todo", Dy = 106, NewTop = 222 } };

        var restored = WidgetGaps.Restore(ledger, "sys", id => tops.TryGetValue(id, out var t) ? t : null, (id, dy) => tops[id] += dy);

        Assert.Empty(restored);
        Assert.Equal(500, tops["todo"]);
        Assert.Empty(ledger);
    }

    [Fact]
    public void A_card_that_is_not_open_keeps_its_record()
    {
        var ledger = new List<ShiftRecord> { new() { Closed = "sys", Moved = "todo", Dy = 106, NewTop = 222 } };
        var restored = WidgetGaps.Restore(ledger, "sys", _ => null, (_, _) => Assert.Fail("must not move"));

        Assert.Empty(restored);
        Assert.Single(ledger);
    }

    [Fact]
    public void Broken_ledger_entries_are_dropped_when_widget_data_loads() => Sta.Run(() =>
    {
        using var dir = new TempDataDir();
        var path = dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        service.Data.Shifts =
        [
            new ShiftRecord { Closed = "todo", Moved = "launcher", Dy = 106, NewTop = 222 },
            new ShiftRecord { Closed = "", Moved = "x", Dy = 5, NewTop = 1 },
            new ShiftRecord { Closed = "a", Moved = "b", Dy = -5, NewTop = 1 },
            new ShiftRecord { Closed = "a", Moved = "b", Dy = double.NaN, NewTop = 1 }
        ];
        service.ScheduleSave();
        service.Flush();

        var reloaded = new WidgetService(path);
        reloaded.Load();
        var record = Assert.Single(reloaded.Data.Shifts);
        Assert.Equal("launcher", record.Moved);
    });
}
