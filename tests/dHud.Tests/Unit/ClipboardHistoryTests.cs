using System.Windows.Media;
using System.Windows.Media.Imaging;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>Pano geçmişi: metin ve görüntü kayıtları, sınırlar, aynı içeriğin öne gelmesi, PNG gidiş-dönüş.</summary>
[Trait(Cat.Name, Cat.Fast)]
public class ClipboardHistoryTests
{
    private static ClipEntry Image(string key, long bytes) => new() { Png = new byte[bytes], Width = 10, Height = 10, Key = "i:" + key };

    private static BitmapSource Bitmap(int width, int height, byte seed = 0)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(i / 4 % 256);
            pixels[i + 1] = seed;
            pixels[i + 2] = (byte)(i / 4 / width % 256);
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void Newest_is_first_and_a_repeated_text_moves_to_the_front()
    {
        var history = new ClipboardHistory();
        history.AddText("a");
        history.AddText("b");
        history.AddText("a");

        Assert.Equal(["a", "b"], history.Items.Select(e => e.Text));
    }

    [Fact]
    public void Texts_and_images_share_the_ten_item_limit()
    {
        var history = new ClipboardHistory();
        for (var i = 0; i < 8; i++)
        {
            history.AddText($"t{i}");
        }

        history.AddImage(Image("x", 100));
        history.AddImage(Image("y", 100));
        history.AddImage(Image("z", 100));

        Assert.Equal(ClipboardHistory.MaxItems, history.Count);
        Assert.True(history.Items[0].IsImage);      // en yeni başta
        Assert.DoesNotContain(history.Items, e => e.Text == "t0");   // en eski metin gitti
    }

    [Fact]
    public void At_most_five_images_are_kept_and_the_oldest_goes_first()
    {
        var history = new ClipboardHistory();
        for (var i = 0; i < 7; i++)
        {
            history.AddImage(Image($"img{i}", 1000));
        }

        var images = history.Items.Where(e => e.IsImage).Select(e => e.Key).ToList();
        Assert.Equal(ClipboardHistory.MaxImages, images.Count);
        Assert.Equal(["i:img6", "i:img5", "i:img4", "i:img3", "i:img2"], images);
    }

    [Fact]
    public void Total_image_size_is_capped_but_the_newest_image_always_stays()
    {
        var history = new ClipboardHistory();
        var big = ClipboardHistory.MaxImageBytes;   // 30 MB
        history.AddImage(Image("a", big));
        history.AddImage(Image("b", big));
        history.AddImage(Image("c", big));          // toplam 90 MB > 60 MB: en eskiler atılır

        Assert.Equal(["i:c", "i:b"], history.Items.Select(e => e.Key));
        Assert.True(history.Items.Sum(e => e.Bytes) <= ClipboardHistory.MaxTotalImageBytes);
    }

    [Fact]
    public void An_image_that_is_too_big_on_its_own_is_refused_and_text_entries_are_untouched()
    {
        var history = new ClipboardHistory();
        history.AddText("keep");

        Assert.False(history.AddImage(Image("huge", ClipboardHistory.MaxImageBytes + 1)));
        Assert.False(history.AddImage(ClipEntry.FromText("not an image")));
        Assert.Equal(["keep"], history.Items.Select(e => e.Text));
    }

    [Fact]
    public void Clearing_removes_everything()
    {
        var history = new ClipboardHistory();
        history.AddText("a");
        history.AddImage(Image("x", 10));
        history.Clear();
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void A_bitmap_is_stored_as_png_with_a_small_thumbnail_and_decodes_back_identically() => Sta.Run(() =>
    {
        var source = Bitmap(400, 300, seed: 7);
        var entry = ClipImageCodec.FromBitmap(source);

        Assert.NotNull(entry);
        Assert.True(entry.IsImage);
        Assert.Equal((400, 300), (entry.Width, entry.Height));
        Assert.Equal(137, entry.Png![0] & 0xFF);       // PNG imzası: 0x89 'P' 'N' 'G'
        Assert.Equal((byte)'P', entry.Png[1]);
        Assert.True(entry.Png.Length < 400 * 300 * 4, "ham pikselden küçük olmalı");
        Assert.NotNull(entry.Thumbnail);
        Assert.True(entry.Thumbnail!.IsFrozen);
        Assert.InRange(entry.Thumbnail.PixelHeight, 80, 90);   // 44 DIP × 2

        var back = ClipImageCodec.Decode(entry.Png);
        Assert.Equal((400, 300), (back.PixelWidth, back.PixelHeight));
        var original = new byte[400 * 300 * 4];
        var decoded = new byte[400 * 300 * 4];
        source.CopyPixels(original, 400 * 4, 0);
        new FormatConvertedBitmap(back, PixelFormats.Bgra32, null, 0).CopyPixels(decoded, 400 * 4, 0);
        Assert.Equal(original, decoded);   // kayıpsız
    });

    [Fact]
    public void The_same_picture_has_the_same_key_and_moves_to_the_front_instead_of_duplicating() => Sta.Run(() =>
    {
        var a1 = ClipImageCodec.FromBitmap(Bitmap(120, 90, 1))!;
        var a2 = ClipImageCodec.FromBitmap(Bitmap(120, 90, 1))!;
        var b = ClipImageCodec.FromBitmap(Bitmap(120, 90, 2))!;
        Assert.Equal(a1.Key, a2.Key);
        Assert.NotEqual(a1.Key, b.Key);

        var history = new ClipboardHistory();
        history.AddImage(a1);
        history.AddImage(b);
        history.AddImage(a2);
        Assert.Equal(2, history.Count);
        Assert.Equal(a1.Key, history.Items[0].Key);
    });

    [Fact]
    public void Tiny_and_empty_bitmaps_do_not_crash_the_encoder() => Sta.Run(() =>
    {
        var pixel = ClipImageCodec.FromBitmap(Bitmap(1, 1));
        Assert.NotNull(pixel);
        Assert.Equal((1, 1), (pixel.Width, pixel.Height));
    });

    [Fact]
    public void Descriptions_show_size_in_pixels_and_kilo_or_megabytes()
    {
        Assert.Contains("1920×1080", ClipImageCodec.Describe(new ClipEntry { Png = new byte[500], Width = 1920, Height = 1080 }));
        Assert.Contains("KB", ClipImageCodec.Describe(new ClipEntry { Png = new byte[500], Width = 1, Height = 1 }));
        Assert.Contains("MB", ClipImageCodec.Describe(new ClipEntry { Png = new byte[3 * 1024 * 1024], Width = 1, Height = 1 }));
    }
}
