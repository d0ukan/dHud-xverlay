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

	/// <summary>PC'de tam ekran bir oyun açıkken oyunun FPS değeri; yoksa null.</summary>
	public float? Fps { get; set; }
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
