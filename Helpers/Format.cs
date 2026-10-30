namespace DHud.Helpers;

/// <summary>Overlay ve tepsi için değer biçimlendirme. Kompakt modda birimler kısaltılır.</summary>
public static class Format
{
    public static string Temperature(float? value, bool compact = false) => value switch
    {
        float v => compact ? $"{v:0}°" : $"{v:0}°C",
        null => compact ? "--°" : "--°C"
    };

    public static string Load(float? value) => value is float v ? $"{v:0}%" : "--%";

    public static string Clock(float? mhz, bool compact) => mhz switch
    {
        float v when v >= 1000 => compact ? $"{v / 1000:0.0}G" : $"{v / 1000:0.0} GHz",
        float v => compact ? $"{v:0}M" : $"{v:0} MHz",
        null => "--"
    };

    public static string Power(float? watts, bool compact) => watts switch
    {
        float v => compact ? $"{v:0}W" : $"{v:0} W",
        null => "--W"
    };

    public static string Fan(float? rpm, bool compact) => rpm switch
    {
        float v => compact ? $"{v:0}" : $"{v:0} RPM",
        null => compact ? "--" : "-- RPM"
    };

    /// <summary>Ağ hızı; bayt/sn (KB/s, MB/s) veya bit/sn (kbps, Mbps).</summary>
    public static string Speed(double? bytesPerSecond, bool inBits, bool compact)
    {
        if (bytesPerSecond is not double bps)
        {
            return "--";
        }

        var value = inBits ? bps * 8 : bps;
        string[] units = inBits
            ? compact ? ["b", "k", "M", "G"] : ["bps", "kbps", "Mbps", "Gbps"]
            : compact ? ["B", "K", "M", "G"] : ["B/s", "KB/s", "MB/s", "GB/s"];

        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        var number = value < 10 && unit > 0 ? $"{value:0.0}" : $"{value:0}";
        return compact ? number + units[unit] : $"{number} {units[unit]}";
    }

    public static string Ping(long? milliseconds, bool compact) => milliseconds switch
    {
        long ms => compact ? $"{ms}ms" : $"{ms} ms",
        null => compact ? "--ms" : "-- ms"
    };

    public static string Memory(float? usedGb, float? totalGb, bool compact) => (usedGb, totalGb) switch
    {
        (float used, float total) when !compact => $"{used:0.0}/{total:0} GB",
        (float used, _) => compact ? $"{used:0.0}G" : $"{used:0.0} GB",
        _ => "--"
    };
}
