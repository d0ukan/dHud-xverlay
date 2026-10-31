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

    public static Spec Make(Rect work)
    {
        var cell = Math.Clamp(Math.Round(work.Width / Divisor), MinCell, MaxCell);
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
