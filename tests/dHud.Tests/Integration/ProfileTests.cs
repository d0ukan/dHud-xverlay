using DHud.Services;
using Xunit;

namespace DHud.Tests.Integration;

/// <summary>Profil kaydet/yükle ve yeni bilgisayara taşıma. En önemli kural: fan kontrolü kendiliğinden açılmaz.</summary>
[Trait(Cat.Name, Cat.Integration)]
public class ProfileTests : IDisposable
{
    private readonly TempDataDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class Pc : IDisposable
    {
        public SettingsService Settings { get; }
        public WidgetService Widgets { get; }
        public ProfileService Profiles { get; }

        public Pc(string dir, string name)
        {
            Settings = new SettingsService(Path.Combine(dir, name + "-settings.json"));
            Settings.Load();
            Widgets = new WidgetService(Path.Combine(dir, name + "-widgets.json"));
            Widgets.Load();
            Profiles = new ProfileService(Settings, Widgets, "1.0.test", Path.Combine(dir, name + "-profiles"));
        }

        public void Dispose()
        {
        }
    }

    private Pc OldPc()
    {
        var pc = new Pc(_dir.Path, "old");
        pc.Settings.Current.Scale = 1.5;
        pc.Settings.Current.Compact = true;
        pc.Settings.Current.WidgetClock = true;
        pc.Settings.Current.ClockOptions.Opacity = 0.6;
        pc.Settings.Current.Extra(WidgetIds.Todo).Enabled = true;
        pc.Settings.Current.WeatherPlace = "Istanbul";
        pc.Settings.Current.WeatherLat = 41.01;
        pc.Settings.Current.WeatherLon = 28.95;
        pc.Settings.Current.FanControlEnabled = true;
        pc.Settings.Current.MobileToken = "ESKI-TOKEN";
        pc.Settings.Current.Fans["/lpc/x/control/0"] = new FanSetting { Mode = FanMode.Manual, ManualPercent = 65 };
        pc.Widgets.AddNote()!.Text = "kira 5. gün";
        pc.Widgets.Data.Todos.Add(new TodoItem { Text = "yedek al" });
        pc.Widgets.Data.Reminders.Add(new Reminder { Text = "geçici", DueUtc = DateTime.UtcNow.AddHours(1) });
        return pc;
    }

    [Fact]
    public void An_in_app_profile_contains_neither_notes_nor_the_phone_key() => Sta.Run(() =>
    {
        var old = OldPc();
        var info = old.Profiles.Save("Oyun / Gece:1");
        var file = ProfileService.Read(info.Path);

        Assert.Null(file.Widgets);
        Assert.False(file.HasPairingKey);
        Assert.True(string.IsNullOrEmpty(file.Settings!.MobileToken));
        Assert.Equal(0, Path.GetFileName(info.Path).IndexOfAny(Path.GetInvalidFileNameChars()) + 1);
        Assert.Single(old.Profiles.List());
    });

    [Fact]
    public void Saving_under_an_existing_name_replaces_it_case_insensitively() => Sta.Run(() =>
    {
        var old = OldPc();
        old.Profiles.Save("Gaming");
        old.Settings.Current.Scale = 1.1;
        old.Profiles.Save("gaming");
        var list = old.Profiles.List();
        Assert.Single(list);
        Assert.Equal(1.1, ProfileService.Read(list[0].Path).Settings!.Scale, 6);
    });

    [Fact]
    public void Moving_everything_to_a_new_pc_keeps_the_user_where_they_left_off_but_stays_safe() => Sta.Run(() =>
    {
        var old = OldPc();
        var backup = Path.Combine(_dir.Path, "yedek" + ProfileService.Extension);
        old.Profiles.Export(backup, includeWidgetData: true, includePairingKey: false);
        Assert.False(File.Exists(backup + ".tmp"));

        var file = ProfileService.Read(backup);
        Assert.Equal("kira 5. gün", Assert.Single(file.Widgets!.Notes).Text);
        Assert.Empty(file.Widgets.Reminders);              // geçici hatırlatıcılar taşınmaz
        Assert.False(file.HasPairingKey);

        using var fresh = new Pc(_dir.Path, "new");
        fresh.Settings.Current.MobileToken = "YENI-TOKEN";
        var changed = 0;
        fresh.Settings.Changed += (_, _) => changed++;

        Assert.True(fresh.Profiles.Apply(file, includeWidgetData: true, includePairingKey: false));

        Assert.Equal(1, changed);
        var s = fresh.Settings.Current;
        Assert.Equal(1.5, s.Scale);
        Assert.True(s.Compact);
        Assert.True(s.WidgetClock);
        Assert.Equal(0.6, s.ClockOptions.Opacity, 6);
        Assert.True(s.Extra(WidgetIds.Todo).Enabled);
        Assert.Equal("Istanbul", s.WeatherPlace);
        Assert.Equal(65, s.Fans["/lpc/x/control/0"].ManualPercent);
        Assert.Equal("kira 5. gün", Assert.Single(fresh.Widgets.Data.Notes).Text);
        Assert.Equal("yedek al", Assert.Single(fresh.Widgets.Data.Todos).Text);

        // GÜVENLİK
        Assert.False(s.FanControlEnabled, "fan kontrolü yeni bilgisayarda kendiliğinden açılmamalı");
        Assert.Equal("YENI-TOKEN", s.MobileToken);
    });

