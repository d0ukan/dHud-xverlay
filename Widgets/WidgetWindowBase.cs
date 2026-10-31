using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DHud.Helpers;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Tüm widget pencerelerinin ortak tabanı: çerçevesiz, yarı saydam koyu kart (overlay ve ayar pencereleriyle aynı tema),
/// sürükleyerek taşınır, konumu hatırlanır, ekranın dışına çıkarılamaz ve isteğe bağlı kenara yapışır.
/// Görünürlük, hep üstte ve kilit ayarları <see cref="Options"/> üzerinden gelir.
/// </summary>
public abstract class WidgetWindowBase : Window
{
    private const double SnapDistance = 24;
    private const double CornerMargin = 16;
    private const double CardPad = 8; // kartın etrafındaki gölge payı

    private Color _cardColor = Color.FromArgb(0xF2, 0x1A, 0x1B, 0x22);
    private IntPtr _hwnd;
    private bool _dragging;

    /// <summary>Kartın kendisi; alt sınıflar içeriği buraya koyar.</summary>
    protected Border Card { get; }

    /// <summary>Bu widget'ın ayarları (Ayarlar → Widget'lar).</summary>
    protected abstract WidgetOptions Options { get; }

    /// <summary>Hep üstte durup durmayacağı. Notlar kendi iğne ayarını kullanır.</summary>
    protected virtual bool WantsTopmost => Options.AlwaysOnTop;

    /// <summary>true: widget bir ızgaraya oturur (üst üste binmez); false: serbest konum.</summary>
    public bool SnapToGrid => Options.SnapToEdges;

    /// <summary>Pencere taşındı (konum kaydedilmeli).</summary>
    public event Action<Point>? Moved;

