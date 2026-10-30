namespace DHud.Services;

/// <summary>
/// Sıcaklık belirli bir süre kritik eşiğin üstünde kalırsa bir kez uyarı üretir.
/// Sıcaklık eşiğin <see cref="HysteresisDegrees"/> derece altına inene kadar tekrar uyarmaz.
/// </summary>
public sealed class TemperatureAlert(string label, bool isGpu)
{
    private const int HysteresisDegrees = 3;

    private DateTime? _aboveSince;
    private bool _notified;

    /// <summary>Uyarı gösterilmesi gerekiyorsa mesajı, aksi halde null döner.</summary>
    public string? Check(float? temperature, AppSettings settings, DateTime now)
    {
        var hot = settings.Thresholds(isGpu).Hot;
        if (temperature is not float t || t < hot - HysteresisDegrees)
        {
            _aboveSince = null;
            _notified = false;
            return null;
        }

        if (t < hot)
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
        return Loc.T("N.HighTemp.Text", label, t, settings.AlertSeconds, hot);
    }
}
