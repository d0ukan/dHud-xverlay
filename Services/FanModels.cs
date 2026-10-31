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

public enum FanSource
{
    Cpu,
    Gpu
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
}

/// <summary>Eğri hesapları. Saf fonksiyonlar; donanıma dokunmaz.</summary>
public static class FanCurve
{
    public const int MinTemp = 20;
    public const int MaxTemp = 100;
    public const int MaxPoints = 8;

    public static List<CurvePoint> Default() =>
    [
        new() { Temp = 30, Percent = 25 },
        new() { Temp = 50, Percent = 35 },
        new() { Temp = 65, Percent = 55 },
        new() { Temp = 75, Percent = 80 },
        new() { Temp = 85, Percent = 100 }
    ];

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
    }
}
