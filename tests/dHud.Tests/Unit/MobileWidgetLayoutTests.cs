using System.Text.Json;
using dHud.Mobile.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Android ana ekran widget'ı: boyuta göre düzen seçimi ve değer biçimleri.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class MobileWidgetLayoutTests
{
    [Theory]
    [InlineData(110, 70, WidgetSize.Small)]     // 2×1 hücre
    [InlineData(250, 70, WidgetSize.Small)]     // 4×1
    [InlineData(250, 110, WidgetSize.Medium)]   // 4×2 (varsayılan)
    [InlineData(180, 145, WidgetSize.Medium)]
    [InlineData(250, 215, WidgetSize.Large)]    // 4×3
    [InlineData(300, 300, WidgetSize.Large)]
    [InlineData(150, 250, WidgetSize.Medium)]   // yüksek ama dar: büyük düzen sığmaz
    [InlineData(0, 0, WidgetSize.Medium)]       // boyut bilinmiyor: eski düzen
    public void The_layout_follows_the_size_the_launcher_reports(int width, int height, WidgetSize expected) =>
        Assert.Equal(expected, WidgetLayout.Choose(width, height));

    [Theory]
    [InlineData(74.4f, "74%")]
    [InlineData(0f, "0%")]
    [InlineData(100f, "100%")]
    [InlineData(null, "--%")]
    [InlineData(-1f, "--%")]
    public void Percent_is_formatted_or_shows_a_placeholder(float? value, string expected) =>
        Assert.Equal(expected, WidgetLayout.Percent(value));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-5f, 0)]
    [InlineData(0f, 0)]
    [InlineData(49.6f, 50)]
    [InlineData(100f, 100)]
    [InlineData(250f, 100)]
    [InlineData(float.NaN, 0)]
    [InlineData(float.PositiveInfinity, 0)]
    public void The_bar_value_is_clamped_to_0_100(float? value, int expected) =>
        Assert.Equal(expected, WidgetLayout.Bar(value));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(10f, 0)]
    [InlineData(74.9f, 0)]
    [InlineData(75f, 1)]
    [InlineData(89.9f, 1)]
    [InlineData(90f, 2)]
    [InlineData(100f, 2)]
    public void Levels_switch_to_warning_at_75_and_critical_at_90(float? value, int expected) =>
        Assert.Equal(expected, WidgetLayout.Level(value));

    [Fact]
    public void Cached_widget_data_from_an_older_app_version_still_loads_with_the_new_fields_empty()
    {
        // Eski sürümün önbelleği yalnızca ilk beş alanı içerir; güncellemeden sonra widget bozulmamalı.
        const string old = """{"Cpu":55.0,"Gpu":60.0,"Fps":120.0,"Pc":"PC","Timestamp":1700000000}""";
        var data = JsonSerializer.Deserialize<WidgetData>(old)!;
        Assert.Equal(55f, data.Cpu);
        Assert.Null(data.Ram);
        Assert.Null(data.Disk);
        Assert.Null(data.CpuLoad);
    }

    [Fact]
    public void Widget_data_round_trips_with_loads_and_disk()
    {
        var data = new WidgetData(55, 60, null, "PC", 1700000000, 12, 34, 65, 96);
        var back = JsonSerializer.Deserialize<WidgetData>(JsonSerializer.Serialize(data))!;
        Assert.Equal(data, back);
    }
}
