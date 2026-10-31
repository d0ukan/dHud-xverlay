using System.Text.Json;

namespace DHud.Services;

/// <summary>
/// Mobil uygulamadan gelen fan komutlarını işler ve fan durumunu mobil için hazırlar.
/// Komutlar yalnızca ayarlarda "mobil uygulama fanları değiştirebilsin" açıksa ve fan kontrolü etkinse uygulanır.
/// Mobil yalnızca niyet bildirir (mod, yüzde, hazır eğri); acil %100, alt sınır ve kademeli yavaşlama gibi
/// güvenlik kuralları <see cref="FanControlService"/> içinde aynen geçerli kalır.
/// </summary>
public sealed class RemoteFanService(HardwareMonitorService monitor, SettingsService settings, FanControlService fan)
{
    /// <summary>Manuel yüzde alt sınırı (donanım kendi alt sınırını ayrıca uygular).</summary>
    private const int MinManualPercent = 20;

    private IReadOnlyList<object> _lastFans = [];

    /// <summary>Telefondan bir değişiklik uygulandı (ana iş parçacığında): kısa açıklama.</summary>
    public event Action<string>? Changed;

    /// <summary>Anlık görüntüyle gönderilen fan bilgisi. Arka plan thread'inden çağrılır; hata olursa son geçerli liste döner.</summary>
    public (object Fans, object State) BuildState()
    {
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
        }
        catch (Exception)
        {
            // Ayarlar o sırada değiştiriliyordu; bir sonraki ölçümde yeniden denenir.
        }

        return (_lastFans, new
        {
            enabled = settings.Current.FanControlEnabled,
            remote = settings.Current.MobileFanControl,
            emergency = fan.Emergency
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

        var cmd = Str(command, "cmd");
        if (cmd == "fan")
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

        if (cmd == "fanAll")
        {
            var includePump = command.TryGetProperty("pump", out var p) && p.ValueKind == JsonValueKind.True;
            var targets = monitor.FanChannels.Where(c => includePump || !IsPump(c.Name)).ToList();
            if (targets.Count == 0)
            {
                return "nofan";
            }

            var summaries = new List<string>();
            foreach (var channel in targets)
            {
                if (!Configure(channel, command, out var summary))
                {
                    return "bad";
                }

                summaries.Add(summary);
            }

            Commit(summaries[0] + $" ({targets.Count})");
            return null;
        }

        return "bad";
    }

    private bool Configure(FanChannel channel, JsonElement command, out string summary)
    {
        summary = string.Empty;
        var s = settings.Current;
        var mode = Str(command, "mode");
        var preset = Str(command, "preset") switch
        {
            "quiet" => FanPreset.Quiet,
            "optimal" => FanPreset.Optimal,
            "max" => FanPreset.MaxCold,
            _ => (FanPreset?)null
        };

        if (!s.Fans.TryGetValue(channel.Id, out var cfg))
        {
            cfg = new FanSetting();
            s.Fans[channel.Id] = cfg;
        }

        if (preset is FanPreset chosen)
        {
            var (curve, min) = FanCurve.Preset(chosen);
            cfg.Mode = FanMode.Curve;
            cfg.Curve = curve.Select(p => new CurvePoint { Temp = p.Temp, Percent = p.Percent }).ToList();
            cfg.MinPercent = min;
            cfg.Source = channel.IsGpu ? FanSource.Gpu
                : channel.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ? FanSource.Cpu
                : FanSource.Max;
            summary = $"{channel.Name}: {FanCurve.PresetName(chosen)}";
            return true;
        }

        switch (mode)
        {
            case "auto":
                cfg.Mode = FanMode.Auto;
                summary = $"{channel.Name}: Auto";
                return true;

            case "curve":
                cfg.Mode = FanMode.Curve;
                summary = $"{channel.Name}: Curve";
                return true;

            case "manual":
                if (!command.TryGetProperty("percent", out var value) || !value.TryGetInt32(out var percent))
                {
                    return false;
                }

                cfg.Mode = FanMode.Manual;
                cfg.ManualPercent = Math.Clamp(percent, MinManualPercent, 100);
                summary = $"{channel.Name}: {cfg.ManualPercent}%";
                return true;

            default:
                return false;
        }
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
