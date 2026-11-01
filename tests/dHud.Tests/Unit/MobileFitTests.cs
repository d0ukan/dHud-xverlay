using dHud.Mobile.Controls;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Android panelinin ekrana sığdırılması: her şey tek ekranda görünmeli (kullanıcının "RAM barları altta kalıyor" şikâyeti).</summary>
[Trait(Cat.Name, Cat.Fast)]
public class MobileFitTests
{
    [Theory]
    [InlineData(800, 600, 1.0)]      // zaten sığıyor: küçültme
    [InlineData(600, 600, 1.0)]
    [InlineData(600, 645, 0.930)]    // hafif taşıyor: hafif küçült
    [InlineData(500, 900, 0.6)]      // çok taşıyor: en az ölçekte dur
    public void The_scale_shrinks_the_panel_just_enough_to_fit(double viewport, double natural, double expected) =>
        Assert.Equal(expected, FitMath.Scale(viewport, natural, 0.6), 2);

    [Fact]
    public void A_panel_that_fits_after_scaling_needs_no_scrolling_and_one_that_does_not_still_scrolls()
    {
        var fit = FitMath.Scale(600, 645, 0.6);
        Assert.True(FitMath.Fits(600, 645, fit));

        var tooTall = FitMath.Scale(300, 900, 0.6);
        Assert.False(FitMath.Fits(300, 900, tooTall));   // en küçük ölçekte bile sığmıyor: kaydırma açık kalır
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(600, 0)]
    [InlineData(double.NaN, 600)]
    [InlineData(600, double.PositiveInfinity)]
    [InlineData(-5, 600)]
    public void Invalid_sizes_never_scale_the_panel(double viewport, double natural) =>
        Assert.Equal(1, FitMath.Scale(viewport, natural, 0.6));

    [Fact]
    public void The_scale_is_always_within_bounds()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 20_000; i++)
        {
            var scale = FitMath.Scale(rnd.Next(1, 2000), rnd.Next(1, 3000), 0.6);
            Assert.InRange(scale, 0.6, 1.0);
        }
    }
}
