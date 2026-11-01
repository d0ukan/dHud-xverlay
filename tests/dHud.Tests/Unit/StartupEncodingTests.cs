using System.Globalization;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>schtasks çıktısının kod sayfası: bazı kültürlerde OEM kod sayfası geçersiz (1) çıkar ve uygulama çöküyordu.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class StartupEncodingTests
{
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("")]   // invariant: OEM kod sayfası 1
    [InlineData("ja-JP")]
    [InlineData("ar-SA")]
    public void An_output_encoding_is_found_for_every_culture(string name)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            Assert.NotNull(StartupManager.OemEncoding());
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }
}
