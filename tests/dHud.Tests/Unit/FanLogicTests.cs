using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Fan güvenliği: dHud'un en kritik mantığı. Donanıma dokunmadan saf fonksiyonlar test edilir.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class FanLogicTests
{
    private static FanSetting Curve(FanPreset preset = FanPreset.Optimal)
    {
        var (curve, min) = FanCurve.Preset(preset);
        return new FanSetting { Mode = FanMode.Curve, Curve = curve, MinPercent = min };
    }

    [Fact]
    public void Emergency_always_runs_fan_at_100_percent()
    {
        foreach (var mode in Enum.GetValues<FanMode>())
        {
            var cfg = new FanSetting { Mode = mode, ManualPercent = 10, MinPercent = 0 };
            Assert.Equal(100, FanControlService.ComputeTarget(cfg, sourceTemp: 20, missingTicks: 0, emergency: true, last: 10));
        }
    }

    [Fact]
    public void Lost_sensor_goes_to_100_after_the_limit_but_holds_last_value_before_it()
    {
        var cfg = Curve();
        Assert.Equal(40, FanControlService.ComputeTarget(cfg, null, 1, false, last: 40));
        Assert.Equal(40, FanControlService.ComputeTarget(cfg, null, FanControlService.MissingTempLimit - 1, false, last: 40));
        Assert.Equal(100, FanControlService.ComputeTarget(cfg, null, FanControlService.MissingTempLimit, false, last: 40));
        Assert.Equal(100, FanControlService.ComputeTarget(cfg, null, 0, false, last: null)); // hiç değer yoksa güvenli taraf
    }

    [Fact]
    public void Fan_never_slows_down_faster_than_the_ramp_limit()
    {
        var cfg = Curve(FanPreset.Quiet);
        double? last = 100;
        for (var i = 0; i < 60; i++)
        {
            var next = FanControlService.ComputeTarget(cfg, 25, 0, false, last);
            Assert.True(last - next <= FanControlService.MaxRampDownPerTick + 1e-9, $"tik {i}: {last} -> {next}");
            last = next;
        }

        Assert.Equal(20, FanControlService.ComputeTarget(cfg, 25, 0, false, 20), 6); // ilk noktanın altında eğri düz kalır (Sessiz: %20)
    }

    [Fact]
    public void Speeding_up_is_immediate()
    {
        var cfg = Curve();
        Assert.True(FanControlService.ComputeTarget(cfg, 90, 0, false, last: 20) >= 100 - 1e-9);
    }

    [Fact]
    public void Result_is_never_below_the_minimum_percent()
    {
        var cfg = Curve(FanPreset.MaxCold);
        for (var temp = 0; temp <= 110; temp += 5)
        {
            Assert.True(FanControlService.ComputeTarget(cfg, temp, 0, false, null) >= cfg.MinPercent);
        }
    }

    [Theory]
    [InlineData(FanPreset.Quiet)]
    [InlineData(FanPreset.Optimal)]
    [InlineData(FanPreset.MaxCold)]
    public void Presets_are_valid_and_reach_100_percent_by_85_degrees(FanPreset preset)
    {
        var (curve, min) = FanCurve.Preset(preset);
        Assert.InRange(curve.Count, 2, FanCurve.MaxPoints);
        Assert.InRange(min, 0, 100);
        Assert.True(curve.Zip(curve.Skip(1)).All(p => p.Second.Temp > p.First.Temp && p.Second.Percent >= p.First.Percent), "artan ve sıralı");
        Assert.Equal(100, FanCurve.Interpolate(curve, 85));
    }

    [Fact]
    public void Interpolate_is_linear_between_points_and_flat_outside()
    {
        List<CurvePoint> points = [new() { Temp = 30, Percent = 20 }, new() { Temp = 70, Percent = 100 }];
        Assert.Equal(20, FanCurve.Interpolate(points, 0));
        Assert.Equal(60, FanCurve.Interpolate(points, 50), 6);
        Assert.Equal(100, FanCurve.Interpolate(points, 95));
        Assert.Equal(100, FanCurve.Interpolate([], 50)); // eğri yoksa güvenli taraf
    }

    [Fact]
    public void Sanitize_repairs_broken_settings()
    {
        var setting = new FanSetting
        {
            ManualPercent = 500,
            MinPercent = -3,
            Curve =
            [
                new() { Temp = 90, Percent = 10 }, new() { Temp = -40, Percent = 300 }, new() { Temp = 50, Percent = 70 },
                new() { Temp = 50, Percent = 5 }, new() { Temp = 60, Percent = 30 }
            ]
        };
        FanCurve.Sanitize(setting);

        Assert.Equal(100, setting.ManualPercent);
        Assert.Equal(0, setting.MinPercent);
        Assert.True(setting.Curve.Count >= 2);
        Assert.All(setting.Curve, p => { Assert.InRange(p.Temp, FanCurve.MinTemp, FanCurve.MaxTemp); Assert.InRange(p.Percent, 0, 100); });
        Assert.True(setting.Curve.Zip(setting.Curve.Skip(1)).All(p => p.Second.Temp > p.First.Temp && p.Second.Percent >= p.First.Percent));
    }

    [Fact]
    public void Sanitize_replaces_a_degenerate_curve_with_the_default_and_is_idempotent()
    {
        var setting = new FanSetting { Curve = [new() { Temp = 40, Percent = 40 }] };
        FanCurve.Sanitize(setting);
        Assert.True(FanCurve.SameCurve(setting.Curve, FanCurve.Default()));

        var before = setting.Curve.Select(p => (p.Temp, p.Percent)).ToList();
        FanCurve.Sanitize(setting);
        Assert.Equal(before, setting.Curve.Select(p => (p.Temp, p.Percent)).ToList());
    }

    [Fact]
    public void SourceTemp_picks_the_hotter_when_both_exist_and_falls_back_when_one_is_missing()
    {
        Assert.Equal(70, FanControlService.SourceTemp(FanSource.Max, 70, 50));
        Assert.Equal(80, FanControlService.SourceTemp(FanSource.Max, 40, 80));
        Assert.Equal(60, FanControlService.SourceTemp(FanSource.Max, null, 60));
        Assert.Equal(60, FanControlService.SourceTemp(FanSource.Max, 60, null));
        Assert.Null(FanControlService.SourceTemp(FanSource.Max, null, null));
        Assert.Null(FanControlService.SourceTemp(FanSource.Cpu, null, 70));
        Assert.Null(FanControlService.SourceTemp(FanSource.Gpu, 70, null));
    }

    [Theory]
    [InlineData(800, 1500, 50, true)]    // devir arttı
    [InlineData(1883, 1901, 68, false)]  // artmadı (kullanıcının gerçek #1 başlığı)
    [InlineData(2600, 2650, 95, true)]   // zaten en yüksekte ve dönüyor
    [InlineData(0, 0, 50, false)]
    public void Fan_test_verdict(float before, float peak, float percentBefore, bool expected) =>
        Assert.Equal(expected, FanControlService.TestPassed(before, peak, percentBefore));

    [Fact]
    public void Header_without_a_fan_is_told_apart_from_a_fan_that_does_not_respond()
    {
        Assert.True(FanControlService.IsNoFan(0, 0));
        Assert.True(FanControlService.IsNoFan(30, 40));
        Assert.False(FanControlService.IsNoFan(1883, 1901));
    }

    [Theory]
    [InlineData("Quiet")]
    [InlineData("quiet")]
    [InlineData("Sessiz")]
    [InlineData("SESSIZ")]
    [InlineData("Optimal")]
    [InlineData("MAX COLD")]
    [InlineData("Max Soğutma")]
    [InlineData("Custom")]
    [InlineData("özel")]
    public void Reserved_curve_names_are_rejected_in_both_languages_case_insensitively(string name) =>
        Assert.True(FanCurve.IsReservedName(name));

    [Theory]
    [InlineData("Gaming")]
    [InlineData("Gece")]
    [InlineData("")]
    public void Ordinary_names_are_allowed(string name) => Assert.False(FanCurve.IsReservedName(name));
}
