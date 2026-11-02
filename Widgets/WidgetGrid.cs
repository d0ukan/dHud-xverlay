using System.Windows;

namespace DHud.Widgets;

/// <summary>
/// Widget'ların oturduğu çok ince ızgara (macOS masaüstü widget'ları gibi). Hücre 6–8 DIP'tir (ekran genişliğine göre), böylece komşu
/// kartlar arasında yalnızca ~6–14 DIP boşluk kalır: ızgara kaba olsaydı her komşu, hücre kadar fazladan boşlukla otururdu.
/// Bir kart, kapladığı hücrelere göre yerleşir ve başka bir widget'ın (veya bar'ın) hücrelerine girmez. Saf hesaplardır; pencerelere dokunmaz.
/// </summary>
public static class WidgetGrid
{
    /// <summary>Kart ile hücre kenarı arasındaki boşluk (iki komşu kart arasında en az 2×Inset boşluk kalır).</summary>
    public const double Inset = 3;

    /// <summary>Hücre boyutu (DIP): ekran genişliği / <see cref="Divisor"/>, bu sınırlar arasında.</summary>
    public const double MinCell = 6;
    public const double MaxCell = 8;
    private const double Divisor = 240;

    public sealed record Spec(Rect Work, double Cell, int Cols, int Rows, double OffsetX, double OffsetY);

    /// <summary>Dolu hücre haritası. Boşluk sorgusu, önceden hesaplanmış toplamlarla (summed-area table) tek adımda yanıtlanır.</summary>
    public sealed class Occupancy
    {
        private readonly bool[,] _cells;
        private readonly int[,] _sums;
        private readonly int _cols;
        private readonly int _rows;

        public Occupancy(int cols, int rows, IEnumerable<(int C, int R)> cells)
        {
            _cols = cols;
            _rows = rows;
            _cells = new bool[cols, rows];
            foreach (var (c, r) in cells)
            {
                if (c >= 0 && c < cols && r >= 0 && r < rows)
                {
                    _cells[c, r] = true;
                }
            }

            _sums = new int[cols + 1, rows + 1];
            for (var c = 0; c < cols; c++)
            {
                for (var r = 0; r < rows; r++)
                {
                    _sums[c + 1, r + 1] = _sums[c, r + 1] + _sums[c + 1, r] - _sums[c, r] + (_cells[c, r] ? 1 : 0);
                }
            }
        }

        public bool Contains(int c, int r) => c >= 0 && c < _cols && r >= 0 && r < _rows && _cells[c, r];

        public int Count => _sums[_cols, _rows];

        /// <summary>(c, r) köşeli, w×h hücrelik dikdörtgenin tamamı boş mu.</summary>
        public bool IsFree(int c, int r, int w, int h) =>
            c >= 0 && r >= 0 && c + w <= _cols && r + h <= _rows
            && _sums[c + w, r + h] - _sums[c, r + h] - _sums[c + w, r] + _sums[c, r] == 0;