    [Fact]
    public void The_phone_key_moves_only_when_asked_and_present() => Sta.Run(() =>
    {
        var old = OldPc();
        var withKey = Path.Combine(_dir.Path, "anahtarli" + ProfileService.Extension);
        old.Profiles.Export(withKey, includeWidgetData: false, includePairingKey: true);
        var file = ProfileService.Read(withKey);
        Assert.True(file.HasPairingKey);

        using var fresh = new Pc(_dir.Path, "new");
        fresh.Settings.Current.MobileToken = "YENI-TOKEN";

        fresh.Profiles.Apply(file, includeWidgetData: false, includePairingKey: false);
        Assert.Equal("YENI-TOKEN", fresh.Settings.Current.MobileToken);

        fresh.Profiles.Apply(file, includeWidgetData: false, includePairingKey: true);
        Assert.Equal("ESKI-TOKEN", fresh.Settings.Current.MobileToken);
        Assert.Empty(fresh.Widgets.Data.Notes);           // widget verisi istenmedi: dokunulmadı
    });

    [Fact]
    public void Applying_an_in_app_profile_never_touches_existing_notes() => Sta.Run(() =>
    {
        var pc = OldPc();
        pc.Profiles.Save("A");
        pc.Settings.Current.Scale = 0.8;
        pc.Widgets.AddNote()!.Text = "ikinci not";

        var file = ProfileService.Read(pc.Profiles.List()[0].Path);
        Assert.False(pc.Profiles.Apply(file, includeWidgetData: false, includePairingKey: false));
        Assert.Equal(1.5, pc.Settings.Current.Scale);
        Assert.Equal(2, pc.Widgets.Data.Notes.Count);
    });

    [Theory]
    [InlineData("{ bu json değil", "bad")]
    [InlineData("{}", "bad")]
    [InlineData("[]", "bad")]
    [InlineData("{\"format\":99,\"settings\":{}}", "newer")]
    [InlineData("{\"Format\":99,\"Settings\":{}}", "newer")]
    public void Invalid_files_are_rejected_with_a_clear_reason(string content, string reason)
    {
        var path = Path.Combine(_dir.Path, "x" + ProfileService.Extension);
        File.WriteAllText(path, content);
        Assert.Equal(reason, Assert.Throws<ProfileException>(() => ProfileService.Read(path)).Reason);
    }

    [Fact]
    public void A_missing_file_and_an_oversized_file_are_rejected()
    {
        Assert.Equal("bad", Assert.Throws<ProfileException>(() => ProfileService.Read(Path.Combine(_dir.Path, "yok.dhudprofile"))).Reason);

        var big = Path.Combine(_dir.Path, "dev.dhudprofile");
        using (var stream = File.Create(big))
        {
            stream.SetLength(21L * 1024 * 1024);
        }

        Assert.Equal("big", Assert.Throws<ProfileException>(() => ProfileService.Read(big)).Reason);
    }

    [Fact]
    public void Names_are_cleaned_and_limited() => Sta.Run(() =>
    {
        var pc = new Pc(_dir.Path, "n");
        Assert.Equal("name", Assert.Throws<ProfileException>(() => pc.Profiles.Save("   ")).Reason);
        Assert.Equal(ProfileService.MaxNameLength, ProfileService.CleanName(new string('x', 100)).Length);
        Assert.Equal("_CON", ProfileService.SafeFileName("CON"));
        Assert.Equal("a_b_c", ProfileService.SafeFileName("a:b*c"));
        Assert.Equal("profil", ProfileService.SafeFileName("..."));

        for (var i = 0; i < ProfileService.MaxProfiles + 5; i++)
        {
            try
            {
                pc.Profiles.Save("p" + i);
            }
            catch (ProfileException ex)
            {
                Assert.Equal("many", ex.Reason);
            }
        }

        Assert.Equal(ProfileService.MaxProfiles, pc.Profiles.List().Count);
        pc.Profiles.Delete(pc.Profiles.List()[0]);
        Assert.Equal(ProfileService.MaxProfiles - 1, pc.Profiles.List().Count);
    });

    [Theory]
    [InlineData(@"..\..\evil")]
    [InlineData(@"../../evil")]
    [InlineData(@"C:\Windows\evil")]
    [InlineData("a\\b")]
    public void A_profile_name_can_never_write_outside_the_profiles_folder(string name) => Sta.Run(() =>
    {
        var pc = new Pc(_dir.Path, "t");
        var info = pc.Profiles.Save(name);
        var folder = Path.GetFullPath(Path.Combine(_dir.Path, "t-profiles"));
        Assert.StartsWith(folder, Path.GetFullPath(info.Path), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(folder.TrimEnd('\\'), Path.GetDirectoryName(Path.GetFullPath(info.Path)));
    });
}
