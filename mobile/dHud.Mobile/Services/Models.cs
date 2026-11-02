namespace dHud.Mobile.Services;

// PC'deki dHud'un gönderdiği JSON ile aynı alan adları (camelCase, bkz. Services/MobileLinkService.cs).

public sealed class DeviceReading
{
	public string? Name { get; set; }
	public float? Temperature { get; set; }
	public float? Load { get; set; }
	public float? ClockMhz { get; set; }
	public float? PowerWatts { get; set; }
	public float? FanRpm { get; set; }
	public float? MemoryUsedGb { get; set; }
	public float? MemoryTotalGb { get; set; }

	/// <summary>En yoğun CPU çekirdeğinin yükü (%) ve tüm çekirdeklerin ortalaması.</summary>
	public float? CoreMaxLoad { get; set; }
	public float? CoreAvgLoad { get; set; }
}

/// <summary>Sistem sürücüsünün doluluğu.</summary>
public sealed class DiskReading
{
	public string? Drive { get; set; }
	public float UsedPercent { get; set; }
	public float UsedGb { get; set; }
	public float TotalGb { get; set; }
}

public sealed class NetReading
{
	public string? AdapterName { get; set; }
	public double? DownloadBytesPerSecond { get; set; }
	public double? UploadBytesPerSecond { get; set; }
	public long? PingMs { get; set; }
}

/// <summary>PC'deki bir fan kanalı. Mode: auto / manual / curve.</summary>
public sealed class FanInfo
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public bool Gpu { get; set; }
	public bool Pump { get; set; }
	public float? Rpm { get; set; }
	public float? Pct { get; set; }
	public string Mode { get; set; } = "auto";

	/// <summary>Eğri modundaysa eğrinin adı (PC'nin dilinde).</summary>
	public string? Curve { get; set; }

	public int Manual { get; set; } = 50;
	public bool Stalled { get; set; }

	/// <summary>PC bu fanın telefondan değiştirilmesine izin veriyor mu.</summary>
	public bool Remote { get; set; } = true;
}

/// <summary>PC'de fan kontrolünün genel durumu.</summary>
public sealed class FanCtl
{
	/// <summary>PC'de fan kontrolü açık mı.</summary>
	public bool Enabled { get; set; }

	/// <summary>PC, telefondan fan değişikliğine izin veriyor mu.</summary>
	public bool Remote { get; set; }

	public bool Emergency { get; set; }

	/// <summary>PC'de kaydedilmiş eğrilerin adları.</summary>
	public List<string>? Curves { get; set; }

	/// <summary>PC'de kaydedilmiş tüm-fan profillerinin adları.</summary>
	public List<string>? Profiles { get; set; }
}

public sealed class PcSnapshot
{
	public List<FanInfo>? Fans { get; set; }
	public FanCtl? FanCtl { get; set; }

	public string? Pc { get; set; }
	public DeviceReading? Cpu { get; set; }
	public DeviceReading? Gpu { get; set; }
	public DeviceReading? Ram { get; set; }
	public NetReading? Net { get; set; }
	public DiskReading? Disk { get; set; }

	/// <summary>PC'de tam ekran bir oyun açıkken oyunun FPS değeri; yoksa null.</summary>
	public float? Fps { get; set; }
}

/// <summary>PC'deki yapılacaklar listesinin bir maddesi (alt görevler <see cref="Subs"/> içinde).</summary>
public sealed class TodoInfo
{
	public string Id { get; set; } = string.Empty;
	public string Text { get; set; } = string.Empty;
	public bool Done { get; set; }
	public List<TodoInfo>? Subs { get; set; }
}

/// <summary>PC'de kurulu bir hatırlatıcı. Due: UTC unix saniyesi.</summary>
public sealed class ReminderInfo
{
	public string Id { get; set; } = string.Empty;
	public string Text { get; set; } = string.Empty;
	public long Due { get; set; }
}

/// <summary>PC'nin gönderdiği görev özeti (<c>{"tasks":{...}}</c> mesajı). Enabled=false: PC telefondan görev yönetimine izin vermiyor.</summary>
public sealed class TasksState
{
	public bool Enabled { get; set; }
	public List<TodoInfo>? Todos { get; set; }
	public List<ReminderInfo>? Reminders { get; set; }
}

// ---- Widget'lar sekmesi (PC'nin {"widgets":{...}} mesajı, bkz. Services/MobileWidgetsFeed.cs) ----

