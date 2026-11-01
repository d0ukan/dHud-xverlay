using System.Diagnostics;
using System.Text.Json;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>
/// Fan bekçisi ve çökme kurtarma. GERÇEK donanıma yazılmaz: bekçi <c>--dry-run</c> ile çağrılır, ölü/canlı süreçler için küçük yardımcı
/// süreçler kullanılır. (Test ana makinenin fanlarına dokunmaz.)
/// </summary>
[Trait(Cat.Name, Cat.Integration)]
public class FanSafetyTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static int DeadPid()
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit();
        return process.Id;
    }

    private static Process LiveProcess() =>
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 20 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;

    private string WriteMarker(int pid, params string[] fans)
    {
        var path = _dir.File("fans-active.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { pid, fans }));
        return path;
    }

    [Fact]
    public void Marker_of_a_dead_process_is_taken_once_and_deleted()
    {
        var path = WriteMarker(DeadPid(), "/lpc/x/control/0", "/gpu/0/control/0");

        var fans = FanSafety.TakePreviousCrashMarker();
        Assert.Equal(["/lpc/x/control/0", "/gpu/0/control/0"], fans);
        Assert.False(File.Exists(path));
        Assert.Null(FanSafety.TakePreviousCrashMarker());   // ikinci kez: temiz
    }

    [Fact]
    public void Marker_of_a_process_that_is_still_running_is_left_alone()
    {
        using var other = LiveProcess();
        try
        {
            var path = WriteMarker(other.Id, "a");
            Assert.Null(FanSafety.TakePreviousCrashMarker());   // başka bir dHud örneği hâlâ çalışıyor: ona dokunma
            Assert.True(File.Exists(path));
        }
        finally
        {
            other.Kill();
        }
    }

    [Theory]
    [InlineData("{ bozuk")]
    [InlineData("")]
    [InlineData("[]")]
    public void A_broken_marker_never_throws(string content)
    {
        File.WriteAllText(_dir.File("fans-active.json"), content);
        Assert.Null(FanSafety.TakePreviousCrashMarker());
    }

    [Fact]
    public void Clearing_removes_the_marker_and_is_safe_when_there_is_none()
    {
        var path = WriteMarker(DeadPid(), "a");
        FanSafety.Clear();
        Assert.False(File.Exists(path));
        FanSafety.Clear(); // yoksa da hata vermez
    }

    [Fact]
    public void Watchdog_does_nothing_after_a_clean_exit()
    {
        var marker = _dir.File("yok.json");
        var code = FanSafety.RunWatchdog(["--fan-watchdog", DeadPid().ToString(), "--marker", marker, "--dry-run"]);
        Assert.Equal(0, code);
        Assert.False(File.Exists(marker + ".acted"));
    }

    [Fact]
    public void Watchdog_acts_on_the_marked_fans_when_dhud_died_without_cleaning_up()
    {
        var marker = WriteMarker(DeadPid(), "/lpc/x/control/0", "/gpu/0/control/0");
        var code = FanSafety.RunWatchdog(["--fan-watchdog", DeadPid().ToString(), "--marker", marker, "--dry-run"]);

        Assert.Equal(10, code);
        Assert.Equal("/lpc/x/control/0,/gpu/0/control/0", File.ReadAllText(marker + ".acted"));
    }

    [Theory]
    [InlineData("--fan-watchdog")]
    [InlineData("--fan-watchdog", "abc")]
    public void Watchdog_with_bad_arguments_exits_with_an_error_code(params string[] args) =>
        Assert.Equal(2, FanSafety.RunWatchdog(args));
}
