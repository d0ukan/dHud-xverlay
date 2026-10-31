using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DHud.Widgets;

/// <summary>
/// Tüm widget pencerelerinin ortak tabanı: çerçevesiz, yarı saydam koyu kart (overlay ve ayar pencereleriyle aynı tema),
/// sürükleyerek taşınır, konumu hatırlanır ve ekran dışına taşmaz.
/// </summary>
public abstract class WidgetWindowBase : Window
{
    /// <summary>Kartın kendisi; alt sınıflar içeriği buraya koyar.</summary>
    protected Border Card { get; }

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
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1A, 0x1B, 0x22)),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x40, 255, 255, 255), Color.FromArgb(0x14, 255, 255, 255), 90),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 }
        };
        Content = Card;

        MouseLeftButtonDown += OnMouseDown;
        LocationChanged += (_, _) => Moved?.Invoke(new Point(Left, Top));
    }

    /// <summary>Kayıtlı konumu uygular; geçersizse (ekran dışı / yok) verilen varsayılanı kullanır.</summary>
    public void Place(double left, double top, double defaultLeft, double defaultTop)
    {
        var valid = !double.IsNaN(left) && !double.IsNaN(top)
                    && left > SystemParameters.VirtualScreenLeft - 40 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60
                    && top > SystemParameters.VirtualScreenTop - 10 && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = valid ? left : defaultLeft;
        Top = valid ? top : defaultTop;
    }

    /// <summary>Varsayılan konum: çalışma alanının sağ üstünden aşağı doğru, sıra numarasına göre.</summary>
    protected static (double Left, double Top) DefaultSpot(int index, double width)
    {
        var wa = SystemParameters.WorkArea;
        return (wa.Right - width - 24 - (index % 3) * 28, wa.Top + 80 + index * 36 % 400);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || e.OriginalSource is not DependencyObject source || IsInteractive(source))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Fare zaten bırakılmış.
        }
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

    protected Brush Resource(string key) => (Brush)FindResource(key);
}
