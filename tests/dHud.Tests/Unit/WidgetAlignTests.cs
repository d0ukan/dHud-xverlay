using System.Windows;
using DHud.Widgets;
using Xunit;
using Edges = DHud.Widgets.WidgetGrid.Edges;

namespace DHud.Tests.Unit;

/// <summary>Kartların birbirinin kenar çizgilerine yapışması: "yukarıdakiyle aynı hizada genişlet", "yanındakiyle aynı hizada uzat".</summary>
[Trait(Cat.Name, Cat.Fast)]
public class WidgetAlignTests
{
    private static readonly WidgetGrid.Spec G = WidgetGrid.Make(new Rect(0, 0, 1920, 1040));
    private static readonly (int W, int H) Min = (12, 8);
    private const int Tol = WidgetGrid.AlignTolerance;

    private static WidgetGrid.CellRect Cells(int c, int r, int w, int h) => new(c, r, w, h);

    private static Rect Pixels(WidgetGrid.CellRect r) => WidgetGrid.CellsRect(r.C, r.R, (r.W, r.H), G);

    [Fact]
    public void CellsOf_is_the_inverse_of_CellsRect()
    {
        var original = Cells(20, 15, 40, 25);
        Assert.Equal(original, WidgetGrid.CellsOf(Pixels(original), G));
    }

    [Fact]
    public void The_right_edge_snaps_to_the_right_edge_of_the_card_above()
    {
        var above = Cells(20, 5, 50, 20);               // sağ kenar: 70. çizgi
        var card = Pixels(Cells(20, 30, 30, 20));       // sağ kenar: 50. çizgi
        var delta = new Vector(G.Cell * (19.4), 0);      // ~69. çizgi: 1 hücre kaldı
        var plain = WidgetGrid.Resize(card, Edges.Right, delta, Min, G);
        var snapped = WidgetGrid.Resize(card, Edges.Right, delta, Min, G, [above], Tol);

        Assert.Equal(69, plain.C + plain.W);
        Assert.Equal(70, snapped.C + snapped.W);   // yukarıdakiyle aynı hizada
    }

    [Fact]
    public void The_bottom_edge_snaps_to_the_bottom_edge_of_the_neighbour_beside()
    {
        var beside = Cells(80, 10, 30, 40);               // alt kenar: 50. satır
        var card = Pixels(Cells(20, 10, 40, 25));
        var delta = new Vector(0, G.Cell * 13.6);          // ~48.6 -> 49. satır
        var snapped = WidgetGrid.Resize(card, Edges.Bottom, delta, Min, G, [beside], Tol);
        Assert.Equal(50, snapped.R + snapped.H);
    }

    [Fact]
    public void The_left_edge_snaps_flush_to_a_neighbour_on_its_left()
    {
        var left = Cells(10, 10, 30, 30);                  // sağ kenar: 40. çizgi
        var card = Pixels(Cells(50, 12, 30, 20));
        var snapped = WidgetGrid.Resize(card, Edges.Left, new Vector(-G.Cell * 8.7, 0), Min, G, [left], Tol); // ~41.3 -> 40 (bitişik)
        Assert.Equal(40, snapped.C);
    }

    [Fact]
    public void Snapping_does_nothing_when_the_edge_is_farther_than_the_tolerance()
    {
        var above = Cells(20, 5, 50, 20);
        var card = Pixels(Cells(20, 30, 30, 20));
        var delta = new Vector(G.Cell * 14, 0);   // sağ kenar 64: komşunun 70'ine 6 hücre var
        var snapped = WidgetGrid.Resize(card, Edges.Right, delta, Min, G, [above], Tol);
        Assert.Equal(64, snapped.C + snapped.W);
    }

    [Fact]
    public void A_neighbour_far_away_only_offers_same_side_lines_not_touching_ones()
    {
        // Karşı eksende örtüşmeyen bir kartın "bitişik" çizgisine yapışılmaz (anlamsız hizalama).
        var far = Cells(10, 100, 30, 10);                  // çok aşağıda, sağ kenar: 40
        var card = Pixels(Cells(50, 10, 30, 20));
        var snapped = WidgetGrid.Resize(card, Edges.Left, new Vector(-G.Cell * 8.8, 0), Min, G, [far], Tol); // ~41.2
        Assert.Equal(41, snapped.C);
    }

    [Fact]
    public void A_dropped_card_aligns_its_top_with_a_neighbour_within_the_tolerance()
    {
        var neighbour = Cells(10, 10, 30, 30);
        var occupied = WidgetGrid.Occupied([Pixels(neighbour)], G);
        var cell = WidgetGrid.AlignCell((45, 12), (30, 20), [neighbour], occupied, G, Tol);   // 2 satır farkla
        Assert.Equal(10, cell.R);   // aynı üst çizgi
    }

    [Fact]
    public void A_dropped_card_aligns_its_right_edge_with_a_neighbours_right_edge()
    {
        var neighbour = Cells(10, 10, 40, 20);                 // sağ: 50
        var occupied = WidgetGrid.Occupied([Pixels(neighbour)], G);
        var cell = WidgetGrid.AlignCell((29, 40), (20, 15), [neighbour], occupied, G, Tol);   // sağ kenar 49 -> 50
        Assert.Equal(30, cell.C);
    }

    [Fact]
    public void Alignment_never_moves_a_card_onto_an_occupied_cell()
    {
        var a = Cells(10, 10, 30, 30);
        var b = Cells(44, 10, 30, 30);
        var occupied = WidgetGrid.Occupied([Pixels(a), Pixels(b)], G);
        var cell = WidgetGrid.AlignCell((40, 50), (30, 20), [a, b], occupied, G, Tol);
        Assert.True(occupied.IsFree(cell.C, cell.R, 30, 20));
    }

    [Fact]
    public void Guides_appear_only_for_edges_on_the_same_line_as_a_neighbour()
    {
        var neighbour = Cells(20, 5, 50, 20);
        var me = Cells(20, 30, 50, 20);                        // sol ve sağ kenarlar aynı çizgide
        var guides = WidgetGrid.GuidesFor(me, [neighbour], G);
        Assert.Equal(2, guides.Count);
        Assert.All(guides, g => Assert.True(g.Vertical));

        var unaligned = WidgetGrid.GuidesFor(Cells(23, 30, 40, 20), [neighbour], G);
        Assert.Empty(unaligned);
    }

    [Fact]
    public void Random_aligned_resizes_stay_valid_and_inside_the_grid_20k()
    {
        var rnd = new Random(5);
        for (var i = 0; i < 20_000; i++)
        {
            var neighbours = Enumerable.Range(0, rnd.Next(0, 6))
                .Select(_ => Cells(rnd.Next(0, 150), rnd.Next(0, 100), rnd.Next(12, 60), rnd.Next(8, 40))).ToList();
            var card = Pixels(Cells(rnd.Next(0, 100), rnd.Next(0, 80), rnd.Next(12, 60), rnd.Next(8, 40)));
            var result = WidgetGrid.Resize(card, (Edges)rnd.Next(1, 16), new Vector(rnd.Next(-800, 800), rnd.Next(-800, 800)), Min, G, neighbours, Tol);

            Assert.True(result.C >= 0 && result.R >= 0 && result.C + result.W <= G.Cols && result.R + result.H <= G.Rows, $"{i}: ekran dışı");
            Assert.True(result.W >= Min.W && result.H >= Min.H, $"{i}: çok küçük");
        }
    }
}
