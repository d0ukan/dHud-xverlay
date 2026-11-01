using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Tam ekran algılama: Windows durum kodları ve titreşim önleme (iki ardışık okuma).</summary>
[Trait(Cat.Name, Cat.Fast)]
public class FullscreenWatcherTests
{
    [Theory]
    [InlineData(1, false)]   // QUNS_NOT_PRESENT
    [InlineData(2, true)]    // QUNS_BUSY: tam ekran uygulama
    [InlineData(3, true)]    // QUNS_RUNNING_D3D_FULL_SCREEN: oyun
    [InlineData(4, true)]    // QUNS_PRESENTATION_MODE
    [InlineData(5, false)]   // QUNS_ACCEPTS_NOTIFICATIONS: normal
    [InlineData(6, false)]   // QUNS_QUIET_TIME
    [InlineData(7, false)]   // QUNS_APP
    public void Only_the_full_screen_states_count(int state, bool expected) =>
        Assert.Equal(expected, FullscreenWatcher.IsFullscreenState(state));

    [Fact]
    public void The_state_changes_only_after_two_equal_readings() => Sta.Run(() =>
    {
        using var watcher = new FullscreenWatcher();
        Assert.False(watcher.Feed(true));      // ilk okuma: aday
        Assert.False(watcher.IsFullscreen);
        Assert.True(watcher.Feed(true));       // ikinci: değişti
        Assert.True(watcher.IsFullscreen);

        Assert.False(watcher.Feed(false));     // tek okumalık sapma
        Assert.True(watcher.IsFullscreen);
        Assert.False(watcher.Feed(true));      // geri döndü: sapma yok sayıldı
        Assert.True(watcher.IsFullscreen);

        Assert.False(watcher.Feed(false));
        Assert.True(watcher.Feed(false));
        Assert.False(watcher.IsFullscreen);
    });

    [Fact]
    public void A_flickering_reading_never_changes_the_state() => Sta.Run(() =>
    {
        using var watcher = new FullscreenWatcher();
        for (var i = 0; i < 100; i++)
        {
            Assert.False(watcher.Feed(i % 2 == 0));
        }

        Assert.False(watcher.IsFullscreen);
    });
}
