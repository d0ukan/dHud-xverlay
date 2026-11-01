using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Panoya kopyalanan son metinleri listeler; birine tıklayınca tekrar panoya konur. Geçmiş YALNIZCA bellekte tutulur
/// (diske yazılmaz, widget kapalıyken toplanmaz) ve parola yöneticilerinin "izleme dışı" işaretli kopyaları yok sayılır.
/// </summary>
public sealed class ClipboardWidgetWindow : WidgetWindowBase
{
    private const int MaxChars = 2000;
    private const int ShownChars = 44;

    private static readonly ClipboardHistory History = new();

    private readonly StackPanel _list = new();
    private readonly TextBlock _status;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private IntPtr _hwnd;
    private bool _ignoreNext;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Clipboard);

    protected override bool ZoomByWidth => true;   // liste: genişlikle büyür, fazla yükseklik daha çok satır gösterir

    public ClipboardWidgetWindow()
    {
        Card.Width = 280;
        var clear = IconButton("🗑", Loc.T("W.ClipClear"), (_, _) => ClearHistory());
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.Clipboard"), WidgetIds.Clipboard, clear));
        _list.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_list);
        _status = Subtle("");
        _status.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_status);
        Card.Child = root;

        RestorePosition(WidgetIds.Clipboard, 4, 296);
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); _status.Text = ""; };
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(_hwnd);
            }
        };

        Rebuild();
        ApplyOptions();
    }

    /// <summary>Ekran görüntüsü üretimi için: geçmişi verilen metinlerle doldurur.</summary>
    internal static void SeedDemo(IEnumerable<string> items)
    {
        History.Clear();
        foreach (var item in items.Reverse())
        {
            History.AddText(item);   // ilk öğe en üstte kalsın
        }
    }

    /// <summary>Ekran görüntüsü üretimi için: geçmişe renk geçişli bir "ekran görüntüsü" ekler.</summary>
    internal static void AddDemoImage(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var x = i / 4 % width;
            var y = i / 4 / width;
            pixels[i] = (byte)(120 + x * 100 / width);
            pixels[i + 1] = (byte)(60 + y * 120 / height);
            pixels[i + 2] = (byte)(40 + x * 60 / width);
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        if (ClipImageCodec.FromBitmap(bitmap) is { } entry)
        {
            History.AddImage(entry);
        }
    }

    /// <summary>Ölçüm/test için: geçmişin şu anki kayıtları.</summary>
    internal static List<ClipEntry> Snapshot() => History.Items.ToList();

    /// <summary>Ölçüm/test için: bir kayda tıklanmış gibi panoya geri koyar.</summary>
    internal void CopyBackForTest(ClipEntry entry) => CopyBack(entry);

    /// <summary>Tüm geçmişi siler (ayarlardaki düğme de bunu kullanır).</summary>
    public static void ClearHistory()
    {
        History.Clear();
        foreach (var window in Application.Current.Windows.OfType<ClipboardWidgetWindow>())
        {
            window.Rebuild();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
        NativeMethods.AddClipboardFormatListener(_hwnd);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            // Pano o anda başka bir uygulamada meşgul olabilir; kısa bir gecikmeyle okunur.
            Dispatcher.BeginInvoke(() => Capture(retry: true), DispatcherPriority.Background);
        }

        return IntPtr.Zero;
    }

    private void Capture(bool retry)
    {
        if (_ignoreNext)
        {
            _ignoreNext = false;
            return;
        }

        string? text = null;
        BitmapSource? image = null;
        try
        {
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing"))
            {
                return;
            }

            // Metin varsa yalnızca metin alınır (Excel/Word gibi uygulamalar aynı kopyaya bir de resim ekler). Resim, yalnızca metin yokken.
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }

            if (string.IsNullOrWhiteSpace(text) && App.Current.Settings.Current.ClipboardImages && Clipboard.ContainsImage())
            {
                image = Clipboard.GetImage();
                if (image is { CanFreeze: true })
                {
                    image.Freeze();   // arka planda PNG'ye çevrilecek
                }
            }
        }
        catch (ExternalException)
        {
            if (retry)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (_, _) => { timer.Stop(); Capture(retry: false); };
                timer.Start();
            }

            return;
        }

        text = text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (text.Length > MaxChars)
            {
                text = text[..MaxChars];
            }

            History.AddText(text);
            Rebuild();
        }
        else if (image is { IsFrozen: true })
        {
            _ = AddImageAsync(image);
        }
    }

    /// <summary>Görüntüyü arka planda PNG'ye çevirir (4K ekran görüntüsü yüzlerce ms sürer; arayüz donmasın) ve geçmişe ekler.</summary>
    private async Task AddImageAsync(BitmapSource image)
    {
        var entry = await Task.Run(() => ClipImageCodec.FromBitmap(image));
        if (entry is null || !History.AddImage(entry))
        {
            ShowStatus(Loc.T("W.ClipImageSkipped"));
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<ClipboardWidgetWindow>())
        {
            window.Rebuild();
        }
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        if (History.Count == 0)
        {
            _list.Children.Add(Subtle(Loc.T("W.ClipEmpty")));
            return;
        }

        foreach (var item in History.Items.ToList())
        {
            var button = item.IsImage ? ImageButton(item) : TextButton(item.Text ?? string.Empty);
            button.Margin = new Thickness(0, 0, 0, 3);
            button.Padding = new Thickness(8, 4, 8, 4);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.SetResourceReference(StyleProperty, "DarkButton");
            var current = item;
            button.Click += (_, _) => CopyBack(current);
            _list.Children.Add(button);
        }
    }

    private static Button TextButton(string item)
    {
        var oneLine = string.Join(' ', item.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        var shown = oneLine.Length > ShownChars ? oneLine[..ShownChars] + "…" : oneLine;
        return new Button
        {
            Content = new TextBlock { Text = shown, TextTrimming = TextTrimming.CharacterEllipsis },
            ToolTip = item.Length > 300 ? item[..300] + "…" : item
        };
    }

    private static Button ImageButton(ClipEntry item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image
        {
            Source = item.Thumbnail,
            Height = ClipImageCodec.ThumbnailHeight,
            MaxWidth = 110,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var label = new TextBlock { Text = ClipImageCodec.Describe(item), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        row.Children.Add(label);
        return new Button { Content = row, ToolTip = Loc.T("W.ClipImageTip") };
    }

    private void CopyBack(ClipEntry entry)
    {
        try
        {
            _ignoreNext = true; // kendi yazdığımızı geçmişe tekrar eklemeyelim
            if (entry.Png is { } png)
            {
                // Hem PNG (saydamlığı korur) hem standart resim biçimi: hangi uygulamaya yapıştırılırsa yapıştırılsın çalışır.
                var data = new DataObject();
                data.SetImage(ClipImageCodec.Decode(png));
                data.SetData("PNG", new MemoryStream(png));
                Clipboard.SetDataObject(data, true);
            }
            else
            {
                Clipboard.SetText(entry.Text ?? string.Empty);
            }

            _status.Text = Loc.T("W.ClipCopied");
        }
        catch (ExternalException)
        {
            _ignoreNext = false;
            _status.Text = Loc.T("W.ClipBusy");
        }

        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
