using System.Text.Json;

namespace DHud.Services;

/// <summary>
/// Mobil uygulamadan gelen fan komutlarını işler ve fan durumunu mobil için hazırlar.
/// Telefon yalnızca ÖNCEDEN HAZIRLANMIŞ seçenekleri seçebilir: hazır eğriler (Sessiz / Optimal / Max Cold),
/// PC'de kaydedilmiş eğriler, fanı otomatiğe vermek ve PC'de kaydedilmiş tüm-fan profilleri.
/// Elle yüzde veya eğri noktası gönderilemez. Komutlar yalnızca ayarlarda "mobil uygulama fanları değiştirebilsin"
/// açıksa ve fan kontrolü etkinse uygulanır; acil %100, alt sınır ve kademeli yavaşlama gibi güvenlik kuralları
/// <see cref="FanControlService"/> içinde aynen geçerli kalır.
/// </summary>
public sealed class RemoteFanService(HardwareMonitorService monitor, SettingsService settings, FanControlService fan)
{
    private IReadOnlyList<object> _lastFans = [];

    /// <summary>Telefondan bir değişiklik uygulandı (ana iş parçacığında): kısa açıklama.</summary>
    public event Action<string>? Changed;

    /// <summary>Anlık görüntüyle gönderilen fan bilgisi. Arka plan thread'inden çağrılır; hata olursa son geçerli liste döner.</summary>
    public (object Fans, object State) BuildState()
    {
        object? curves = null;
        object? profiles = null;
        try
        {
            var s = settings.Current;
            var status = fan.Status;
            var list = new List<object>(status.Count);
            foreach (var f in status)
            {
                s.Fans.TryGetValue(f.Id, out var cfg);
                var mode = s.FanControlEnabled ? cfg?.Mode ?? FanMode.Auto : FanMode.Auto;
                list.Add(new
                {
                    id = f.Id,
                    name = f.Name,
                    gpu = f.IsGpu,
                    rpm = f.Rpm,
                    pct = f.Percent,
                    mode = mode.ToString().ToLowerInvariant(),
                    curve = mode == FanMode.Curve && cfg is not null ? FanCurve.Describe(cfg.Curve, s.CurvePresets) : null,
                    manual = cfg?.ManualPercent ?? 50,
                    stalled = f.Stalled,
                    pump = IsPump(f.Name)
                });
            }

            _lastFans = list;
            curves = s.CurvePresets.Select(c => c.Name).ToArray();
            profiles = s.FanProfiles.Select(p => p.Name).ToArray();
        }
        catch (Exception)
        {
            // Ayarlar o sırada değiştiriliyordu; bir sonraki ölçümde yeniden denenir.
        }

        return (_lastFans, new
        {
            enabled = settings.Current.FanControlEnabled,
            remote = settings.Current.MobileFanControl,
            emergency = fan.Emergency,
            curves,
            profiles
        });
    }

    /// <summary>
    /// Komutu uygular. ANA İŞ PARÇACIĞINDA çağrılmalıdır (ayar nesnelerini değiştirir).
    /// Dönüş: null = başarılı, aksi halde hata kodu (<c>disabled</c>, <c>fanctl</c>, <c>nofan</c>, <c>bad</c>).
    /// </summary>
    public string? Apply(JsonElement command)
    {
        var s = settings.Current;
        if (!s.MobileFanControl)
        {
            return "disabled";
        }

        if (!s.FanControlEnabled)
        {
            return "fanctl";
        }

        switch (Str(command, "cmd"))
        {
            case "fan":
            {
                var id = Str(command, "id");
                var channel = monitor.FanChannels.FirstOrDefault(c => c.Id == id);
                if (channel is null)
                {
                    return "nofan";
                }

                if (!Configure(channel, command, out var summary))
                {
                    return "bad";
                }

                Commit(summary);
                return null;
            }

            case "profile":
            {
                var name = Str(command, "name");
                var profile = s.FanProfiles.FirstOrDefault(p => p.Name == name);
                if (profile is null)
                {
                    return "bad";
                }

                s.Fans = profile.Fans.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
                Commit(profile.Name);
                return null;
            }

            default:
                return "bad";
        }
    }

    /// <summary>Hazır eğri (preset), kayıtlı eğri (saved) veya otomatik (mode=auto) seçimini fana uygular.</summary>
    private bool Configure(FanChannel channel, JsonElement command, out string summary)
    {
        summary = string.Empty;
        var s = settings.Current;

        List<CurvePoint>? curve = null;
        int min = 20;
        string label;

        var preset = Str(command, "preset");
        var saved = Str(command, "saved");
        if (preset.Length > 0)
        {
            FanPreset? chosen = preset switch
            {
                "quiet" => FanPreset.Quiet,
                "optimal" => FanPreset.Optimal,
                "max" => FanPreset.MaxCold,
                _ => null
            };
            if (chosen is not FanPreset p)
            {
                return false;
            }

            (curve, min) = FanCurve.Preset(p);
            label = FanCurve.PresetName(p);
        }
        else if (saved.Length > 0)
        {
            var found = s.CurvePresets.FirstOrDefault(c => c.Name == saved);
            if (found is null)
            {
                return false;
            }

            curve = found.Curve;
            min = found.MinPercent;
            label = found.Name;
        }
        else if (Str(command, "mode") == "auto")
        {
            label = "Auto";
        }
        else
        {
            return false;
        }

        if (!s.Fans.TryGetValue(channel.Id, out var cfg))
        {
            cfg = new FanSetting();
            s.Fans[channel.Id] = cfg;
        }

        if (curve is null)
        {
            cfg.Mode = FanMode.Auto;
        }
        else
        {
            cfg.Mode = FanMode.Curve;
            cfg.Curve = curve.Select(pt => new CurvePoint { Temp = pt.Temp, Percent = pt.Percent }).ToList();
            cfg.MinPercent = min;
            cfg.Source = channel.IsGpu ? FanSource.Gpu
                : channel.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ? FanSource.Cpu
                : FanSource.Max;
        }

        summary = $"{channel.Name}: {label}";
        return true;
    }

    private void Commit(string summary)
    {
        settings.NotifyChanged();
        Changed?.Invoke(summary);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static bool IsPump(string name) =>
        name.Contains("pump", StringComparison.OrdinalIgnoreCase) || name.Contains("AIO", StringComparison.OrdinalIgnoreCase);
}
