using System.Windows;
using DHud.Services;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Fuzz;

/// <summary>Özellik testleri: rastgele ama tohumlu 100 bin girdi için değişmez kurallar (invariant) sağlanmalı.</summary>
[Trait(Cat.Name, Cat.Fuzz)]
public class PropertyTests
{
    private const int Seed = 20261101;

    private static int Pick(Random rnd, params int[] values) => values[rnd.Next(values.Length)];

    private static FanSetting RandomSetting(Random rnd)
    {
        var setting = new FanSetting
        {
            Mode = (FanMode)rnd.Next(Enum.GetValues<FanMode>().Length),
            Source = (FanSource)rnd.Next(Enum.GetValues<FanSource>().Length),
            ManualPercent = rnd.Next(4) == 0 ? Pick(rnd, int.MinValue, int.MaxValue, -1, 101, 0, 100) : rnd.Next(-50, 160),
            MinPercent = rnd.Next(4) == 0 ? Pick(rnd, int.MinValue, int.MaxValue, -1, 101) : rnd.Next(-50, 160)
        };
        setting.Curve.Clear();
        for (var i = rnd.Next(0, 14); i > 0; i--)
        {
            setting.Curve.Add(new() { Temp = rnd.Next(5) == 0 ? Pick(rnd, int.MinValue, int.MaxValue) : rnd.Next(-100, 300), Percent = rnd.Next(5) == 0 ? Pick(rnd, int.MinValue, int.MaxValue) : rnd.Next(-100, 400) });
        }

        return setting;
    }

    [Fact]
    public void Sanitize_always_produces_a_valid_idempotent_setting_100k()
    {
        var rnd = new Random(Seed);
        for (var i = 0; i < 100_000; i++)
        {
            var setting = RandomSetting(rnd);
            FanCurve.Sanitize(setting);

            Assert.InRange(setting.ManualPercent, 0, 100);
            Assert.InRange(setting.MinPercent, 0, 100);
            Assert.InRange(setting.Curve.Count, 2, 8);
            Assert.All(setting.Curve, p => { Assert.InRange(p.Temp, FanCurve.MinTemp, FanCurve.MaxTemp); Assert.InRange(p.Percent, 0, 100); });
            for (var k = 1; k < setting.Curve.Count; k++)
            {
                Assert.True(setting.Curve[k].Temp > setting.Curve[k - 1].Temp && setting.Curve[k].Percent >= setting.Curve[k - 1].Percent, $"yineleme {i}");
            }

            var before = setting.Curve.Select(p => (p.Temp, p.Percent)).ToList();
            FanCurve.Sanitize(setting);
            Assert.Equal(before, setting.Curve.Select(p => (p.Temp, p.Percent)).ToList());
        }
    }

    [Fact]
    public void The_fan_target_is_always_finite_within_0_to_100_and_emergency_means_100_100k()
    {
        var rnd = new Random(Seed + 1);
        double?[] temps = [null, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue, -273, 0, 30, 999];
        for (var i = 0; i < 100_000; i++)
        {
            var setting = RandomSetting(rnd);
            FanCurve.Sanitize(setting);
            double? temp = rnd.Next(3) == 0 ? temps[rnd.Next(temps.Length)] : rnd.NextDouble() * 140 - 20;
            var missing = rnd.Next(0, 12);
            var emergency = rnd.Next(10) == 0;
            double? last = rnd.Next(4) == 0 ? null : rnd.NextDouble() * 100;

            var target = FanControlService.ComputeTarget(setting, temp, missing, emergency, last);

            Assert.True(double.IsFinite(target), $"yineleme {i}: {target} (mod {setting.Mode}, sıcaklık {temp})");
            Assert.InRange(target, 0, 100);
            if (emergency)
            {
                Assert.Equal(100, target);
            }
        }
    }

    [Fact]
    public void A_free_spot_is_always_inside_the_screen_and_never_overlaps_another_card_20k()
    {
        var rnd = new Random(Seed + 2);
        for (var i = 0; i < 20_000; i++)
        {
            var work = new Rect(0, 0, rnd.Next(1000, 3841), rnd.Next(600, 2161));
            var g = WidgetGrid.Make(work);
            var card = new Size(rnd.Next(60, 520), rnd.Next(40, 420));
            var span = WidgetGrid.SpanOf(card, g);

            var obstacles = new List<Rect>();
            for (var k = rnd.Next(0, 16); k > 0; k--)
            {
                obstacles.Add(new Rect(rnd.Next(-100, (int)work.Width), rnd.Next(-100, (int)work.Height), rnd.Next(20, 520), rnd.Next(20, 420)));
            }

            var occupied = WidgetGrid.Occupied(obstacles, g);
            var spot = WidgetGrid.FindFree((rnd.Next(-20, g.Cols + 20), rnd.Next(-20, g.Rows + 20)), span, occupied, g);
            if (spot is null)
            {
                continue;
            }

            var (c, r) = spot.Value;
            Assert.True(c >= 0 && r >= 0 && c + span.W <= g.Cols && r + span.H <= g.Rows, $"yineleme {i}: ekran dışı ({c},{r}) span {span} ızgara {g.Cols}x{g.Rows}");
            var placed = new Rect(WidgetGrid.CardOrigin(c, r, g), card);
            foreach (var other in obstacles)
            {
                var clipped = Rect.Intersect(other, new Rect(0, 0, work.Width, work.Height));
                Assert.False(!clipped.IsEmpty && clipped.Width > 0 && clipped.Height > 0 && placed.IntersectsWith(clipped), $"yineleme {i}: {placed} ile {other} çakıştı");
            }
        }
    }
}
