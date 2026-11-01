using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>Ayar/widget/profil dosyaları: yeniden başlatınca veri duruyor mu, bozulursa kurtarılıyor mu.</summary>
[Trait(Cat.Name, Cat.Integration)]
public class PersistenceTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    // ---- Widget verisi (notlar vb.) ----

    [Fact]
    public void Notes_survive_a_restart_and_the_old_version_is_kept_as_bak() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var first = new WidgetService(path);
        first.Load();
        var note = first.AddNote()!;
        note.Text = "ilk yazı";
        first.Flush();
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"), "geçici dosya kalmamalı");

        note.Text = "ikinci yazı";
        first.ScheduleSave();
        first.Flush();
        Assert.True(File.Exists(path + ".bak"));

        var second = new WidgetService(path);
        second.Load();
        Assert.Equal("ikinci yazı", Assert.Single(second.Data.Notes).Text);
    });

    [Fact]
    public void A_corrupt_main_file_is_recovered_from_the_backup() => Sta.Run(() =>
    {
        var path = _dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        service.AddNote()!.Text = "eski";
        service.Flush();
        service.Data.Notes[0].Text = "yeni";
        service.ScheduleSave();
        service.Flush();

        File.WriteAllText(path, "{ yarım yazılmış json");
        var recovered = new WidgetService(path);
        recovered.Load();
        Assert.Equal("eski", Assert.Single(recovered.Data.Notes).Text);
    });

    [Fact]
    public void A_note_that_was_never_moved_still_saves_because_its_position_is_NaN() => Sta.Run(() =>
    {
        // Geçmişte gerçek hata: konumu NaN olan not JSON'a yazılamıyor ve tüm dosya kaydedilemiyordu.
        var path = _dir.File("widgets.json");
        var service = new WidgetService(path);
        service.Load();
        var note = service.AddNote()!;
        Assert.True(double.IsNaN(note.Left));
        note.Text = "taşınmamış not";
        service.Flush();

        var reloaded = new WidgetService(path);
        reloaded.Load();
        Assert.Equal("taşınmamış not", Assert.Single(reloaded.Data.Notes).Text);
    });

    [Fact]
    public void Missing_or_garbage_files_give_empty_defaults() => Sta.Run(() =>
    {
        var missing = new WidgetService(_dir.File("yok.json"));
        missing.Load();
        Assert.Empty(missing.Data.Notes);

        var garbagePath = _dir.File("cop.json");
        File.WriteAllBytes(garbagePath, [0xFF, 0xFE, 0x00, 0x01, 0x02]);
        var garbage = new WidgetService(garbagePath);
        garbage.Load();
        Assert.Empty(garbage.Data.Notes);
    });

    [Fact]
    public void Loaded_data_is_clamped_to_safe_limits() => Sta.Run(() =>
    {
        var service = new WidgetService(_dir.File("w.json"));
        service.Load();
        var data = new WidgetData();
        for (var i = 0; i < 80; i++)
        {
            data.Notes.Add(new StickyNote { Width = -50, Height = 99999, Color = 77 });
            data.Todos.Add(new TodoItem { Text = "x" });
        }

        service.ReplaceData(data);
        Assert.Equal(WidgetService.MaxNotes, service.Data.Notes.Count);
        Assert.Equal(WidgetService.MaxTodos, service.Data.Todos.Count);
        Assert.All(service.Data.Notes, n =>
        {
            Assert.InRange(n.Width, 160, 900);
            Assert.InRange(n.Height, 100, 900);
            Assert.InRange(n.Color, 0, 5);
        });
    });

    [Fact]
    public void Limits_for_notes_and_reminders() => Sta.Run(() =>
    {
        var service = new WidgetService(_dir.File("w.json"));
        service.Load();
        for (var i = 0; i < WidgetService.MaxNotes; i++)
        {
            Assert.NotNull(service.AddNote());
        }

        Assert.Null(service.AddNote());

        for (var i = 0; i < WidgetService.MaxReminders; i++)
        {
            Assert.NotNull(service.AddReminder("x", DateTime.UtcNow.AddHours(1), "timer"));
        }

        Assert.Null(service.AddReminder("fazla", DateTime.UtcNow.AddHours(1), "timer"));
    });

    [Fact]
    public void Pomodoro_runs_focus_then_short_breaks_and_a_long_break_after_four_rounds() => Sta.Run(() =>
    {
        var service = new WidgetService(_dir.File("w.json"));
        service.Load();
        service.StartPomodoro();
        Assert.Equal(PomodoroPhase.Focus, service.Phase);

        var seen = new List<PomodoroPhase>();
        for (var i = 0; i < 8; i++)
        {
            service.SkipPomodoro();
            seen.Add(service.Phase);
        }

        Assert.Equal(
            [PomodoroPhase.ShortBreak, PomodoroPhase.Focus, PomodoroPhase.ShortBreak, PomodoroPhase.Focus,
             PomodoroPhase.ShortBreak, PomodoroPhase.Focus, PomodoroPhase.LongBreak, PomodoroPhase.Focus], seen);

        service.StopPomodoro();
        Assert.Equal(PomodoroPhase.Off, service.Phase);
    });

    // ---- Ayarlar ----

    [Fact]
    public void Settings_round_trip_including_widget_options_and_fans()
    {
        var path = _dir.File("settings.json");
        var first = new SettingsService(path);
        first.Load();
        first.Current.Scale = 1.5;
        first.Current.ClockOptions.Opacity = 0.6;
        first.Current.Extra(WidgetIds.Todo).Enabled = true;
        first.Current.Fans["/lpc/x/control/0"] = new FanSetting { Mode = FanMode.Manual, ManualPercent = 65 };
        first.NotifyChanged();

        var second = new SettingsService(path);
        second.Load();
        Assert.Equal(1.5, second.Current.Scale);
        Assert.Equal(0.6, second.Current.ClockOptions.Opacity, 6);
        Assert.True(second.Current.Extra(WidgetIds.Todo).Enabled);
        Assert.Equal(65, second.Current.Fans["/lpc/x/control/0"].ManualPercent);
        Assert.Equal(FanMode.Manual, second.Current.Fans["/lpc/x/control/0"].Mode);
    }

    [Fact]
    public void Out_of_range_settings_are_normalized_on_load()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """
            {"Scale": 99, "BackgroundOpacity": -4, "CpuWarnTemperature": 500, "CpuHotTemperature": 3,
             "WeatherLat": 123456, "WeatherLon": 5, "WeatherPlace": "Nerede",
             "ClockOptions": {"Opacity": 7}, "MobileBlockedFans": ["", "a", "a"], "Language": "Turkish"}
            """);
        var settings = new SettingsService(path);
        settings.Load();
        var s = settings.Current;

        Assert.InRange(s.Scale, 0.7, 2.0);
        Assert.InRange(s.BackgroundOpacity, 0.2, 1.0);
        Assert.True(s.CpuHotTemperature > s.CpuWarnTemperature);
        Assert.Null(s.WeatherLat);       // geçersiz koordinat: şehir de temizlenir
        Assert.Equal(string.Empty, s.WeatherPlace);
        Assert.InRange(s.ClockOptions.Opacity, 0.2, 1.0);
        Assert.Equal(["a"], s.MobileBlockedFans);
        Assert.Equal(AppLanguage.Turkish, s.Language);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{ bu json degil")]
    [InlineData("\0\0\0\0")]
    public void Garbage_settings_fall_back_to_defaults_without_throwing(string content)
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, content);
        var settings = new SettingsService(path);
        settings.Load();
        Assert.NotNull(settings.Current);
        Assert.False(settings.Current.FanControlEnabled); // güvenlik: bozuk dosya fan kontrolünü açmaz
    }

    [Fact]
    public void A_custom_data_dir_starts_with_clean_defaults_and_never_adopts_an_old_installation()
    {
        // --data-dir ile çalışan örnek (test / ekran görüntüsü) gerçek kurulumun ayarlarını okumamalı veya taşımamalı.
        Assert.True(DHud.Helpers.AppPaths.IsCustom);
        var settings = new SettingsService();   // yol: AppPaths.File("settings.json") = geçici klasör
        settings.Load();
        Assert.False(settings.Current.FanControlEnabled);
        Assert.False(settings.Current.MobileEnabled);
        Assert.Empty(settings.Current.Fans);
    }

    [Fact]
    public void Settings_file_from_v1_0_9_migrates_the_single_thresholds_to_cpu_and_gpu()
    {
        var path = _dir.File("v109.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v1.0.9.json"), path);
        var settings = new SettingsService(path);
        settings.Load();
        Assert.Equal(65, settings.Current.CpuWarnTemperature);
        Assert.Equal(85, settings.Current.CpuHotTemperature);
        Assert.Equal(65, settings.Current.GpuWarnTemperature);
        Assert.Equal(85, settings.Current.GpuHotTemperature);
    }

    [Fact]
    public void Settings_file_from_v1_0_28_loads_fans_curves_and_repairs_a_decreasing_curve()
    {
        var path = _dir.File("v128.json");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-v1.0.28.json"), path);
        var settings = new SettingsService(path);
        settings.Load();
        var s = settings.Current;

        Assert.Equal(AppLanguage.Turkish, s.Language);
        Assert.Equal(OverlayLayout.Horizontal, s.Layout);
        Assert.Equal("abc", s.MobileToken);
        var fan = Assert.Single(s.Fans).Value;
        Assert.Equal(FanMode.Curve, fan.Mode);
        Assert.Equal(FanSource.Max, fan.Source);
        // Dosyadaki eğri artan değildi (60 -> 30 -> 100): yüklenirken düzeltilir.
        Assert.True(fan.Curve.Zip(fan.Curve.Skip(1)).All(p => p.Second.Percent >= p.First.Percent));
        Assert.Equal("Gece", Assert.Single(s.CurvePresets).Name);
    }
}