/// <summary>PC'deki widget'ların özeti. Ctl=false: PC telefonun widget'ları kullanmasına izin vermiyor (diğer alanlar boş).</summary>
public sealed class WidgetsState
{
	public bool Ctl { get; set; }
	public MediaInfo? Media { get; set; }
	public ClipState? Clip { get; set; }
	public LaunchState? Launch { get; set; }
	public ProcState? Proc { get; set; }

	/// <summary>Borsa widget'ı PC'de kapalıysa null.</summary>
	public List<MarketInfo>? Market { get; set; }

	public WidgetBattery? Battery { get; set; }
}

/// <summary>Çalan parça. Cover (base64 JPEG) yalnızca değiştiğinde gelir; telefon <see cref="CoverKey"/> ile saklar.</summary>
public sealed class MediaInfo
{
	public string Title { get; set; } = string.Empty;
	public string Artist { get; set; } = string.Empty;
	public string App { get; set; } = string.Empty;
	public double Pos { get; set; }
	public double Len { get; set; }
	public bool Playing { get; set; }
	public bool Prev { get; set; }
	public bool Next { get; set; }
	public bool Play { get; set; }
	public string CoverKey { get; set; } = string.Empty;
	public string? Cover { get; set; }
}

/// <summary>PC'nin pano geçmişi. On=false: PC'de Pano widget'ı kapalı (geçmiş toplanmıyor). Images: yalnızca yeni görüntülerin önizlemesi (anahtar → base64 JPEG).</summary>
public sealed class ClipState
{
	public bool On { get; set; }
	public List<ClipItem>? Items { get; set; }
	public Dictionary<string, string>? Images { get; set; }
}

public sealed class ClipItem
{
	public string K { get; set; } = string.Empty;

	/// <summary>Metin kaydıysa metin (en çok 300 karakter); görüntü kaydında null.</summary>
	public string? T { get; set; }

	public int W { get; set; }
	public int H { get; set; }
}

/// <summary>PC'deki başlatıcı. Icons: yalnızca yeni/değişen öğelerin simgesi (kimlik → base64 PNG).</summary>
public sealed class LaunchState
{
	public List<LaunchItem>? Items { get; set; }
	public Dictionary<string, string>? Icons { get; set; }
}

public sealed class LaunchItem
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
}

/// <summary>Görev yöneticisi: en çok kaynak yiyen uygulamalar (her ölçütün ilk 6'sının birleşimi) ve sistem toplamları.</summary>
public sealed class ProcState
{
	public List<ProcInfo>? Rows { get; set; }

	/// <summary>Uygulama başına internet ölçülebiliyor mu (PC'de yönetici yetkisi gerekir).</summary>
	public bool Net { get; set; }

	/// <summary>Uygulama başına GPU kullanımı ölçülebiliyor mu.</summary>
	public bool GpuOn { get; set; }

	public float? CpuLoad { get; set; }
	public float? RamUsedGb { get; set; }
	public float? RamTotalGb { get; set; }
	public float? GpuLoad { get; set; }
	public float? VramUsedGb { get; set; }
	public float? VramTotalGb { get; set; }
}

/// <summary>Bir uygulamanın (aynı adlı işlemlerin toplamı) kaynak kullanımı. Ram: bayt; Disk ve Net: bayt/sn.</summary>
public sealed class ProcInfo
{
	public string N { get; set; } = string.Empty;
	public int C { get; set; }
	public double Cpu { get; set; }
	public long Ram { get; set; }
	public double Disk { get; set; }
	public double Net { get; set; }
	public double Gpu { get; set; }
}

public sealed class MarketInfo
{
	public string S { get; set; } = string.Empty;
	public double? Price { get; set; }
	public double? Chg { get; set; }
	public string? Ccy { get; set; }
	public List<double>? Closes { get; set; }
}

public sealed class WidgetBattery
{
	public PcBattery? Pc { get; set; }
	public List<DeviceBat>? Devices { get; set; }
}

public sealed class PcBattery
{
	public int Pct { get; set; }
	public bool Charging { get; set; }
	public int? Mins { get; set; }
}

public sealed class DeviceBat
{
	public string N { get; set; } = string.Empty;
	public int P { get; set; }
}

public enum LinkState
{
	Idle,
	Connecting,
	Connected,
	Offline,

	/// <summary>PC eşleştirme anahtarını reddetti (ör. PC'de "Eşleştirmeyi sıfırla" yapıldı).</summary>
	Rejected
}
