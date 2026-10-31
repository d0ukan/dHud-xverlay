using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DHud.Services;

namespace DHud;

/// <summary>
/// Fan eğrisi editörü: yatay eksen sıcaklık (°C), dikey eksen fan hızı (%). Noktalar sürüklenir,
/// boş bir yere tıklayınca nokta eklenir, sağ tık noktayı siler. Her değişiklikte yeni bir liste üretilir
/// (okuyan arka plan thread'i yarım güncellenmiş liste görmesin).
/// </summary>
public sealed class CurveEditor : FrameworkElement
{
    private const double PadLeft = 44;
    private const double PadRight = 14;
    private const double PadTop = 12;
    private const double PadBottom = 28;
    private const double HitRadius = 11;
    private const double DotRadius = 6;

    private static readonly Brush Background = Frozen(0x12, 0x15, 0x1C);
    private static readonly Brush Subtle = Frozen(0x9C, 0xA3, 0xAF);
    private static readonly Brush Accent = Frozen(0x3B, 0x82, 0xF6);
    private static readonly Brush AccentFill = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x3B, 0x82, 0xF6)));
    private static readonly Brush Good = Frozen(0x4A, 0xDE, 0x80);
    private static readonly Pen GridPen = Freeze(new Pen(Frozen(0x2A, 0x2F, 0x3C), 1));
    private static readonly Pen LinePen = Freeze(new Pen(Accent, 2.5) { LineJoin = PenLineJoin.Round });
    private static readonly Pen DotPen = Freeze(new Pen(Frozen(0x0B, 0x0D, 0x12), 2));
    private static readonly Pen LivePen = Freeze(new Pen(Good, 1.5) { DashStyle = DashStyles.Dash });

    private List<CurvePoint> _points = FanCurve.Default();
    private int _drag = -1;
    private int _hover = -1;

    /// <summary>Her değişiklikte (sürüklerken sürekli) yeni nokta listesiyle tetiklenir.</summary>
    public event Action<List<CurvePoint>>? PointsChanged;

    /// <summary>Fare bırakıldığında veya nokta eklenip silindiğinde tetiklenir (kaydetme zamanı).</summary>
    public event Action? Committed;

    public IReadOnlyList<CurvePoint> Points
    {
        get => _points;
        set
        {
            _points = value.OrderBy(p => p.Temp).Select(Clone).ToList();
            InvalidateVisual();
        }
    }

    /// <summary>Kaynağın şu anki sıcaklığı ve fanın şu anki yüzdesi (yeşil çizgi/nokta olarak gösterilir).</summary>
    public (double? Temp, double? Percent) Live
    {
        set
        {
            _live = value;
            InvalidateVisual();
        }
    }

    private (double? Temp, double? Percent) _live;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 280 : availableSize.Height);

    // ---- Koordinatlar ----

    private double PlotWidth => Math.Max(1, ActualWidth - PadLeft - PadRight);
    private double PlotHeight => Math.Max(1, ActualHeight - PadTop - PadBottom);

    private double X(double temp) =>
        PadLeft + (temp - FanCurve.MinTemp) / (FanCurve.MaxTemp - FanCurve.MinTemp) * PlotWidth;

    private double Y(double percent) => PadTop + (1 - percent / 100) * PlotHeight;

    private double ToTemp(double x) =>
        FanCurve.MinTemp + (x - PadLeft) / PlotWidth * (FanCurve.MaxTemp - FanCurve.MinTemp);

    private double ToPercent(double y) => (1 - (y - PadTop) / PlotHeight) * 100;

    private Point PointOf(CurvePoint p) => new(X(p.Temp), Y(p.Percent));

    // ---- Çizim ----

    protected override void OnRender(DrawingContext dc)
    {
        var area = new Rect(PadLeft, PadTop, PlotWidth, PlotHeight);
        dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Kılavuz çizgileri ve etiketler.
        for (var temp = FanCurve.MinTemp; temp <= FanCurve.MaxTemp; temp += 10)
        {
            var x = X(temp);
            dc.DrawLine(GridPen, new Point(x, area.Top), new Point(x, area.Bottom));
            DrawText(dc, $"{temp}°", new Point(x, area.Bottom + 6), dpi, centered: true);
        }

        for (var percent = 0; percent <= 100; percent += 20)
        {
            var y = Y(percent);
            dc.DrawLine(GridPen, new Point(area.Left, y), new Point(area.Right, y));
            DrawText(dc, $"{percent}%", new Point(area.Left - 6, y - 7), dpi, rightAligned: true);
        }

        if (_points.Count >= 2)
        {
            // Eğrinin altı hafif dolgu; eğri ilk noktanın solunda ve son noktanın sağında yatay devam eder.
            var first = PointOf(_points[0]);
            var last = PointOf(_points[^1]);
            var figure = new PathFigure { StartPoint = new Point(area.Left, area.Bottom), IsClosed = true };
            figure.Segments.Add(new LineSegment(new Point(area.Left, first.Y), true));
            foreach (var p in _points)
            {
                figure.Segments.Add(new LineSegment(PointOf(p), true));
            }

            figure.Segments.Add(new LineSegment(new Point(area.Right, last.Y), true));
            figure.Segments.Add(new LineSegment(new Point(area.Right, area.Bottom), true));
            var fill = new PathGeometry([figure]);
            fill.Freeze();
            dc.DrawGeometry(AccentFill, null, fill);

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(new Point(area.Left, first.Y), false, false);
                foreach (var p in _points)
                {
                    ctx.LineTo(PointOf(p), true, true);
                }

                ctx.LineTo(new Point(area.Right, last.Y), true, true);
            }

            line.Freeze();
            dc.DrawGeometry(null, LinePen, line);
        }

        // Canlı sıcaklık çizgisi ve fanın şu anki yüzdesi.
        if (_live.Temp is double liveTemp && liveTemp is >= FanCurve.MinTemp and <= FanCurve.MaxTemp)
        {
            var x = X(liveTemp);
            dc.DrawLine(LivePen, new Point(x, area.Top), new Point(x, area.Bottom));
            if (_live.Percent is double livePercent)
            {
                dc.DrawEllipse(Good, DotPen, new Point(x, Y(Math.Clamp(livePercent, 0, 100))), 5, 5);
            }
        }

        // Noktalar.
        for (var i = 0; i < _points.Count; i++)
        {
            var center = PointOf(_points[i]);
            var radius = i == _drag || i == _hover ? DotRadius + 2 : DotRadius;
            dc.DrawEllipse(Accent, DotPen, center, radius, radius);
        }

        // Sürüklenen/üstüne gelinen noktanın değeri.
        var shown = _drag >= 0 ? _drag : _hover;
        if (shown >= 0 && shown < _points.Count)
        {
            var p = _points[shown];
            var c = PointOf(p);
            DrawText(dc, $"{p.Temp}°C → {p.Percent}%", new Point(Math.Clamp(c.X, area.Left + 40, area.Right - 40), Math.Max(c.Y - 26, area.Top)), dpi, centered: true, bright: true);
        }
    }

    private static void DrawText(DrawingContext dc, string text, Point at, double dpi,
        bool centered = false, bool rightAligned = false, bool bright = false)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, bright ? Brushes.White : Subtle, dpi);
        var x = centered ? at.X - formatted.Width / 2 : rightAligned ? at.X - formatted.Width : at.X;
        dc.DrawText(formatted, new Point(x, at.Y));
    }

    // ---- Fare ----

    private int HitTest(Point position)
    {
        for (var i = 0; i < _points.Count; i++)
        {
            if ((PointOf(_points[i]) - position).Length <= HitRadius)
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var position = e.GetPosition(this);
        var hit = HitTest(position);

        if (hit < 0)
        {
            var inside = position.X >= PadLeft && position.X <= PadLeft + PlotWidth
                         && position.Y >= PadTop && position.Y <= PadTop + PlotHeight;
            if (!inside || _points.Count >= FanCurve.MaxPoints)
            {
                return;
            }

            var addedTemp = (int)Math.Round(Math.Clamp(ToTemp(position.X), FanCurve.MinTemp, FanCurve.MaxTemp));
            var previous = _points.LastOrDefault(p => p.Temp < addedTemp);
            var next = _points.FirstOrDefault(p => p.Temp > addedTemp);
            var added = new CurvePoint
            {
                Temp = addedTemp,
                // Yeni nokta da komşularının arasında kalır (sıcaklıkla birlikte fan hızı düşmez).
                Percent = (int)Math.Round(Math.Clamp(ToPercent(position.Y), previous?.Percent ?? 0, next?.Percent ?? 100))
            };

            // Aynı sıcaklıkta zaten nokta varsa ekleme.
            if (_points.Any(p => p.Temp == added.Temp))
            {
                return;
            }

            Mutate(list => list.Add(added));
            hit = _points.FindIndex(p => p.Temp == added.Temp);
        }

        _drag = hit;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);

        if (_drag < 0)
        {
            var hover = HitTest(position);
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover >= 0 ? Cursors.Hand : Cursors.Arrow;
                InvalidateVisual();
            }

            return;
        }

        // Noktalar birbirini geçemez: sıcaklık komşu noktaların arasında kalır.
        var minTemp = _drag > 0 ? _points[_drag - 1].Temp + 1 : FanCurve.MinTemp;
        var maxTemp = _drag < _points.Count - 1 ? _points[_drag + 1].Temp - 1 : FanCurve.MaxTemp;
        var temp = (int)Math.Round(Math.Clamp(ToTemp(position.X), minTemp, maxTemp));
        // Sıcaklık artarken fan hızı düşmez: yüzde sol komşudan aşağı, sağ komşudan yukarı çıkamaz.
        var minPercent = _drag > 0 ? _points[_drag - 1].Percent : 0;
        var maxPercent = _drag < _points.Count - 1 ? _points[_drag + 1].Percent : 100;
        var percent = (int)Math.Round(Math.Clamp(ToPercent(position.Y), minPercent, maxPercent));

        var index = _drag;
        Mutate(list =>
        {
            list[index].Temp = temp;
            list[index].Percent = percent;
        });
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag >= 0)
        {
            _drag = -1;
            ReleaseMouseCapture();
            InvalidateVisual();
            Committed?.Invoke();
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit >= 0 && _points.Count > 2)
        {
            Mutate(list => list.RemoveAt(hit));
            _hover = -1;
            Committed?.Invoke();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag < 0 && _hover >= 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    /// <summary>Listeyi kopyalayıp değiştirir ve yeni listeyi yayınlar (değiştirilen liste hiç paylaşılmaz).</summary>
    private void Mutate(Action<List<CurvePoint>> edit)
    {
        var copy = _points.Select(Clone).ToList();
        edit(copy);
        _points = copy.OrderBy(p => p.Temp).ToList();
        InvalidateVisual();
        PointsChanged?.Invoke(_points.Select(Clone).ToList());
    }

    private static CurvePoint Clone(CurvePoint p) => new() { Temp = p.Temp, Percent = p.Percent };

    private static SolidColorBrush Frozen(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