    protected WidgetWindowBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(ForegroundProperty, "TextBrush");

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/WidgetStyles.xaml", UriKind.Absolute)
        });

        Card = new Border
        {
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(_cardColor),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x40, 255, 255, 255), Color.FromArgb(0x14, 255, 255, 255), 90),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 }
        };
        Content = Card;

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseDown;

        // İçerik büyüyünce (yeni hatırlatıcı, tur listesi, not boyutu) komşunun üstüne taşmasın.
        SizeChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                Dispatcher.BeginInvoke(() => App.Current.WidgetManager?.Resolve(this), System.Windows.Threading.DispatcherPriority.Background);
            }
        };
        LocationChanged += (_, _) =>
        {
            Moved?.Invoke(new Point(Left, Top));
            if (_dragging && SnapToGrid)
            {
                App.Current.WidgetManager?.Preview(this); // sürüklerken oturacağı hücreleri göster
            }
        };
    }

    // ---- Ayarlar ----

    /// <summary>Kartın zemin rengi; görünürlük ayarı bunun saydamlığına uygulanır.</summary>
    protected void SetCardColor(Color color)
    {
        _cardColor = color;
        ApplyOptions();
    }

    /// <summary>Görünürlük ve hep üstte ayarlarını uygular. Yazılar etkilenmez; yalnızca kartın zemini saydamlaşır.</summary>
    public void ApplyOptions()
    {
        Topmost = WantsTopmost;
        var alpha = (byte)Math.Clamp(Math.Round(_cardColor.A * Options.Opacity), 0, 255);
        Card.Background = new SolidColorBrush(Color.FromArgb(alpha, _cardColor.R, _cardColor.G, _cardColor.B));
    }

    // ---- Konum ----

    /// <summary>Kayıtlı konumu uygular; geçersizse (ekran dışı / yok) verilen varsayılanı kullanır.</summary>
    public void Place(double left, double top, double defaultLeft, double defaultTop)
    {
        var valid = !double.IsNaN(left) && !double.IsNaN(top)
                    && left > SystemParameters.VirtualScreenLeft - 40
                    && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60
                    && top > SystemParameters.VirtualScreenTop - 10
                    && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = valid ? left : defaultLeft;
        Top = valid ? top : defaultTop;
        Loaded += (_, _) =>
        {
            KeepInsideWorkArea();
            App.Current.WidgetManager?.Resolve(this);
        };
    }

    /// <summary>Varsayılan konum: çalışma alanının sağ üstünden aşağı doğru, sıra numarasına göre.</summary>
    protected static (double Left, double Top) DefaultSpot(int index, double width)
    {
        var wa = SystemParameters.WorkArea;
        return (wa.Right - width - 24 - (index % 3) * 28, wa.Top + 80 + index * 36 % 400);
    }

    /// <summary>Widget'ı geçerli monitörün bir köşesine/ortasına taşır. <paramref name="stack"/> aynı köşedeki sıra (üst üste binmesin).</summary>
    public void MoveToCorner(WidgetCorner corner, int stack = 0)
    {
        var work = CurrentWorkArea() ?? SystemParameters.WorkArea;
        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        var offset = stack * 28;

        Left = corner switch
        {
            WidgetCorner.TopLeft or WidgetCorner.BottomLeft => work.Left + CornerMargin + offset,
            WidgetCorner.TopRight or WidgetCorner.BottomRight => work.Right - w - CornerMargin - offset,
            _ => work.Left + (work.Width - w) / 2 + offset
        };
        Top = corner switch
        {
            WidgetCorner.TopLeft or WidgetCorner.TopRight => work.Top + CornerMargin + offset,
            WidgetCorner.BottomLeft or WidgetCorner.BottomRight => work.Bottom - h - CornerMargin - offset,
            _ => work.Top + (work.Height - h) / 2 + offset
        };
        KeepInsideWorkArea();
        App.Current.WidgetManager?.Resolve(this);
    }

    /// <summary>Kartın ekrandaki dikdörtgeni (gölge payı hariç); widget'ların birbirine binmemesi için kullanılır.</summary>
    public Rect CardRect => new(Left + CardPad, Top + CardPad, Math.Max(0, ActualWidth - 2 * CardPad), Math.Max(0, ActualHeight - 2 * CardPad));

    /// <summary>Kartın sol üst köşesini verilen noktaya getirir.</summary>
    public void SetCardTopLeft(Point p)
    {
        Left = p.X - CardPad;
        Top = p.Y - CardPad;
    }

    /// <summary>Pencerenin bulunduğu monitörün çalışma alanı (görev çubuğu hariç), DIP cinsinden.</summary>
    public Rect? WorkAreaRect() => CurrentWorkArea();

    private Rect? CurrentWorkArea()
    {
        if (_hwnd == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return null;
        }

        var monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var fromDevice = target.TransformFromDevice;
        return new Rect(
            fromDevice.Transform(new Point(info.rcWork.Left, info.rcWork.Top)),
            fromDevice.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom)));
    }

    /// <summary>Widget ekranın dışına taşmasın (boyut büyüyünce, monitör değişince, köşeye taşınınca).</summary>
    protected void KeepInsideWorkArea()
    {
        if (CurrentWorkArea() is not Rect work)
        {
            return;
        }

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        if (Left + w > work.Right) Left = Math.Max(work.Left, work.Right - w);
        if (Top + h > work.Bottom) Top = Math.Max(work.Top, work.Bottom - h);
        if (Left < work.Left) Left = work.Left;
        if (Top < work.Top) Top = work.Top;
    }

    /// <summary>Sürükleme bitti. true dönerse taban sınıf ek yerleştirme yapmaz.</summary>
    protected virtual bool OnDragCompleted(Point before) => false;

    /// <summary>Pencereyi odağı çalmadan z sırasında öne alır (not yığınında sekme sırası için).</summary>
    public void BringToTop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(_hwnd, WantsTopmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void SnapToEdges()
    {
        if (CurrentWorkArea() is not Rect work)
        {
            return;
        }

        // Kart, pencerenin 8 piksellik gölge payının içinde durur; yapışma kartın kenarına göre olur.
        const double pad = 8;
        if (Math.Abs(Left + pad - work.Left) < SnapDistance) Left = work.Left - pad;
        else if (Math.Abs(work.Right - (Left + ActualWidth - pad)) < SnapDistance) Left = work.Right - ActualWidth + pad;

        if (Math.Abs(Top + pad - work.Top) < SnapDistance) Top = work.Top - pad;
        else if (Math.Abs(work.Bottom - (Top + ActualHeight - pad)) < SnapDistance) Top = work.Bottom - ActualHeight + pad;
    }

    // ---- Pencere davranışı ----

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Araç penceresi: Alt+Tab listesinde görünmez (widget'lar masaüstü süsü).
        var style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
            (style | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    /// <summary>Sürüklerken (WM_MOVING) konumu en yakın monitörün çalışma alanında tutar: widget ekran dışına çıkarılamaz.</summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_MOVING)
        {
            return IntPtr.Zero;
        }

        var rect = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
        var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return IntPtr.Zero;
        }

        var work = info.rcWork;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        rect.Left = Math.Max(work.Left, Math.Min(rect.Left, work.Right - width));
        rect.Top = Math.Max(work.Top, Math.Min(rect.Top, work.Bottom - height));
        rect.Right = rect.Left + width;
        rect.Bottom = rect.Top + height;
        Marshal.StructureToPtr(rect, lParam, false);

        handled = true;
        return (IntPtr)1;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || Options.LockPosition
            || e.OriginalSource is not DependencyObject source || IsInteractive(source))
        {
            return;
        }

        var before = new Point(Left, Top);
        _dragging = true;
        try
        {
            DragMove(); // fare bırakılana kadar bloklar
        }
        catch (InvalidOperationException)
        {
            return; // fare zaten bırakılmış
        }
        finally
        {
            _dragging = false;
            App.Current.WidgetManager?.HidePreview();
        }

        if (OnDragCompleted(before))
        {
            return; // alt sınıf (ör. not yığını) yerleşimi kendisi yönetti
        }

        if (!SnapToGrid)
        {
            SnapToEdges(); // ızgara kapalıyken yalnızca ekran kenarına yapışma
        }

        KeepInsideWorkArea();
        App.Current.WidgetManager?.Resolve(this); // bırakılan yerde başka widget/overlay varsa en yakın boş yere kayar
        Moved?.Invoke(new Point(Left, Top));
    }

    private static bool IsInteractive(DependencyObject element)
    {
        for (var current = element; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is TextBoxBase or ButtonBase or Thumb or ScrollBar or ComboBox or Slider)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Genel widget'lar için: kayıtlı konumu (kimlikle) uygular ve taşındıkça kaydeder.</summary>
    protected void RestorePosition(string id, int index, double width)
    {
        var widgets = App.Current.Widgets;
        widgets.Data.Positions.TryGetValue(id, out var pos);
        var def = DefaultSpot(index, width);
        Place(pos?.Left ?? double.NaN, pos?.Top ?? double.NaN, def.Left, def.Top);
        Moved += p =>
        {
            widgets.Data.Positions[id] = new WidgetPos { Left = p.X, Top = p.Y };
            widgets.ScheduleSave();
        };
    }

    /// <summary>Başlık satırı: solda başlık, sağda (isteğe bağlı) ek düğmeler ve "gizle" düğmesi.</summary>
    protected Grid TitleBar(string title, string id, params UIElement[] extraButtons)
    {
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        var all = extraButtons.Append(IconButton("✕", Loc.T("W.Hide"), (_, _) =>
        {
            App.Current.Settings.Current.Extra(id).Enabled = false;
            App.Current.Settings.NotifyChanged();
        })).ToList();
        for (var i = 0; i < all.Count; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(all[i], i + 1);
            bar.Children.Add(all[i]);
        }

        return bar;
    }

    /// <summary>Başlık altı küçük bölüm başlığı / soluk metin.</summary>
    protected TextBlock Subtle(string text, double size = 12)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        return block;
    }

    /// <summary>Koyu temalı küçük düğme.</summary>
    protected Button SmallButton(string text, Action onClick, double minWidth = 0)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 3, 10, 3), MinWidth = minWidth };
        button.SetResourceReference(FrameworkElement.StyleProperty, "DarkButton");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Koyu temalı tek satır metin kutusu.</summary>
    protected TextBox Field(int maxLength = 100)
    {
        var box = new TextBox
        {
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CaretBrush = Brushes.White,
            MaxLength = maxLength
        };
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        return box;
    }

    /// <summary>Küçük bir simge düğmesi (✕, +, 📌 ...).</summary>
    protected Button IconButton(string glyph, string tooltip, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = glyph,
            ToolTip = tooltip,
            Style = (Style)FindResource("WidgetIconButton")
        };
        button.Click += onClick;
        return button;
    }
}
