using System.Diagnostics;
using System.Windows;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Widget ızgarası: çakışmasız, sıkı ve hızlı yerleşim (kullanıcının "çok boşluk var" şikâyeti ve ekran görüntüleri baz alındı).</summary>
[Trait(Cat.Name, Cat.Fast)]
public class WidgetGridTests
{
    private static readonly Rect Hd = new(0, 0, 1920, 1040);
    private static readonly Size Card = new(300, 190);

    [Theory]
    [InlineData(1280, 6)]
    [InlineData(1366, 6)]
    [InlineData(1920, 8)]
    [InlineData(2560, 8)]
    [InlineData(3840, 8)]
    public void Cell_size_is_between_6_and_8_dips(double width, double expected) =>
        Assert.Equal(expected, WidgetGrid.Make(new Rect(0, 0, width, width * 0.55)).Cell);

    [Fact]
    public void A_card_wastes_less_than_one_cell_per_axis()
    {
        var g = WidgetGrid.Make(Hd);
        var span = WidgetGrid.SpanOf(Card, g);
        Assert.True(span.W * g.Cell - 2 * WidgetGrid.Inset - Card.Width < g.Cell);
        Assert.True(span.H * g.Cell - 2 * WidgetGrid.Inset - Card.Height < g.Cell);
    }

    [Fact]
    public void Cell_to_pixel_to_cell_round_trips_and_out_of_screen_points_clamp()
    {
        var g = WidgetGrid.Make(Hd);
        var span = WidgetGrid.SpanOf(Card, g);
        Assert.Equal((30, 20), WidgetGrid.NearestCell(WidgetGrid.CardOrigin(30, 20, g), span, g));
        Assert.Equal((0, 0), WidgetGrid.NearestCell(new Point(-500, -500), span, g));
        Assert.Equal((g.Cols - span.W, g.Rows - span.H), WidgetGrid.NearestCell(new Point(9999, 9999), span, g));
    }

    [Fact]
    public void Neighbours_never_overlap_and_sit_snugly_in_all_four_directions()
    {
        var g = WidgetGrid.Make(Hd);
        var span = WidgetGrid.SpanOf(Card, g);
        var first = new Rect(WidgetGrid.CardOrigin(80, 50, g), Card);
        var occupied = WidgetGrid.Occupied([first], g);

        var preferences = new[] { (80 + span.W - 2, 50), (80 - span.W + 2, 50), (80, 50 + span.H - 2), (80, 50 - span.H + 2) };
        foreach (var pref in preferences)
        {
            var spot = WidgetGrid.FindFree(pref, span, occupied, g);
            Assert.NotNull(spot);
            var second = new Rect(WidgetGrid.CardOrigin(spot.Value.C, spot.Value.R, g), Card);
            Assert.False(first.IntersectsWith(second), $"çakıştı: {pref}");

            var gap = Math.Max(Math.Max(second.Left - first.Right, first.Left - second.Right), Math.Max(second.Top - first.Bottom, first.Top - second.Bottom));
            Assert.InRange(gap, 0, 2 * WidgetGrid.Inset + g.Cell);
        }
    }

    [Fact]
    public void A_card_dropped_on_the_top_bar_moves_just_below_it()
    {
        var g = WidgetGrid.Make(Hd);
        var span = WidgetGrid.SpanOf(Card, g);
        var bar = new Rect(700, 10, 520, 26);
        var occupied = WidgetGrid.Occupied([bar], g);
        var spot = WidgetGrid.FindFree((100, 0), span, occupied, g);
        Assert.NotNull(spot);
        var placed = new Rect(WidgetGrid.CardOrigin(spot.Value.C, spot.Value.R, g), Card);
        Assert.False(placed.IntersectsWith(bar));
        Assert.InRange(placed.Top - bar.Bottom, 0, 14);
    }

    [Fact]
    public void On_an_empty_screen_the_preferred_cell_is_kept_and_on_a_full_one_there_is_no_spot()
    {
        var g = WidgetGrid.Make(Hd);
        var span = WidgetGrid.SpanOf(Card, g);
        Assert.Equal((4, 4), WidgetGrid.FindFree((4, 4), span, WidgetGrid.Occupied([], g), g));
        Assert.Null(WidgetGrid.FindFree((0, 0), span, WidgetGrid.Occupied([Hd], g), g));
    }

    [Fact]
    public void A_card_larger_than_the_screen_is_clamped_to_the_grid()
    {
        var g = WidgetGrid.Make(Hd);
        Assert.Equal((g.Cols, g.Rows), WidgetGrid.SpanOf(new Size(9000, 9000), g));
    }

    [Fact]
    public void Edge_touching_rectangles_do_not_share_cells()
    {
        var g = WidgetGrid.Make(Hd);
        var a = WidgetGrid.Occupied([new Rect(WidgetGrid.CardOrigin(10, 10, g), Card)], g);
        var b = WidgetGrid.Occupied([new Rect(WidgetGrid.CardOrigin(90, 10, g), Card)], g);
        Assert.False(a.Overlaps(b));
        Assert.True(a.Overlaps(a));
    }

    [Theory]
    [InlineData(1920, 1040)]
    [InlineData(2560, 1400)]
    [InlineData(3440, 1400)]
    public void Placement_is_fast_enough_to_run_while_dragging(double width, double height)
    {
        var g = WidgetGrid.Make(new Rect(0, 0, width, height));
        var many = Enumerable.Range(0, 12).Select(i => new Rect(100 + i * 140, 100 + i % 3 * 200, 130, 180)).ToList();
        var span = WidgetGrid.SpanOf(Card, g);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 60; i++)
        {
            WidgetGrid.FindFree((g.Cols / 2, g.Rows / 2), span, WidgetGrid.Occupied(many, g), g);
        }

        // Saniyede ~60 çağrı yapılır: bir çağrı 8 ms altında kalmalı (gürültüye karşı cömert sınır).
        Assert.True(sw.Elapsed.TotalMilliseconds / 60 < 8, $"{sw.Elapsed.TotalMilliseconds / 60:0.00} ms/çağrı");
    }
}
