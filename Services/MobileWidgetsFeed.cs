using System.Globalization;
using System.IO;
using Forms = System.Windows.Forms;

namespace DHud.Services;

/// <summary>
/// Telefondaki "Widget'lar" sekmesinin verisi: medya, pano, başlatıcı, görev yöneticisi, borsa, pil. Yalnızca en az bir telefon
/// bu sekmeyi izlerken (<see cref="MobileLinkService"/> "watch") ve ayar açıkken çalışır; hiçbiri diske yazılmaz.
/// Büyük parçalar (kapak, pano önizlemeleri, simgeler) yalnızca değişince veya yeni bir telefon izlemeye başlayınca gönderilir.
/// ANA İŞ PARÇACIĞINDAN çağrılmalıdır (widget verisini okur).
/// </summary>
public sealed class MobileWidgetsFeed : IDisposable
{
    private const int ProcessRowsPerMetric = 6;

    private readonly WidgetService _widgets;
    private readonly SettingsService _settings;
    private readonly MediaBridge _media;
    private readonly ProcessMonitor _process = new();

    private readonly HashSet<string> _sentImages = [];
    private readonly HashSet<string> _sentIcons = [];
    private string _sentCover = string.Empty;

    private Dictionary<string, MarketQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _quotesAt = DateTime.MinValue;
    private List<DeviceBattery> _devices = [];
    private DateTime _devicesAt = DateTime.MinValue;
    private bool _marketBusy;
    private bool _watching;

    public MobileWidgetsFeed(WidgetService widgets, SettingsService settings, MediaBridge media)
    {
        _widgets = widgets;
        _settings = settings;
        _media = media;
    }

    /// <summary>Pano widget'ının geçmişi (yalnızca widget açıkken dolar); pencereye bağlı olmadan okunur.</summary>
    public Func<IReadOnlyList<ClipEntry>>? ClipboardProvider { get; set; }

    /// <summary>Son sensör ölçümü (sistem toplamları için); yoksa null.</summary>
    public Func<SensorSnapshot?>? SnapshotProvider { get; set; }

    /// <summary>Başlatıcı öğesinin simgesini PNG olarak verir (yoksa null).</summary>
    public Func<string, byte[]?>? IconProvider { get; set; }

    /// <summary>İzleme başladı/bitti. Başlarken tüm büyük parçalar yeniden gönderilir ve ağ ölçümü açılır; bitince kapatılır.</summary>
    public void SetWatching(bool watching)
    {
        if (_watching == watching)
        {
            return;
        }

        _watching = watching;
        _sentImages.Clear();
        _sentIcons.Clear();
        _sentCover = string.Empty;
        _devicesAt = DateTime.MinValue;
        if (watching)
        {
            _process.Reset();
            _process.StartNetwork();
        }
        else
        {
            _process.StopNetwork();
        }
    }

    /// <summary>Yeni bir telefon izlemeye başladı: büyük parçalar herkese yeniden gönderilsin.</summary>
    public void ForceFull()
    {
        _sentImages.Clear();
        _sentIcons.Clear();
        _sentCover = string.Empty;
    }

    public async Task<object> BuildAsync()
    {
        var s = _settings.Current;
        if (!s.MobileWidgets)
        {
            return new { ctl = false };
        }

        var mediaTask = _media.ReadAsync(includeCover: true);
        var procTask = Task.Run(_process.Sample);
        StartMarketRefresh();
        StartDeviceRefresh();

        var media = await mediaTask.ConfigureAwait(true);
        var procRows = await procTask.ConfigureAwait(true);

        return new
        {
            ctl = true,
            media = BuildMedia(media),
            clip = BuildClipboard(),
            launch = BuildLauncher(),
            proc = BuildProcesses(procRows),
            market = BuildMarket(),
            battery = BuildBattery()
        };
    }

    private object? BuildMedia(MediaState? m)
    {
        if (m is null)
        {
            return null;
        }

        string? cover = null;
        if (m.CoverJpeg is not null && m.CoverKey != _sentCover)
        {
            cover = Convert.ToBase64String(m.CoverJpeg);
            _sentCover = m.CoverKey;
        }

        return new
        {
            title = m.Title,
            artist = m.Artist,
            app = m.App,
            pos = Math.Round(m.PositionSeconds, 1),
            len = Math.Round(m.LengthSeconds, 1),
            playing = m.Playing,
            prev = m.CanPrevious,
            next = m.CanNext,
            play = m.CanPlayPause,
            coverKey = m.CoverKey,
            cover
        };
    }

    private object BuildClipboard()
    {
        var on = _settings.Current.Extra(WidgetIds.Clipboard).Enabled;
        var items = on ? ClipboardProvider?.Invoke() ?? [] : [];
        var list = new List<object>();
        var images = new Dictionary<string, string>();
        foreach (var item in items.Take(10))
        {
            if (item.IsImage)
            {
                list.Add(new { k = item.Key, w = item.Width, h = item.Height });
                if (_sentImages.Add(item.Key) && item.Thumbnail is { } thumb && ClipJpeg.Encode(thumb, 160) is { } jpeg)
                {
                    images[item.Key] = Convert.ToBase64String(jpeg);
                }
            }
            else
            {
                var text = item.Text ?? string.Empty;
                list.Add(new { k = item.Key.Length > 40 ? item.Key[..40] : item.Key, t = text.Length > 300 ? text[..300] : text });
            }
        }

