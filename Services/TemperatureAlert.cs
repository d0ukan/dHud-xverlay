namespace CpuGpuOverlay.Services;

/// <summary>
/// Sıcaklık belirli bir süre kritik eşiğin üstünde kalırsa bir kez uyarı üretir.
/// Sıcaklık eşiğin <see cref="HysteresisDegrees"/> derece altına inene kadar tekrar uyarmaz.
/// </summary>
public sealed class TemperatureAlert(string label)
{
    private const int HysteresisDegrees = 3;

    private DateTime? _aboveSince;
    private bool _notified;

    /// <summary>Uyarı gösterilmesi gerekiyorsa mesajı, aksi halde null döner.</summary>
    public string? Check(float? temperature, AppSettings settings, DateTime now)
    {
        if (temperature is not float t || t < settings.HotTemperature - HysteresisDegrees)
        {
            _aboveSince = null;
            _notified = false;
            return null;
        }

        if (t < settings.HotTemperature)
        {
            // Eşiğin hemen altındaki bantta süre sayılmaz ama uyarı da sıfırlanmaz (titreşimi önler).
            _aboveSince = null;
            return null;
        }

        _aboveSince ??= now;

        if (_notified || !settings.AlertEnabled || now - _aboveSince < TimeSpan.FromSeconds(settings.AlertSeconds))
        {
            return null;
        }

        _notified = true;
        return $"{label} sıcaklığı {t:0}°C — {settings.AlertSeconds} sn'dir kritik eşiğin ({settings.HotTemperature}°C) üzerinde.";
    }
}
