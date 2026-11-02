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
    private const byte TransparentAlpha = 0x70; // "Widget'lar saydam olsun" açıkken kart zemini opaklığı

    private Color _cardColor = Color.FromArgb(0xF2, 0x1A, 0x1B, 0x22);
    private IntPtr _hwnd;
    private bool _dragging;
    private readonly Grid _shell;

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
        // Kart + kenar/köşe tutamaçları (boyutlandırma). Tutamaçlar kartın etrafındaki saydam gölge payında ve kartın dış kenarındadır.
        var shell = new Grid();
        _shell = shell;
        shell.Children.Add(Card);
        Content = shell;
        if (Resizable)
        {
            AddGrips(shell);
            Card.SizeChanged += (_, _) => UpdateZoom();
        }

        SourceInitialized += OnSourceInitialized;
        // Önizleme (tünel) olayı: kaydırma alanı (ScrollViewer) gibi alt öğeler tıklamayı yutsa da kart yine sürüklenir.
        // Düğme, metin kutusu, tutamaç ve kaydırma çubukları IsInteractive ile zaten ayrılır.
        PreviewMouseLeftButtonDown += OnMouseDown;

        // İçerik büyüyünce (yeni hatırlatıcı, tur listesi, not boyutu) komşunun üstüne taşmasın.
        SizeChanged += (_, _) =>
        {
            if (IsLoaded && !_resizing)
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
        WidgetGrid.SetScale(App.Current.Settings.Current.WidgetScale);
        ApplyTheme();
        var alpha = (byte)Math.Clamp(Math.Round(_cardColor.A * Options.Opacity), 0, 255);
        Card.Background = new SolidColorBrush(Color.FromArgb(alpha, _cardColor.R, _cardColor.G, _cardColor.B));
        ApplySize();
        ApplyShellScale();
    }

    /// <summary>Otomatik boyutlu kart içeriğiyle birlikte küçülür; kullanıcı boyutunda kart ızgaranın (ölçekli) hücreleriyle zaten küçülür.</summary>
    private void ApplyShellScale()
    {
        var s = WidgetGrid.Scale;
        _shell.LayoutTransform = !_custom && s < 1 ? new ScaleTransform(s, s) : Transform.Identity;
    }

    // ---- Renk teması ----

    /// <summary>Genel widget teması bu pencereye uygulanır mı (notlar kendi renklerini kullanır).</summary>
    protected virtual bool UsesTheme => true;

    private ResourceDictionary? _lightResources;
    private bool _lightApplied;

    /// <summary>Kart zemini, vurgu rengi ve (açık temada) yazı/düğme stillerini pencerenin kaynaklarına uygular.</summary>
    private void ApplyTheme()
    {
        if (!UsesTheme)
        {
            return;
        }

        var theme = WidgetThemes.Get(App.Current.Settings.Current.WidgetTheme);
        _cardColor = App.Current.Settings.Current.WidgetTransparent
            ? Color.FromArgb(TransparentAlpha, theme.Card.R, theme.Card.G, theme.Card.B)
            : theme.Card;
        Resources["AccentColor"] = theme.Accent;
        Resources["AccentBrush"] = new SolidColorBrush(theme.Accent);

        if (theme.Light && !_lightApplied)
        {
            _lightResources ??= new ResourceDictionary { Source = new Uri("pack://application:,,,/WidgetLight.xaml", UriKind.Absolute) };
            Resources.MergedDictionaries.Add(_lightResources);
            _lightApplied = true;
        }
        else if (!theme.Light && _lightApplied && _lightResources is not null)
        {
            Resources.MergedDictionaries.Remove(_lightResources);
            _lightApplied = false;
        }
    }

    // ---- Boyutlandırma (ızgaraya oturan, içeriği orantılı büyüyen/küçülen) ----

    /// <summary>Kenarlardan/köşelerden çekilerek boyutlandırılabilir mi (notlar kendi tutamacını kullanır).</summary>
    protected virtual bool Resizable => true;

    /// <summary>
    /// true: içerik yalnızca genişliğe göre büyür/küçülür, fazla yükseklik daha çok satır gösterir (listeler);
    /// false: içerik kartın içine orantılı sığar (saat, hava durumu gibi görsel widget'lar).
    /// </summary>
    protected virtual bool ZoomByWidth => false;

    // Yazı ve grafikler normal boyutun altına küçülmez (okunmaz hale gelir): alt sınır içeriğin kendi boyutudur, yalnızca büyütülür.
    private const double MinZoom = 1.0;
    private const double MaxZoom = 3.0;
    private const double MinCardWidth = 96;

    /// <summary>Listelerde en küçük kart yüksekliği (başlık + birkaç satır; fazlası kaydırılır).</summary>
    protected virtual double MinListHeight => 120;

    /// <summary>
    /// true: içerik kendi kaydırmasını yönetir (ör. başlık sabit, yalnızca liste kayar); taban sınıf dıştan ikinci bir kaydırma
    /// alanı koymaz, içeriğe kartın yüksekliğini verir. Yalnızca <see cref="ZoomByWidth"/> ile birlikte anlamlıdır.
    /// </summary>
    protected virtual bool ContentScrolls => false;

    /// <summary>Kart kullanıcı boyutuna geçti / otomatik boyuta döndü.</summary>
    protected virtual void OnCustomSizeChanged(bool custom)
    {
    }

    private bool _resizing;
    private bool _custom;                       // kullanıcı boyutu veriyor (içerik sarmalanmış)
    private UIElement? _content;                // widget'ın kendi içeriği (Card.Child)
    private Border? _holder;                    // içerik doğal genişlikte burada durur
    private Size _natural;
    private readonly ScaleTransform _zoom = new(1, 1);

    private sealed record ResizeSession(Rect Card, Point Mouse, WidgetGrid.Spec Spec, List<Rect> Others, List<WidgetGrid.CellRect> Neighbors, WidgetGrid.Edges Edges, (int W, int H) Min)
    {
        public (int C, int R, int W, int H)? Last { get; set; }
    }

    private ResizeSession? _session;

    private static ControlTemplate InvisibleGrip()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent); // saydam ama fareyi yakalar
        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }

    private void AddGrips(Grid shell)
    {
        // (yatay, dikey, imleç, kenarlar, kalınlık/kenar). Kenarlar önce, köşeler üstte (köşe öncelikli).
        (HorizontalAlignment H, VerticalAlignment V, Cursor Cursor, WidgetGrid.Edges Edges)[] grips =
        [
            (HorizontalAlignment.Left, VerticalAlignment.Stretch, Cursors.SizeWE, WidgetGrid.Edges.Left),
            (HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, WidgetGrid.Edges.Right),
            (HorizontalAlignment.Stretch, VerticalAlignment.Top, Cursors.SizeNS, WidgetGrid.Edges.Top),
            (HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, WidgetGrid.Edges.Bottom),
            (HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE, WidgetGrid.Edges.Left | WidgetGrid.Edges.Top),
            (HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, WidgetGrid.Edges.Right | WidgetGrid.Edges.Bottom),
            (HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW, WidgetGrid.Edges.Right | WidgetGrid.Edges.Top),
            (HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW, WidgetGrid.Edges.Left | WidgetGrid.Edges.Bottom)
        ];

        const double thick = 10;   // kartın dış kenarından ~4 DIP içeri, ~6 DIP dışarı (gölge payı)
        const double corner = 18;
        foreach (var (h, v, cursor, edges) in grips)
        {
            var isCorner = h != HorizontalAlignment.Stretch && v != VerticalAlignment.Stretch;
            var grip = new Thumb
            {
                Template = InvisibleGrip(),
                Cursor = cursor,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Width = isCorner ? corner : h == HorizontalAlignment.Stretch ? double.NaN : thick,
                Height = isCorner ? corner : v == VerticalAlignment.Stretch ? double.NaN : thick,
                ToolTip = Loc.T("W.ResizeHint")
            };

            // Kenar tutamacı köşelerle çakışmasın: uçlarda köşe payı bırak; kartın dış kenarından biraz içeri girer.
            if (!isCorner)
            {
                grip.Margin = h == HorizontalAlignment.Stretch
                    ? new Thickness(corner, v == VerticalAlignment.Top ? 4 : 0, corner, v == VerticalAlignment.Bottom ? 4 : 0)
                    : new Thickness(h == HorizontalAlignment.Left ? 4 : 0, corner, h == HorizontalAlignment.Right ? 4 : 0, corner);
            }

            var captured = edges;
            grip.DragStarted += (_, e) => BeginResize(captured, grip, e);
            grip.DragDelta += (_, _) => ResizeStep();
            grip.DragCompleted += (_, _) => EndResize();
            grip.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    ResetSize();   // çift tık: otomatik boyuta dön
                    e.Handled = true;
                }
            };
            shell.Children.Add(grip);
        }
    }

    private Point ScreenDip()
    {
        var device = PointToScreen(Mouse.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(device) : device;
    }

    /// <summary>
    /// En küçük hücre boyutu. Görsel widget'larda içeriğin ölçeksiz boyutu (yazı/grafik küçülmesin); listelerde normal genişlik
    /// ve kısa bir yükseklik (fazlası kaydırılır).
    /// </summary>
    private (int W, int H) MinSpan(WidgetGrid.Spec g)
    {
        var s = WidgetGrid.Scale;
        var width = Math.Max(MinCardWidth, _natural.Width) * s;
        var height = ZoomByWidth ? MinListHeight * s : Math.Max(MinListHeight / 2, NaturalContentHeight() + 2 * Card.BorderThickness.Top) * s;
        return WidgetGrid.SpanOf(new Size(width, height), g);
    }

    /// <summary>İçeriğin ölçeksiz yüksekliği (taşıyıcı doğal genişlikte ölçülür).</summary>
    private double NaturalContentHeight()
    {
        if (_holder is null)
        {
            return 0;
        }

        _holder.Measure(new Size(_natural.Width, double.PositiveInfinity));
        return _holder.DesiredSize.Height;
    }

    /// <summary>İçerik sonradan uzadıysa (hava durumu yüklendi, satır eklendi) kart yazıyı küçültmek yerine büyür.</summary>
    private void EnsureFits()
    {
        if (!_custom || _resizing || ZoomByWidth || Options.SpanW <= 0)
        {
            return;
        }

        var spec = WidgetGrid.Make(CurrentWorkArea() ?? SystemParameters.WorkArea);
        var min = MinSpan(spec);
        if (Options.SpanW < min.W || Options.SpanH < min.H)
        {
            Options.SpanW = Math.Max(Options.SpanW, min.W);
            Options.SpanH = Math.Max(Options.SpanH, min.H);
            App.Current.Settings.Save();
            ApplySize();
            App.Current.WidgetManager?.Resolve(this);
        }
    }

    private void BeginResize(WidgetGrid.Edges edges, Thumb grip, DragStartedEventArgs e)
    {
        if (Options.LockPosition || CurrentWorkArea() is not Rect work)
        {
            grip.CancelDrag();
            return;
        }

        EnterCustomSize();
        _resizing = true;
        var spec = WidgetGrid.Make(work);
        var others = App.Current.WidgetManager?.OtherCards(this) ?? [];
        var neighbors = others.Select(o => WidgetGrid.CellsOf(o, spec)).ToList();
        _session = new ResizeSession(CardRect, ScreenDip(), spec, others, neighbors, edges, MinSpan(spec));
    }

    private void ResizeStep()
    {
        if (_session is not { } s)
        {
            return;
        }

        var d = ScreenDip() - s.Mouse;
        // Komşu kartların kenar çizgilerine yapışma; Alt tuşu basılıyken kapalı (ince ayar).
        var align = !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var (c, r, w, h) = WidgetGrid.Resize(s.Card, s.Edges, d, s.Min, s.Spec, align ? s.Neighbors : null, WidgetGrid.AlignTolerance);
        if (s.Last == (c, r, w, h))
        {
            return;
        }

        var rect = WidgetGrid.CellsRect(c, r, (w, h), s.Spec);
        if (s.Others.Any(o => WidgetManager.Overlaps(o, rect)))
        {
            return; // başka kartın üstüne binemez; son geçerli boyutta kalır
        }

        s.Last = (c, r, w, h);
        App.Current.WidgetManager?.ShowGuides(WidgetGrid.GuidesFor(new WidgetGrid.CellRect(c, r, w, h), s.Neighbors, s.Spec));
        Left = rect.Left - CardPad;
        Top = rect.Top - CardPad;
        Width = rect.Width + 2 * CardPad;
        Height = rect.Height + 2 * CardPad;
    }

    private void EndResize()
    {
        var session = _session;
        _session = null;
        _resizing = false;
        App.Current.WidgetManager?.HideGuides();
        if (session?.Last is { } last)
        {
            Options.SpanW = last.W;
            Options.SpanH = last.H;
            App.Current.Settings.Save();
            Moved?.Invoke(new Point(Left, Top));
        }

        App.Current.WidgetManager?.Resolve(this);
    }

    /// <summary>Kullanıcı boyutunu siler: kart yeniden içeriğin kendi boyutuna döner.</summary>
    private void ResetSize()
    {
        Options.SpanW = Options.SpanH = 0;
        App.Current.Settings.Save();
        ApplySize();
        App.Current.WidgetManager?.Resolve(this);
    }

    /// <summary>Ayardaki hücre boyutunu pencereye uygular (açılışta, profil/ayar değişince).</summary>
    private void ApplySize()
    {
        if (!Resizable || _resizing || (Card.Child is null && _content is null))
        {
            return;
        }

        if (Options.SpanW <= 0 || Options.SpanH <= 0)
        {
            LeaveCustomSize();
            return;
        }

        EnterCustomSize();
        var spec = WidgetGrid.Make(CurrentWorkArea() ?? SystemParameters.WorkArea);

        // Eskiden kaydedilmiş çok küçük boyutlar yeni alt sınıra yükselir.
        var min = MinSpan(spec);
        if (Options.SpanW < min.W || Options.SpanH < min.H)
        {
            Options.SpanW = Math.Max(Options.SpanW, min.W);
            Options.SpanH = Math.Max(Options.SpanH, min.H);
            App.Current.Settings.Save();
        }

        var span = (W: Math.Min(Options.SpanW, spec.Cols), H: Math.Min(Options.SpanH, spec.Rows));
        var size = WidgetGrid.SizeOf(span, spec);
        Width = size.Width + 2 * CardPad;
        Height = size.Height + 2 * CardPad;
    }

    /// <summary>İçeriği sarmalar (bir kez): doğal genişlikte bir taşıyıcıya koyar, kart ölçüleri pencereden gelir.</summary>
    private void EnterCustomSize()
    {
        if (_custom)
        {
            return;
        }

        _content = Card.Child;
        if (_content is null)
        {
            return;
        }

        // Doğal genişlik: widget'ın verdiği sabit kart genişliği; yoksa içeriğin istediği genişlik.
        var natural = Card.Width;
        if (double.IsNaN(natural))
        {
            Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            natural = Card.DesiredSize.Width - Card.Margin.Left - Card.Margin.Right;   // DesiredSize kenar boşluğunu da içerir
        }

        _natural = new Size(Math.Max(natural, MinCardWidth), Card.ActualHeight);
        Card.Child = null;
        _holder = new Border { Child = _content, Width = _natural.Width };
        if (ZoomByWidth)
        {
            _holder.LayoutTransform = _zoom;
            if (ContentScrolls)
            {
                _holder.VerticalAlignment = VerticalAlignment.Top;
                Card.Child = _holder;   // içerik kendi kaydırmasını yönetir; yüksekliği UpdateZoom verir
            }
            else
            {
                Card.Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _holder
                };
            }
        }
        else
        {
            Card.Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = _holder };
        }

        Card.Width = double.NaN;
        Card.Height = double.NaN;
        SizeToContent = SizeToContent.Manual;
        _custom = true;
        if (!ZoomByWidth)
        {
            _holder.SizeChanged += (_, _) => Dispatcher.BeginInvoke(EnsureFits, System.Windows.Threading.DispatcherPriority.Background);
        }

        ApplyShellScale();
        UpdateZoom();
        OnCustomSizeChanged(true);
    }

    private void LeaveCustomSize()
    {
        if (!_custom)
        {
            return;
        }

        _custom = false;
        Card.Child = null;
        if (_holder is not null)
        {
            _holder.Child = null;
            _holder.LayoutTransform = Transform.Identity;
        }

        Card.Child = _content;
        Card.Width = _natural.Width;
        _content = null;
        _holder = null;
        ClearValue(WidthProperty);
        ClearValue(HeightProperty);
        SizeToContent = SizeToContent.WidthAndHeight;
        ApplyShellScale();
        OnCustomSizeChanged(false);
    }

    private void UpdateZoom()
    {
        if (!_custom || !ZoomByWidth || _natural.Width <= 0)
        {
            return;
        }

        var s = Math.Clamp((Card.ActualWidth - 2 * Card.BorderThickness.Left) / _natural.Width, Math.Min(MinZoom, WidgetGrid.Scale), MaxZoom);
        _zoom.ScaleX = _zoom.ScaleY = s;
        if (ContentScrolls && _holder is not null)
        {
            _holder.Height = Math.Max(0, (Card.ActualHeight - 2 * Card.BorderThickness.Top) / s);   // içerik kartı doldurur
        }
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
    public Rect CardRect => new(Left + Pad, Top + Pad, Math.Max(0, ActualWidth - 2 * Pad), Math.Max(0, ActualHeight - 2 * Pad));

    /// <summary>Gölge payı: otomatik boyutta pencere içeriği ölçekle birlikte küçülür, kullanıcı boyutunda ölçeksizdir.</summary>
    private double Pad => _custom ? CardPad : CardPad * WidgetGrid.Scale;

    /// <summary>Kartın sol üst köşesini verilen noktaya getirir.</summary>
    public void SetCardTopLeft(Point p)
    {
        Left = p.X - Pad;
        Top = p.Y - Pad;
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
            const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
            if (!WantsTopmost)
            {
                // Arka plandaki bir uygulama HWND_TOP ile başka bir uygulamanın önüne geçemez (Windows engeller); geçici "üstte" sonra
                // "normal" yapmak ise çalışır ve pencereyi normal pencerelerin en üstüne bırakır.
                NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            }
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
        ShowDesktopGuard.Attach(this, _hwnd);   // Win+D widget'ları gizlemesin
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
        App.Current.WidgetManager?.Resolve(this, align: true); // bırakılan yerde başka widget/overlay varsa en yakın boş yere kayar; komşu kenarlara yapışır
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
            BorderThickness = new Thickness(1),
            MaxLength = maxLength
        };
        // Renkler temaya göre değişir (açık temada koyu yazı, koyu imleç).
        box.SetResourceReference(Control.BackgroundProperty, "FieldBackgroundBrush");
        box.SetResourceReference(Control.BorderBrushProperty, "FieldBorderBrush");
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "TextBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        return box;
    }

    /// <summary>Küçük bir simge düğmesi (✕, +, 📌 ...).</summary>
    protected Button IconButton(string glyph, string tooltip, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = glyph,
            ToolTip = tooltip
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "WidgetIconButton");   // dinamik: tema değişince stil de değişir
        button.Click += onClick;
        return button;
    }
}
