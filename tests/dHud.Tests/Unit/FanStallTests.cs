using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>"Fan durdu" uyarısı: yanlış alarm yağmuru kullanıcıyı en çok rahatsız eden şeydi; kurallar sabitlenir.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class FanStallTests : IDisposable
{
    private readonly TempDataDir _dir = new();
    private readonly SettingsService _settings;
    private readonly FanControlService _service;
    private readonly List<IReadOnlyList<(string Name, int Percent)>> _alerts = [];
    private DateTime _now = new(2026, 11, 1, 12, 0, 0);

    public FanStallTests()
    {
        _settings = new SettingsService(_dir.File("settings.json"));
        _settings.Load();
        _service = new FanControlService(new HardwareMonitorService(), _settings);
        _service.FanStalled += _alerts.Add;
    }

    public void Dispose()
    {
        _service.Dispose();
        _dir.Dispose();
    }

    private static FanControlService.State Running(double percent = 60) => new() { Taken = true, Last = percent };

    private void Tick(FanControlService.State state, float? rpm, string name = "System Fan #3", bool gpu = false, double? gpuTemp = null)
    {
        _service.DetectStall(name, gpu, state, rpm, testing: false, gpuTemp, _now);
        _now = _now.AddSeconds(1);
    }

    private void Spin(FanControlService.State state, string name = "System Fan #3") => Tick(state, 1200, name);

    private void Stop(FanControlService.State state, int seconds, string name = "System Fan #3", bool gpu = false, double? gpuTemp = null)
    {
        for (var i = 0; i < seconds; i++)
        {
            Tick(state, 0, name, gpu, gpuTemp);
        }
    }

    [Fact]
    public void A_fan_that_was_spinning_and_stays_at_zero_for_the_limit_raises_one_alert()
    {
        var state = Running();
        Spin(state);
        Stop(state, FanControlService.StallTicks + 1);
        _service.FlushStalls(_now.AddMinutes(1));

        var alert = Assert.Single(_alerts);
        Assert.Equal(("System Fan #3", 60), Assert.Single(alert));
    }

    [Fact]
    public void A_short_dropout_does_not_alert()
    {
        var state = Running();
        Spin(state);
        Stop(state, FanControlService.StallTicks - 5);
        Spin(state);
        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Empty(_alerts);
    }

    [Fact]
    public void A_fan_that_never_spun_does_not_alert_unused_header()
    {
        var state = Running();
        Stop(state, 120);
        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Empty(_alerts);
    }

    [Fact]
    public void Low_commanded_speed_is_ignored_because_many_fans_stop_there_by_design()
    {
        var state = Running(percent: 30);
        Spin(state);
        Stop(state, 120);
        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Empty(_alerts);
    }

    [Fact]
    public void Gpu_fan_in_zero_rpm_mode_is_not_a_fault_when_the_card_is_cool()
    {
        var state = Running(percent: 60);
        Spin(state, "GPU Fan 2");
        Stop(state, 120, "GPU Fan 2", gpu: true, gpuTemp: 40);
        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Empty(_alerts);
    }

    [Fact]
    public void Gpu_fan_that_does_not_spin_while_the_card_is_hot_is_a_fault()
    {
        var state = Running(percent: 70);
        Spin(state, "GPU Fan 2");
        Stop(state, FanControlService.StallTicks + 2, "GPU Fan 2", gpu: true, gpuTemp: 75);
        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Single(_alerts);
    }

    [Fact]
    public void Several_fans_stopping_together_are_reported_in_one_message()
    {
        var a = Running();
        var b = Running();
        var c = Running();
        Spin(a, "System Fan #3");
        Spin(b, "System Fan #4");
        Spin(c, "System Fan #5");
        for (var i = 0; i < FanControlService.StallTicks + 1; i++)
        {
            Tick(a, 0, "System Fan #3");
            Tick(b, 0, "System Fan #4");
            Tick(c, 0, "System Fan #5");
        }

        _service.FlushStalls(_now.AddMinutes(1));
        var message = Assert.Single(_alerts);
        Assert.Equal(3, message.Count);
    }

    [Fact]
    public void The_same_fan_is_not_reported_again_within_the_cooldown_and_messages_are_spaced()
    {
        var state = Running();
        Spin(state);
        Stop(state, FanControlService.StallTicks + 1);
        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Single(_alerts);

        // Fan döner, tekrar durur: 6 saat dolmadan yeni bildirim yok.
        Spin(state);
        Stop(state, FanControlService.StallTicks + 1);
        _service.FlushStalls(_now.AddHours(1));
        Assert.Single(_alerts);
    }

    [Fact]
    public void Alerts_can_be_turned_off_in_settings()
    {
        _settings.Current.FanStallAlerts = false;
        var state = Running();
        Spin(state);
        Stop(state, FanControlService.StallTicks + 5);
        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Empty(_alerts);
    }

    [Fact]
    public void Fans_under_test_are_never_judged()
    {
        var state = Running();
        Spin(state);
        for (var i = 0; i < 100; i++)
        {
            _service.DetectStall("System Fan #3", false, state, 0, testing: true, null, _now);
            _now = _now.AddSeconds(1);
        }

        _service.FlushStalls(_now.AddMinutes(1));
        Assert.Empty(_alerts);
    }
}
