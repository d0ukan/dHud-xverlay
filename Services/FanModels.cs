namespace DHud.Services;

public enum FanMode
{
    /// <summary>BIOS/sürücü kontrol eder; dHud dokunmaz.</summary>
    Auto,

    /// <summary>Sabit yüzde.</summary>
    Manual,

    /// <summary>Sıcaklığa göre eğri.</summary>
    Curve
}

public enum FanPreset
{
    Quiet,
    Optimal,
    MaxCold
}

public enum FanSource
{
    Cpu,
    Gpu,

    /// <summary>CPU ve GPU sıcaklığının yüksek olanı (kasa fanları için).</summary>
    Max
}

/// <summary>Eğri noktası: bu sıcaklıkta (°C) fan bu yüzdede (%) çalışır.</summary>
public sealed class CurvePoint
{
    public int Temp { get; set; }
    public int Percent { get; set; }
}

/// <summary>Tek bir fan kanalının ayarı. Anahtar: sensör kimliği (ayarlarda <see cref="AppSettings.Fans"/>).</summary>
public sealed class FanSetting
{
    public FanMode Mode { get; set; } = FanMode.Auto;
    public int ManualPercent { get; set; } = 50;
    public FanSource Source { get; set; } = FanSource.Cpu;

    /// <summary>Fan bu yüzdenin altına inmez (eğri ve manuel mod için).</summary>
    public int MinPercent { get; set; } = 20;

    public List<CurvePoint> Curve { get; set; } = FanCurve.Default();

    public FanSetting Clone() => new()
    {
        Mode = Mode,
        ManualPercent = ManualPercent,
        Source = Source,
        MinPercent = MinPercent,
        Curve = Curve.Select(p => new CurvePoint { Temp = p.Temp, Percent = p.Percent }).ToList()
    };
}

/// <summary>Tüm fanların ayarını bir adla saklar; sonra tek tıkla yüklenir.</summary>
public sealed class FanProfile
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, FanSetting> Fans { get; set; } = [];
}

/// <summary>Kullanıcının adlandırıp kaydettiği fan eğrisi.</summary>
public sealed class SavedCurve
{
    public string Name { get; set; } = string.Empty;
    public List<CurvePoint> Curve { get; set; } = [];
    public int MinPercent { get; set; } = 20;
}

/// <summary>Eğri hesapları. Saf fonksiyonlar; donanıma dokunmaz.</summary>
public static class FanCurve
{
    public const int MinTemp = 20;
    public const int MaxTemp = 100;
    public const int MaxPoints = 8;

    public static List<CurvePoint> Default() => Preset(FanPreset.Optimal).Curve;

    /// <summary>
    /// Hazır eğriler. Hepsi en geç 85 °C'de %100'e ulaşır; hiçbiri sıcak bir sistemi düşük hızda bırakmaz.
    /// </summary>
    public static (List<CurvePoint> Curve, int MinPercent) Preset(FanPreset preset) => preset switch
    {
        // Sessiz: 60 °C'ye kadar çok yavaş, sonra hızlı yükselir.
        FanPreset.Quiet =>
        (
            [P(30, 20), P(50, 25), P(60, 30), P(70, 45), P(78, 70), P(85, 100)], 15
        ),

        // Max Cold: düşük sıcaklıkta bile yüksek hız; en soğuk, en gürültülü.
        FanPreset.MaxCold =>
        (
            [P(30, 50), P(40, 60), P(50, 75), P(60, 90), P(70, 100)], 50
        ),

        // Optimal: dengeli.
        _ =>
        (
            [P(30, 25), P(50, 35), P(65, 55), P(75, 80), P(85, 100)], 20
        )
    };

    public const int MaxSavedCurves = 20;
    public const int MaxProfiles = 10;
    public const int MaxNameLength = 24;

