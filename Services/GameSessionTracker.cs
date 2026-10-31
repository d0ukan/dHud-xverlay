namespace DHud.Services;

/// <summary>Biten bir oyun oturumunun özeti. Sıcaklıklar okunamadıysa null.</summary>
public sealed record GameSummary(
    TimeSpan Duration, float AvgFps, float OnePercentLow, float MinFps, float MaxFps,
    float? MaxCpuTemp, float? MaxGpuTemp, float? AvgCpuTemp, float? AvgGpuTemp);

/// <summary>
/// FPS ölçümünden oyun oturumlarını algılar: FPS birkaç saniye süreyle ölçülürse oturum başlar,
/// bir süre ölçülmezse biter. Yeterince uzun oturumlar için özet üretir.
/// Her saniye <see cref="Sample"/> çağrılır (tek bir thread'den).
/// </summary>
public sealed class GameSessionTracker
{
    /// <summary>Oturum, FPS bu kadar ardışık saniye ölçülünce başlar (kısa parlamalar oyun sayılmaz).</summary>
    public const int StartAfterSeconds = 10;

    /// <summary>FPS bu kadar saniye ölçülmezse oturum biter (Alt+Tab ile kısa çıkışlar oturumu bölmesin).</summary>
    public const int EndAfterSeconds = 20;

    /// <summary>Bundan kısa oturumlar için özet gösterilmez.</summary>
    public const int MinSessionSeconds = 120;

    private readonly List<float> _fps = [];
    private readonly List<float> _cpuTemps = [];
    private readonly List<float> _gpuTemps = [];
    private int _withFps;
    private int _withoutFps;
    private bool _active;

    /// <summary>Oturum bitti ve yeterince uzundu. DİKKAT: <see cref="Sample"/>'ı çağıran thread'den tetiklenir.</summary>
    public event Action<GameSummary>? Finished;

    public bool IsActive => _active;

    public void Sample(float? fps, float? cpuTemp, float? gpuTemp)
    {
        if (fps is float value && value > 0)
        {
            _withoutFps = 0;
            _withFps++;
            if (!_active && _withFps >= StartAfterSeconds)
            {
                _active = true;
            }

            // Oturum başlamadan önceki ilk saniyeler de kayda geçer (başlangıç sayımı yalnızca bekleme içindir).
            _fps.Add(value);
            if (cpuTemp is float c && c > 0)
            {
                _cpuTemps.Add(c);
            }

            if (gpuTemp is float g && g > 0)
            {
                _gpuTemps.Add(g);
            }

            return;
        }

        if (!_active)
        {
            // Henüz oyun sayılmayan kısa FPS parlaması bitti: sayımı ve kaydı sıfırla.
            Reset();
            return;
        }

        _withoutFps++;
        if (_withoutFps < EndAfterSeconds)
        {
            return;
        }

        if (_active)
        {
            var summary = Summarize();
            if (summary is not null)
            {
                Finished?.Invoke(summary);
            }
        }

        Reset();
    }

    private GameSummary? Summarize()
    {
        // Süre: FPS ölçülen saniye sayısı (oturum içi kısa duraklamalar sayılmaz).
        if (_fps.Count < MinSessionSeconds)
        {
            return null;
        }

        var sorted = _fps.OrderBy(x => x).ToList();
        var lowCount = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.01));
        return new GameSummary(
            TimeSpan.FromSeconds(_fps.Count),
            _fps.Average(),
            sorted.Take(lowCount).Average(),
            sorted[0],
            sorted[^1],
            _cpuTemps.Count > 0 ? _cpuTemps.Max() : null,
            _gpuTemps.Count > 0 ? _gpuTemps.Max() : null,
            _cpuTemps.Count > 0 ? _cpuTemps.Average() : null,
            _gpuTemps.Count > 0 ? _gpuTemps.Average() : null);
    }

    private void Reset()
    {
        _fps.Clear();
        _cpuTemps.Clear();
        _gpuTemps.Clear();
        _withFps = 0;
        _withoutFps = 0;
        _active = false;
    }
}
