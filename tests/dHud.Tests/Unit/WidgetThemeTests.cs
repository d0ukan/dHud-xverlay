using DHud.Services;
using DHud.Widgets;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Widget renk temaları: tutarlı tanımlar, güvenli geri dönüş ve çeviriler.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class WidgetThemeTests
{
    [Fact]
    public void Theme_ids_are_unique_and_the_default_comes_first()
    {
        Assert.Equal(WidgetThemes.All.Count, WidgetThemes.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(WidgetThemes.Default, WidgetThemes.All[0].Id);
        Assert.Equal(new AppSettings().WidgetTheme, WidgetThemes.Default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yok-boyle-bir-tema")]
    public void Unknown_ids_fall_back_to_the_default_theme(string? id) =>
        Assert.Equal(WidgetThemes.Default, WidgetThemes.Get(id).Id);

    [Fact]
    public void Exactly_one_theme_is_light_and_dark_cards_are_dark_enough_for_white_text()
    {
        Assert.Single(WidgetThemes.All, t => t.Light);
        foreach (var theme in WidgetThemes.All.Where(t => !t.Light))
        {
            var luminance = (0.299 * theme.Card.R + 0.587 * theme.Card.G + 0.114 * theme.Card.B) / 255;
            Assert.True(luminance < 0.2, $"{theme.Id} kartı beyaz yazı için yeterince koyu değil ({luminance:0.00})");
        }

        var light = WidgetThemes.All.Single(t => t.Light);
        Assert.True((0.299 * light.Card.R + 0.587 * light.Card.G + 0.114 * light.Card.B) / 255 > 0.8);
    }

    [Fact]
    public void Every_theme_has_an_english_and_a_turkish_name()
    {
        var previous = Loc.Current;
        try
        {
            foreach (var language in new[] { AppLanguage.English, AppLanguage.Turkish })
            {
                Loc.Current = language;
                foreach (var theme in WidgetThemes.All)
                {
                    var name = Loc.T(theme.NameKey);
                    Assert.False(string.IsNullOrWhiteSpace(name) || name == theme.NameKey, $"{theme.NameKey} çevirisi eksik ({language})");
                }
            }
        }
        finally
        {
            Loc.Current = previous;
        }
    }
}
