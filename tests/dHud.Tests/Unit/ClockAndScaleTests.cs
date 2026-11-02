using System.Windows;
using DHud.Services;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Saat stilleri (okla gezme, analog kol açıları, çeviriler) ve genel widget ölçeği.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class ClockAndScaleTests
{
    [Fact]
    public void Clock_styles_are_unique_start_with_the_default_and_unknown_ids_fall_back()
    {
        Assert.Equal(ClockStyles.All.Count, ClockStyles.All.Distinct().Count());
        Assert.Equal(ClockStyles.Default, ClockStyles.All[0]);
        Assert.Equal(ClockStyles.Default, new AppSettings().ClockStyle);
        Assert.InRange(ClockStyles.All.Count, 3, 8);
        foreach (var bad in new string?[] { null, "", "nope", "ANALOG" })
        {
            Assert.Equal(ClockStyles.Default, ClockStyles.Normalize(bad));
        }
    }

    [Fact]
    public void Stepping_wraps_around_in_both_directions_and_visits_every_style()
    {
        var id = ClockStyles.All[0];
        var seen = new HashSet<string>();
        for (var i = 0; i < ClockStyles.All.Count; i++)
        {
            seen.Add(id);
            id = ClockStyles.Step(id, +1);
        }

        Assert.Equal(ClockStyles.All[0], id);
        Assert.Equal(ClockStyles.All.Count, seen.Count);
        Assert.Equal(ClockStyles.All[^1], ClockStyles.Step(ClockStyles.All[0], -1));
        Assert.Equal(ClockStyles.All[0], ClockStyles.Step("garbage", 0));
    }

    [Theory]
    [InlineData(3, 0, 0, 90, 0, 0)]
    [InlineData(6, 30, 30, 195, 183, 180)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(12, 15, 0, 7.5, 90, 0)]
    [InlineData(23, 59, 59, 359.5, 359.9, 354)]
    public void Analog_hands_point_where_a_real_clock_would(int h, int m, int s, double hour, double minute, double second)
    {
        var t = new DateTime(2026, 1, 1, h, m, s);
        Assert.Equal(hour, ClockStyles.HourAngle(t), 1);
        Assert.Equal(minute, ClockStyles.MinuteAngle(t), 1);
        Assert.Equal(second, ClockStyles.SecondAngle(t), 1);
    }

    [Fact]
    public void Every_clock_style_has_an_english_and_a_turkish_name_and_so_do_the_new_labels()
    {
        var previous = Loc.Current;
        try
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.Turkish })
            {
                Loc.Current = language;
                foreach (var key in ClockStyles.All.Select(id => "Clock." + id).Concat(["W.ClockStyle", "W.ClockPrev", "W.ClockNext", "S.WidgetScale", "S.WidgetScaleHint", "Theme.glass"]))
                {
                    var text = Loc.T(key);
                    Assert.False(string.IsNullOrWhiteSpace(text) || text == key, $"{key} çevirisi eksik ({language})");
                }
            }
        }
        finally
        {
            Loc.Current = previous;
        }
    }

    [Fact]
    public void The_glass_theme_is_translucent_and_the_others_stay_nearly_opaque()
    {
        var glass = WidgetThemes.Get("glass");
        Assert.Equal("glass", glass.Id);
        Assert.InRange(glass.Card.A, 0x40, 0xA0);
        Assert.All(WidgetThemes.All.Where(t => t.Id != "glass"), t => Assert.True(t.Card.A >= 0xE0, t.Id));
    }

    [Fact]
    public void Scale_shrinks_the_grid_cell_but_never_below_three_dips_and_ignores_garbage()
    {
        var work = new Rect(0, 0, 2560, 1400);
        try
        {
            WidgetGrid.SetScale(1);
            var full = WidgetGrid.Make(work);
            WidgetGrid.SetScale(0.5);
            var half = WidgetGrid.Make(work);
            Assert.True(half.Cell < full.Cell);
            Assert.True(half.Cell >= 3);
            Assert.True(half.Cols > full.Cols);

            // Aynı hücre sayısı kapladığı alanın yarısı kadar yer kaplar.
            Assert.InRange(half.Cell * 10 / (full.Cell * 10), 0.45, 0.6);

            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, 5, 0, -3, 0.1 })
            {
                WidgetGrid.SetScale(bad);
                Assert.InRange(WidgetGrid.Scale, WidgetGrid.MinScale, 1);
                Assert.True(WidgetGrid.Make(work).Cell >= 3);
            }
        }
        finally
        {
            WidgetGrid.SetScale(1);
        }
    }

    [Fact]
    public void A_scaled_grid_still_places_cards_without_overlap_and_inside_the_screen()
    {
        try
        {
            WidgetGrid.SetScale(0.7);
            var g = WidgetGrid.Make(new Rect(0, 0, 1920, 1040));
            var span = WidgetGrid.SpanOf(new Size(300, 190), g);
            var a = WidgetGrid.CellsRect(0, 0, span, g);
            var b = WidgetGrid.CellsRect(span.W, 0, span, g);
            Assert.False(a.IntersectsWith(b));
            Assert.True(new Rect(0, 0, 1920, 1040).Contains(b));
        }
        finally
        {
            WidgetGrid.SetScale(1);
        }
    }

    [Fact]
    public void The_default_scale_is_full_size()
    {
        Assert.Equal(1.0, new AppSettings().WidgetScale);
    }
}
