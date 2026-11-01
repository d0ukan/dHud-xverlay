using System.Windows;
using DHud.Widgets;
using Xunit;
using Edges = DHud.Widgets.WidgetGrid.Edges;

namespace DHud.Tests.Unit;

/// <summary>Widget'ı kenarlardan çekerek boyutlandırma: kenarlar ızgara çizgilerine oturur, komşuyla sıkı "puzzle" düzeni oluşur.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class WidgetResizeTests
{
    private static readonly WidgetGrid.Spec G = WidgetGrid.Make(new Rect(0, 0, 1920, 1040));
    private static readonly (int W, int H) Min = (12, 8);

    private static Rect CardAt(int c, int r, int w, int h) => WidgetGrid.CellsRect(c, r, (w, h), G);

    [Fact]
    public void Dragging_the_right_edge_grows_the_card_by_whole_cells_and_keeps_the_left_edge()
    {
        var card = CardAt(50, 40, 40, 25);
        var (c, r, w, h) = WidgetGrid.Resize(card, Edges.Right, new Vector(8 * 10 + 1, 999), Min, G); // dikey çekiş yok sayılır
        Assert.Equal((50, 40, 50, 25), (c, r, w, h));
    }

    [Fact]
    public void Dragging_the_left_edge_moves_the_card_and_keeps_the_right_edge()
    {
        var card = CardAt(50, 40, 40, 25);
        var (c, _, w, _) = WidgetGrid.Resize(card, Edges.Left, new Vector(-G.Cell * 7.2, 0), Min, G);
        Assert.Equal(43, c);
        Assert.Equal(47, w);
        Assert.Equal(90, c + w);   // sağ kenar yerinde
    }

    [Fact]
    public void A_corner_changes_both_axes_and_the_result_is_always_a_whole_number_of_cells()
    {
        var card = CardAt(30, 30, 40, 25);
        var (c, r, w, h) = WidgetGrid.Resize(card, Edges.Left | Edges.Top, new Vector(-33.3, -21.7), Min, G);
        var rect = WidgetGrid.CellsRect(c, r, (w, h), G);
        Assert.Equal(card.Right, rect.Right, 6);
        Assert.Equal(card.Bottom, rect.Bottom, 6);
        Assert.True(w > 40 && h > 25);
    }

    [Fact]
    public void The_card_never_shrinks_below_the_minimum_even_when_dragged_across_the_opposite_edge()
    {
        var card = CardAt(50, 40, 40, 25);
        var right = WidgetGrid.Resize(card, Edges.Right, new Vector(-5000, 0), Min, G);
        Assert.Equal(Min.W, right.W);
        Assert.Equal(50, right.C);

        var left = WidgetGrid.Resize(card, Edges.Left, new Vector(5000, 0), Min, G);
        Assert.Equal(Min.W, left.W);
        Assert.Equal(90, left.C + left.W);

        var bottom = WidgetGrid.Resize(card, Edges.Bottom, new Vector(0, -5000), Min, G);
        Assert.Equal(Min.H, bottom.H);
    }

    [Fact]
    public void The_card_never_leaves_the_screen()
    {
        var card = CardAt(50, 40, 40, 25);
        var big = WidgetGrid.Resize(card, Edges.Right | Edges.Bottom, new Vector(99999, 99999), Min, G);
        Assert.Equal(G.Cols, big.C + big.W);
        Assert.Equal(G.Rows, big.R + big.H);

        var small = WidgetGrid.Resize(card, Edges.Left | Edges.Top, new Vector(-99999, -99999), Min, G);
        Assert.Equal(0, small.C);
        Assert.Equal(0, small.R);
    }

    [Fact]
    public void Two_neighbours_resized_to_each_others_edges_leave_exactly_the_normal_gap()
    {
        var left = CardAt(10, 10, 40, 25);
        var right = CardAt(50, 10, 40, 25);   // hücre sınırında bitişik
        Assert.Equal(2 * WidgetGrid.Inset, right.Left - left.Right, 6);

        // Sağdakinin sol kenarı, soldakinin sağ kenarına doğru çekilince tam bu boşlukta durur.
        var grown = WidgetGrid.Resize(right, Edges.Left, new Vector(-(G.Cell * 3) + 1, 0), Min, G);
        var rect = WidgetGrid.CellsRect(grown.C, grown.R, (grown.W, grown.H), G);
        Assert.Equal(left.Right + 2 * WidgetGrid.Inset - 3 * G.Cell, rect.Left, 6);
    }

    [Fact]
    public void The_size_in_pixels_matches_the_cell_span()
    {
        var size = WidgetGrid.SizeOf((40, 25), G);
        Assert.Equal(40 * G.Cell - 2 * WidgetGrid.Inset, size.Width, 6);
        Assert.Equal(25 * G.Cell - 2 * WidgetGrid.Inset, size.Height, 6);
    }

    [Fact]
    public void Random_drags_always_produce_valid_spans_within_the_grid_20k()
    {
        var rnd = new Random(11);
        for (var i = 0; i < 20_000; i++)
        {
            var w = rnd.Next(12, 80);
            var h = rnd.Next(8, 60);
            var c = rnd.Next(0, G.Cols - w);
            var r = rnd.Next(0, G.Rows - h);
            var edges = (Edges)rnd.Next(1, 16);
            var result = WidgetGrid.Resize(CardAt(c, r, w, h), edges, new Vector(rnd.Next(-3000, 3000), rnd.Next(-3000, 3000)), Min, G);

            Assert.True(result.C >= 0 && result.R >= 0, $"{i}: negatif");
            Assert.True(result.C + result.W <= G.Cols && result.R + result.H <= G.Rows, $"{i}: ekran dışı");
            Assert.True(result.W >= Min.W && result.H >= Min.H, $"{i}: çok küçük");
        }
    }
}