        return new { on, items = list, images = images.Count == 0 ? null : images };
    }

    private object BuildLauncher()
    {
        var items = new List<object>();
        var icons = new Dictionary<string, string>();
        foreach (var item in _widgets.Data.Launchers)
        {
            items.Add(new { id = item.Id, name = item.Name });
            if (_sentIcons.Add(item.Id) && IconProvider?.Invoke(item.Path) is { } png)
            {
                icons[item.Id] = Convert.ToBase64String(png);
            }
        }

        return new { items, icons = icons.Count == 0 ? null : icons };
    }

    private object BuildProcesses(List<ProcRow> rows)
    {
        var picked = new Dictionary<string, ProcRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var sort in Enum.GetValues<ProcSort>().Where(s => s != ProcSort.Name))
        {
            foreach (var row in ProcessTable.Top(rows, sort, ProcessRowsPerMetric))
            {
                picked[row.Name] = row;
            }
        }

        var latest = SnapshotProvider?.Invoke();
        return new
        {
            rows = picked.Values.Select(r => new
            {
                n = r.Name,
                c = r.Count,
                cpu = Math.Round(r.CpuPercent, 1),
                ram = r.MemoryBytes,
                disk = Math.Round(r.DiskBytesPerSecond),
                net = Math.Round(r.NetBytesPerSecond),
                gpu = Math.Round(r.GpuPercent, 1)
            }).ToList(),
            net = _process.NetworkAvailable,
            gpuOn = _process.GpuAvailable,
            cpuLoad = latest?.Cpu?.Load,
            ramUsedGb = latest?.Memory?.MemoryUsedGb,
            ramTotalGb = latest?.Memory?.MemoryTotalGb,
            gpuLoad = latest?.Gpu?.Load,
            vramUsedGb = latest?.Gpu?.MemoryUsedGb,
            vramTotalGb = latest?.Gpu?.MemoryTotalGb
        };
    }

    /// <summary>Borsa: yalnızca PC'de Borsa widget'ı açıkken (internet kullanımı kullanıcının bilgisi dahilinde) ve dakikada bir.</summary>
    private void StartMarketRefresh()
    {
        if (!_settings.Current.Extra(WidgetIds.Market).Enabled || _marketBusy || DateTime.UtcNow - _quotesAt < TimeSpan.FromSeconds(60))
        {
            return;
        }

        _marketBusy = true;
        var symbols = (_widgets.Data.Markets ?? [.. MarketService.DefaultSymbols]).ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                var quotes = await MarketService.FetchAsync(symbols).ConfigureAwait(false);
                foreach (var (symbol, quote) in quotes)
                {
                    _quotes[symbol] = quote;
                }

                _quotesAt = DateTime.UtcNow;
            }
            finally
            {
                _marketBusy = false;
            }
        });
    }

    private object? BuildMarket()
    {
        if (!_settings.Current.Extra(WidgetIds.Market).Enabled)
        {
            return null;
        }

        var symbols = _widgets.Data.Markets ?? [.. MarketService.DefaultSymbols];
        return symbols.Select(symbol =>
        {
            _quotes.TryGetValue(symbol, out var q);
            return new
            {
                s = symbol,
                price = q?.Price,
                chg = q?.ChangePercent,
                ccy = q?.Currency,
                closes = q is null ? null : Thin(q.Closes, 30)
            };
        }).ToList();
    }

    private static List<double> Thin(IReadOnlyList<double> values, int max)
    {
        if (values.Count <= max)
        {
            return [.. values.Select(v => Math.Round(v, 4))];
        }

        return [.. Enumerable.Range(0, max).Select(i => Math.Round(values[i * (values.Count - 1) / (max - 1)], 4))];
    }

    private void StartDeviceRefresh()
    {
        if (DateTime.UtcNow - _devicesAt < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _devicesAt = DateTime.UtcNow;
        _ = Task.Run(() => _devices = DeviceBatteryService.Read());
    }

    private object BuildBattery()
    {
        object? pc = null;
        try
        {
            var power = Forms.SystemInformation.PowerStatus;
            if (!power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery) && power.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown)
            {
                pc = new
                {
                    pct = Math.Clamp((int)Math.Round(power.BatteryLifePercent * 100), 0, 100),
                    charging = power.PowerLineStatus == Forms.PowerLineStatus.Online,
                    mins = power.BatteryLifeRemaining > 0 ? power.BatteryLifeRemaining / 60 : (int?)null
                };
            }
        }
        catch (Exception)
        {
            // Güç durumu o an okunamadı: pil yok sayılır.
        }

        return new { pc, devices = _devices.Select(d => new { n = d.Name, p = d.Percent }).ToList() };
    }

    public void Dispose() => _process.Dispose();
}