    /// <summary>İki eğri aynı noktalara sahip mi (sıra önemsiz).</summary>
    public static bool SameCurve(IReadOnlyList<CurvePoint> a, IReadOnlyList<CurvePoint> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var left = a.OrderBy(p => p.Temp).ToList();
        var right = b.OrderBy(p => p.Temp).ToList();
        return left.Zip(right).All(pair => pair.First.Temp == pair.Second.Temp && pair.First.Percent == pair.Second.Percent);
    }

    /// <summary>Hazır eğrilerin görünen adı (arayüz diline göre).</summary>
    public static string PresetName(FanPreset preset) => Loc.T(preset switch
    {
        FanPreset.Quiet => "F.PresetQuiet",
        FanPreset.MaxCold => "F.PresetMax",
        _ => "F.PresetOptimal"
    });

    /// <summary>Eğrinin adı: hazır ayarla aynıysa onun adı, kayıtlı eğriyle aynıysa kaydedilen ad, değilse "Özel".</summary>
    public static string Describe(IReadOnlyList<CurvePoint> curve, IEnumerable<SavedCurve> saved)
    {
        foreach (var preset in Enum.GetValues<FanPreset>())
        {
            if (SameCurve(curve, Preset(preset).Curve))
            {
                return PresetName(preset);
            }
        }

        var match = saved.FirstOrDefault(s => SameCurve(curve, s.Curve));
        return match?.Name ?? Loc.T("F.Custom");
    }

    /// <summary>Kayıtlı bir eğriye verilemeyecek adlar (hazır ayar adları ve "Özel", iki dilde).</summary>
    public static bool IsReservedName(string name)
    {
        string[] reserved = ["Quiet", "Sessiz", "Optimal", "Max Cold", "Max Soğutma", "Custom", "Özel"];
        return reserved.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static CurvePoint P(int temp, int percent) => new() { Temp = temp, Percent = percent };

    /// <summary>Sıcaklığa karşılık gelen yüzde: noktalar arası doğrusal, uçların dışında en yakın noktanın değeri.</summary>
    public static double Interpolate(IReadOnlyList<CurvePoint> points, double temp)
    {
        if (points.Count == 0)
        {
            return 100; // eğri yoksa güvenli taraf
        }

        var sorted = points.OrderBy(p => p.Temp).ToList();
        if (temp <= sorted[0].Temp)
        {
            return sorted[0].Percent;
        }

        for (var i = 1; i < sorted.Count; i++)
        {
            var a = sorted[i - 1];
            var b = sorted[i];
            if (temp <= b.Temp)
            {
                if (b.Temp == a.Temp)
                {
                    return b.Percent;
                }

                return a.Percent + (b.Percent - a.Percent) * (temp - a.Temp) / (b.Temp - a.Temp);
            }
        }

        return sorted[^1].Percent;
    }

    /// <summary>Ayar dosyasından gelen bozuk/eksik değerleri düzeltir (sınırlar, sıralama, en az 2 nokta).</summary>
    public static void Sanitize(FanSetting setting)
    {
        setting.ManualPercent = Math.Clamp(setting.ManualPercent, 0, 100);
        setting.MinPercent = Math.Clamp(setting.MinPercent, 0, 100);

        var points = (setting.Curve ?? [])
            .Select(p => new CurvePoint { Temp = Math.Clamp(p.Temp, MinTemp, MaxTemp), Percent = Math.Clamp(p.Percent, 0, 100) })
            .GroupBy(p => p.Temp).Select(g => g.Last())
            .OrderBy(p => p.Temp)
            .Take(MaxPoints)
            .ToList();
        setting.Curve = points.Count >= 2 ? points : Default();
        MakeNonDecreasing(setting.Curve);
    }

    /// <summary>
    /// Sıcaklık artarken fan hızı düşmesin: her nokta bir önceki noktadan düşük olamaz.
    /// Eski veya elle düzenlenmiş ayar dosyalarındaki ters eğrileri düzeltir.
    /// </summary>
    public static void MakeNonDecreasing(List<CurvePoint> points)
    {
        var floor = 0;
        foreach (var point in points.OrderBy(p => p.Temp))
        {
            point.Percent = Math.Max(point.Percent, floor);
            floor = point.Percent;
        }
    }
}
