using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DHud.Services;

/// <summary>Pano geçmişinde bir kayıt: ya metin ya da (sıkıştırılmış PNG olarak tutulan) görüntü.</summary>
public sealed class ClipEntry
{
    public string? Text { get; init; }

    /// <summary>Görüntü kaydında PNG baytları (ham piksel yerine: 4K ekran görüntüsü ~33 MB yerine birkaç MB tutar).</summary>
    public byte[]? Png { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Liste için küçük önizleme (dondurulmuş; her iş parçacığında kullanılabilir).</summary>
    public BitmapSource? Thumbnail { get; init; }

    public bool IsImage => Png is not null;

    /// <summary>Aynı içeriği ayırt eden anahtar: metnin kendisi veya PNG'nin özeti.</summary>
    public string Key { get; init; } = string.Empty;

    public long Bytes => Png?.LongLength ?? 0;

    public static ClipEntry FromText(string text) => new() { Text = text, Key = "t:" + text };
}

/// <summary>
/// Pano widget'ının geçmişi (yalnızca bellekte). En çok <see cref="MaxItems"/> kayıt; görüntüler ayrıca sınırlıdır:
/// en çok <see cref="MaxImages"/> tane ve toplam <see cref="MaxTotalImageBytes"/> bayt. Sınır aşılınca en eski kayıt atılır.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxItems = 10;
    public const int MaxImages = 5;
    public const long MaxImageBytes = 30L * 1024 * 1024;
    public const long MaxTotalImageBytes = 60L * 1024 * 1024;

    private readonly List<ClipEntry> _items = [];

    public IReadOnlyList<ClipEntry> Items => _items;

    public int Count => _items.Count;

    public void Clear() => _items.Clear();

    public void AddText(string text) => Add(ClipEntry.FromText(text));

    /// <summary>Görüntüyü en başa ekler; tek başına çok büyükse eklenmez (false).</summary>
    public bool AddImage(ClipEntry image)
    {
        if (!image.IsImage || image.Bytes > MaxImageBytes)
        {
            return false;
        }

        Add(image);
        return true;
    }

    private void Add(ClipEntry entry)
    {
        _items.RemoveAll(e => e.Key == entry.Key);   // aynı içerik tekrar kopyalandı: öne gelir
        _items.Insert(0, entry);

        // Görüntü sınırları: yenisi (başta) korunur, en eskiler atılır.
        while (_items.Count(e => e.IsImage) > MaxImages || _items.Where(e => e.IsImage).Sum(e => e.Bytes) > MaxTotalImageBytes)
        {
            var oldest = _items.FindLastIndex(e => e.IsImage);
            if (oldest <= 0)
            {
                break;
            }

            _items.RemoveAt(oldest);
        }

        while (_items.Count > MaxItems)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }
}

/// <summary>Panodan alınan görüntüyü saklanacak biçime çevirir (PNG + küçük önizleme). Arka plan iş parçacığından çağrılabilir.</summary>
public static class ClipImageCodec
{
    public const int ThumbnailHeight = 44;

    /// <summary>Kaynak dondurulmuş (Freeze) olmalıdır. Çözülemeyen veya boş görüntüde null.</summary>
    public static ClipEntry? FromBitmap(BitmapSource source)
    {
        try
        {
            if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
            {
                return null;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var png = stream.ToArray();

            var scale = Math.Min(1.0, ThumbnailHeight * 2.0 / source.PixelHeight);   // yüksek DPI için iki kat çözünürlük
            BitmapSource thumb = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
            if (thumb.CanFreeze)
            {
                thumb.Freeze();
            }

            return new ClipEntry
            {
                Png = png,
                Width = source.PixelWidth,
                Height = source.PixelHeight,
                Thumbnail = thumb,
                Key = "i:" + Convert.ToHexString(SHA256.HashData(png))
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Saklanan PNG'yi geri okur (panoya koymak için).</summary>
    public static BitmapFrame Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);   // OnLoad: akış hemen okunur
        frame.Freeze();
        return frame;
    }

    /// <summary>"1920×1080 · 1,2 MB" gibi kısa açıklama.</summary>
    public static string Describe(ClipEntry image)
    {
        var size = image.Bytes switch
        {
            >= 1024 * 1024 => $"{image.Bytes / 1048576.0:0.0} MB",
            _ => $"{Math.Max(1, image.Bytes / 1024)} KB"
        };
        return $"{image.Width}×{image.Height} · {size}";
    }
}