        public bool Overlaps(Occupancy other)
        {
            for (var c = 0; c < Math.Min(_cols, other._cols); c++)
            {
                for (var r = 0; r < Math.Min(_rows, other._rows); r++)
                {
                    if (_cells[c, r] && other._cells[c, r])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Genel widget ölçeği (Ayarlar → Widget'lar). Hücreler de ölçeklenir, böylece hücre cinsinden kayıtlı boyutlar orantılı küçülür.</summary>
    public const double MinScale = 0.5;
    public static double Scale { get; private set; } = 1;

    public static void SetScale(double scale) =>
        Scale = double.IsNaN(scale) || double.IsInfinity(scale) ? 1 : Math.Clamp(scale, MinScale, 1);

    public static Spec Make(Rect work)
    {
        var cell = Math.Clamp(Math.Round(work.Width / Divisor), MinCell, MaxCell);
        if (Scale < 1)
        {
            cell = Math.Max(3, Math.Round(cell * Scale * 2) / 2);
        }

        var cols = Math.Max(1, (int)Math.Floor(work.Width / cell));
        var rows = Math.Max(1, (int)Math.Floor(work.Height / cell));
        return new Spec(work, cell, cols, rows, (work.Width - cols * cell) / 2, (work.Height - rows * cell) / 2);
    }

    /// <summary>Kartın kapladığı hücre sayısı (genişlik, yükseklik); ızgaradan büyük olamaz.</summary>
    public static (int W, int H) SpanOf(Size card, Spec g) => (
        Math.Clamp((int)Math.Ceiling((card.Width + 2 * Inset) / g.Cell), 1, g.Cols),
        Math.Clamp((int)Math.Ceiling((card.Height + 2 * Inset) / g.Cell), 1, g.Rows));

    /// <summary>Kartın sol üst köşesine en yakın hücre (ızgara sınırları içinde).</summary>
    public static (int C, int R) NearestCell(Point cardTopLeft, (int W, int H) span, Spec g)
    {
        var c = (int)Math.Round((cardTopLeft.X - Inset - g.Work.Left - g.OffsetX) / g.Cell);
        var r = (int)Math.Round((cardTopLeft.Y - Inset - g.Work.Top - g.OffsetY) / g.Cell);
        return (Math.Clamp(c, 0, g.Cols - span.W), Math.Clamp(r, 0, g.Rows - span.H));
    }

    /// <summary>Hücrenin kart sol üst köşesi (piksel).</summary>
    public static Point CardOrigin(int c, int r, Spec g) =>
        new(g.Work.Left + g.OffsetX + c * g.Cell + Inset, g.Work.Top + g.OffsetY + r * g.Cell + Inset);

    /// <summary>Bir hücre kümesinin ekrandaki dikdörtgeni (kartın kapladığı alan).</summary>
    public static Rect CellsRect(int c, int r, (int W, int H) span, Spec g) =>
        new(g.Work.Left + g.OffsetX + c * g.Cell + Inset, g.Work.Top + g.OffsetY + r * g.Cell + Inset,
            span.W * g.Cell - 2 * Inset, span.H * g.Cell - 2 * Inset);

    [Flags]
    public enum Edges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    // ---- Komşu kartlarla hizalama (snapping) ----

    /// <summary>Bir kenar, komşunun kenar çizgisine bu kadar hücre yaklaşınca ona yapışır (8 px'lik hücrede ≈ 24 px).</summary>
    public const int AlignTolerance = 3;

    /// <summary>Hücre cinsinden dikdörtgen: sol üst hücre ve kapladığı hücre sayısı.</summary>
    public sealed record CellRect(int C, int R, int W, int H)
    {
        public int Right => C + W;
        public int Bottom => R + H;
    }

    /// <summary>Hizayı gösteren çizgi (piksel). Dikeyse <c>Pos</c> x'tir ve <c>From..To</c> y aralığıdır; yataysa tersi.</summary>
    public sealed record Guide(bool Vertical, double Pos, double From, double To);

    private static double LineX(int col, Spec g) => g.Work.Left + g.OffsetX + col * g.Cell;

    private static double LineY(int row, Spec g) => g.Work.Top + g.OffsetY + row * g.Cell;

    /// <summary>
    /// Bir kartın piksel dikdörtgenine en yakın hücre çizgileri (ızgara sınırlarına kırpılır). Izgaraya oturmuş kartlarda tam, otomatik
    /// boyutlu kartlarda en fazla yarım hücre (≈4 px) sapmayla; hizalama için kullanılır (doluluk için <see cref="Occupied"/>).
    /// </summary>
    public static CellRect CellsOf(Rect rect, Spec g)
    {
        var c0 = (int)Math.Round((rect.Left - Inset - g.Work.Left - g.OffsetX) / g.Cell);
        var c1 = (int)Math.Round((rect.Right + Inset - g.Work.Left - g.OffsetX) / g.Cell);
        var r0 = (int)Math.Round((rect.Top - Inset - g.Work.Top - g.OffsetY) / g.Cell);
        var r1 = (int)Math.Round((rect.Bottom + Inset - g.Work.Top - g.OffsetY) / g.Cell);
        c0 = Math.Clamp(c0, 0, g.Cols - 1);
        r0 = Math.Clamp(r0, 0, g.Rows - 1);
        c1 = Math.Clamp(c1, c0 + 1, g.Cols);
        r1 = Math.Clamp(r1, r0 + 1, g.Rows);
        return new CellRect(c0, r0, c1 - c0, r1 - r0);
    }

    /// <summary>
    /// <paramref name="edge"/> çizgisini komşuların kenar çizgilerine yaklaştırır. Aynı taraf (sol-sol, sağ-sağ, üst-üst, alt-alt) her komşuyla;
    /// bitişik (sol-komşunun sağı...) yalnızca karşı eksende komşuyla örtüşen kartlarla düşünülür. Yakın değilse çizgi değişmez.
    /// </summary>
    private static int SnapEdge(int edge, bool isStart, bool xAxis, int perpStart, int perpEnd, IReadOnlyList<CellRect> neighbors, int tolerance)
    {
        var best = edge;
        var bestDistance = tolerance + 1;
        foreach (var n in neighbors)
        {
            var same = xAxis ? (isStart ? n.C : n.Right) : (isStart ? n.R : n.Bottom);
            if (Math.Abs(same - edge) < bestDistance)
            {
                best = same;
                bestDistance = Math.Abs(same - edge);
            }

            var nStart = xAxis ? n.R : n.C;
            var nEnd = xAxis ? n.Bottom : n.Right;
            if (nStart < perpEnd + tolerance && nEnd > perpStart - tolerance)
            {
                var touch = xAxis ? (isStart ? n.Right : n.C) : (isStart ? n.Bottom : n.R);
                if (Math.Abs(touch - edge) < bestDistance)
                {
                    best = touch;
                    bestDistance = Math.Abs(touch - edge);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Sürüklenerek bırakılan kartın hücresini komşularla hizalar: sol/sağ/üst/alt kenarı bir komşunun aynı tarafındaki kenara (veya bitişiğine)
    /// <paramref name="tolerance"/> hücre içinde yaklaşmışsa o hücreye kayar; yalnızca boş (çakışmayan) hücreler seçilir.
    /// </summary>
    public static (int C, int R) AlignCell((int C, int R) cell, (int W, int H) span, IReadOnlyList<CellRect> neighbors, Occupancy occupied, Spec g, int tolerance)
    {
        var (c, r) = cell;

        var bestC = c;
        var bestD = tolerance + 1;
        void TryX(int candidate)
        {
            var d = Math.Abs(candidate - c);
            if (d < bestD && candidate >= 0 && candidate + span.W <= g.Cols && occupied.IsFree(candidate, r, span.W, span.H))
            {
                bestD = d;
                bestC = candidate;
            }
        }

        foreach (var n in neighbors)
        {
            TryX(n.C);
            TryX(n.Right - span.W);
            if (n.R < r + span.H + tolerance && n.Bottom > r - tolerance)
            {
                TryX(n.Right);
                TryX(n.C - span.W);
            }
        }

        c = bestC;
        var bestR = r;
        bestD = tolerance + 1;
        void TryY(int candidate)
        {
            var d = Math.Abs(candidate - r);
            if (d < bestD && candidate >= 0 && candidate + span.H <= g.Rows && occupied.IsFree(c, candidate, span.W, span.H))
            {
                bestD = d;
                bestR = candidate;
            }
        }

        foreach (var n in neighbors)
        {
            TryY(n.R);
            TryY(n.Bottom - span.H);
            if (n.C < c + span.W + tolerance && n.Right > c - tolerance)
            {
                TryY(n.Bottom);
                TryY(n.R - span.H);
            }
        }

        return (c, bestR);
    }

    /// <summary>Kartın hangi kenarları bir komşunun aynı taraftaki kenarıyla aynı çizgide: gösterilecek kılavuzlar (piksel).</summary>
    public static List<Guide> GuidesFor(CellRect me, IReadOnlyList<CellRect> neighbors, Spec g)
    {
        var result = new List<Guide>();

        void Add(bool vertical, double pos, double meFrom, double meTo, double nFrom, double nTo)
        {
            var from = Math.Min(meFrom, nFrom);
            var to = Math.Max(meTo, nTo);
            var index = result.FindIndex(x => x.Vertical == vertical && Math.Abs(x.Pos - pos) < 0.5);
            if (index >= 0)
            {
                result[index] = result[index] with { From = Math.Min(result[index].From, from), To = Math.Max(result[index].To, to) };
            }
            else
            {
                result.Add(new Guide(vertical, pos, from, to));
            }
        }

        double Top(CellRect r) => LineY(r.R, g) + Inset;
        double Bottom(CellRect r) => LineY(r.Bottom, g) - Inset;
        double Left(CellRect r) => LineX(r.C, g) + Inset;
        double Right(CellRect r) => LineX(r.Right, g) - Inset;

        foreach (var n in neighbors)
        {
            if (n.C == me.C)
            {
                Add(true, Left(me), Top(me), Bottom(me), Top(n), Bottom(n));
            }

            if (n.Right == me.Right)
            {
                Add(true, Right(me), Top(me), Bottom(me), Top(n), Bottom(n));
            }

            if (n.R == me.R)
            {
                Add(false, Top(me), Left(me), Right(me), Left(n), Right(n));
            }

            if (n.Bottom == me.Bottom)
            {
                Add(false, Bottom(me), Left(me), Right(me), Left(n), Right(n));
            }
        }

        return result;
    }

    /// <summary>Kartın piksel boyutunu verilen hücre kapsamına çevirir (kenarlar ızgara çizgilerine oturur, komşu kartla 2×Inset boşluk kalır).</summary>
    public static Size SizeOf((int W, int H) span, Spec g) => new(span.W * g.Cell - 2 * Inset, span.H * g.Cell - 2 * Inset);

    /// <summary>
    /// Kartın seçili kenarları <paramref name="delta"/> kadar çekilince oluşan hücre kapsamı: hareket eden kenarlar en yakın ızgara çizgisine
    /// oturur, diğer kenarlar sabit kalır. En küçük boyut ve ızgara sınırları korunur. Saf hesap; pencerelere dokunmaz.
    /// </summary>
    public static (int C, int R, int W, int H) Resize(Rect card, Edges edges, Vector delta, (int W, int H) min, Spec g,
        IReadOnlyList<CellRect>? neighbors = null, int tolerance = 0)
    {
        int Col(double x, double shift) => (int)Math.Round((x + shift - g.Work.Left - g.OffsetX) / g.Cell);
        int Row(double y, double shift) => (int)Math.Round((y + shift - g.Work.Top - g.OffsetY) / g.Cell);

        var left = edges.HasFlag(Edges.Left);
        var right = edges.HasFlag(Edges.Right);
        var top = edges.HasFlag(Edges.Top);
        var bottom = edges.HasFlag(Edges.Bottom);

        var c0 = Col(card.Left + (left ? delta.X : 0), -Inset);
        var c1 = Col(card.Right + (right ? delta.X : 0), Inset);
        var r0 = Row(card.Top + (top ? delta.Y : 0), -Inset);
        var r1 = Row(card.Bottom + (bottom ? delta.Y : 0), Inset);

        // Komşu kartların kenar çizgilerine yapışma (hareket eden kenarlar için).
        if (neighbors is { Count: > 0 } && tolerance > 0)
        {
            if (left)
            {
                c0 = SnapEdge(c0, true, true, r0, r1, neighbors, tolerance);
            }

            if (right)
            {
                c1 = SnapEdge(c1, false, true, r0, r1, neighbors, tolerance);
            }

            if (top)
            {
                r0 = SnapEdge(r0, true, false, c0, c1, neighbors, tolerance);
            }

            if (bottom)
            {
                r1 = SnapEdge(r1, false, false, c0, c1, neighbors, tolerance);
            }
        }

        (c0, c1) = FitAxis(c0, c1, left, right, Math.Min(min.W, g.Cols), g.Cols);
        (r0, r1) = FitAxis(r0, r1, top, bottom, Math.Min(min.H, g.Rows), g.Rows);
        return (c0, r0, c1 - c0, r1 - r0);
    }

    private static (int Start, int End) FitAxis(int start, int end, bool movesStart, bool movesEnd, int min, int limit)
    {
        if (movesStart && !movesEnd)
        {
            start = Math.Min(start, end - min);
        }

        if (movesEnd && !movesStart)
        {
            end = Math.Max(end, start + min);
        }

        start = Math.Max(0, start);
        end = Math.Min(limit, end);
        if (end - start < min)
        {
            if (movesStart)
            {
                start = Math.Max(0, end - min);
            }
            else
            {
                end = Math.Min(limit, start + min);
            }

            if (end - start < min)
            {
                (start, end) = (0, Math.Min(limit, min)); // ızgaraya sığmıyorsa tüm eksen
            }
        }

        return (start, end);
    }

    /// <summary>Verilen dikdörtgenlerin dokunduğu tüm hücrelerin haritası.</summary>
    public static Occupancy Occupied(IEnumerable<Rect> rects, Spec g)
    {
        var cells = new List<(int, int)>();
        foreach (var rect in rects)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            var c0 = (int)Math.Floor((rect.Left - Inset - g.Work.Left - g.OffsetX) / g.Cell + 0.0001);
            var c1 = (int)Math.Ceiling((rect.Right + Inset - g.Work.Left - g.OffsetX) / g.Cell - 0.0001) - 1;
            var r0 = (int)Math.Floor((rect.Top - Inset - g.Work.Top - g.OffsetY) / g.Cell + 0.0001);
            var r1 = (int)Math.Ceiling((rect.Bottom + Inset - g.Work.Top - g.OffsetY) / g.Cell - 0.0001) - 1;
            for (var c = Math.Max(0, c0); c <= Math.Min(g.Cols - 1, c1); c++)
            {
                for (var r = Math.Max(0, r0); r <= Math.Min(g.Rows - 1, r1); r++)
                {
                    cells.Add((c, r));
                }
            }
        }

        return new Occupancy(g.Cols, g.Rows, cells);
    }

    /// <summary>
    /// <paramref name="span"/> büyüklüğündeki bir kartı, tercih edilen hücreye en yakın ve tamamen boş olan konuma yerleştirir.
    /// Boş yer yoksa null.
    /// </summary>
    public static (int C, int R)? FindFree((int C, int R) preferred, (int W, int H) span, Occupancy occupied, Spec g)
    {
        (int C, int R)? best = null;
        var bestDistance = long.MaxValue;
        for (var r = 0; r <= g.Rows - span.H; r++)
        {
            for (var c = 0; c <= g.Cols - span.W; c++)
            {
                var dc = c - preferred.C;
                var dr = r - preferred.R;
                var distance = (long)dc * dc + (long)dr * dr;
                if (distance < bestDistance && occupied.IsFree(c, r, span.W, span.H))
                {
                    bestDistance = distance;
                    best = (c, r);
                }
            }
        }

        return best;
    }
}
