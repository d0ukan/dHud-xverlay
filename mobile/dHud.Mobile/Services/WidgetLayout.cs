namespace dHud.Mobile.Services;

/// <summary>
/// Ana ekran widget'ının göstereceği son değerler (önbellekte JSON olarak saklanır). Sonradan eklenen alanlar isteğe bağlıdır:
/// eski sürümün önbelleği güncellemeden sonra da okunabilsin.
/// </summary>
public sealed record WidgetData(float? Cpu, float? Gpu, float? Fps, string Pc, long Timestamp,
	float? CpuLoad = null, float? GpuLoad = null, float? Ram = null, float? Disk = null);

/// <summary>Ana ekran widget'ının üç boyutlu düzeni.</summary>
public enum WidgetSize
{
	/// <summary>Tek satır: yalnızca CPU ve GPU sıcaklığı.</summary>
	Small,

	/// <summary>Sıcaklıklar ve FPS (eski düzen).</summary>
	Medium,

	/// <summary>Sıcaklık + yük yüzdeleri, FPS, RAM ve disk çubukları.</summary>
	Large
}

/// <summary>Widget'ın boyutuna göre düzen seçimi ve değer biçimleri (Android'e bağımlı değil; PC test projesinde de çalışır).</summary>
public static class WidgetLayout
{
	/// <summary>Başlatıcı her satır için ~70 dp verir: 1 satır küçük, 2 satır orta, 3+ satır (ve yeterince geniş) büyük.</summary>
	public static WidgetSize Choose(int minWidthDp, int minHeightDp) =>
		minHeightDp <= 0 ? WidgetSize.Medium
		: minHeightDp < 100 ? WidgetSize.Small
		: minHeightDp >= 180 && minWidthDp >= 200 ? WidgetSize.Large
		: WidgetSize.Medium;

	/// <summary>"74%" ya da değer yoksa "--%".</summary>
	public static string Percent(float? value) => value is >= 0 and <= 1000 ? $"{value:0}%" : "--%";

	/// <summary>Doluluk seviyesi: 0 normal, 1 dikkat (≥75), 2 kritik (≥90). Değer yoksa 0.</summary>
	public static int Level(float? percent) => percent switch
	{
		>= 90 => 2,
		>= 75 => 1,
		_ => 0
	};

	/// <summary>0–100 aralığına kırpılmış çubuk değeri.</summary>
	public static int Bar(float? value) => value is float v && float.IsFinite(v) ? (int)Math.Clamp(Math.Round(v), 0, 100) : 0;
}
