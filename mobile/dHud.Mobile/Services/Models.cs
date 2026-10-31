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

public sealed class PcSnapshot
{
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
