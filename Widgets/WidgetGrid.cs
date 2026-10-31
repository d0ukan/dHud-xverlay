using System.Windows;

namespace DHud.Widgets;

/// <summary>
/// Widget'ların oturduğu ızgara (macOS masaüstü widget'ları gibi). Izgara, monitörün çalışma alanının genişliğine oranlıdır
/// (24 sütun), böylece her çözünürlükte benzer görünür. Bir kart, kapladığı hücrelere göre yerleşir ve başka bir widget'ın
/// (veya overlay'in) hücrelerine girmez. Saf hesaplardır; pencerelere dokunmaz.
/// </summary>
public static class WidgetGrid
{
    /// <summary>Kart ile hücre kenarı arasındaki boşluk (iki komşu kart arasında 2×Inset boşluk kalır).</summary>
    public const double Inset = 6;

    public const int Columns = 24;

    public sealed record Spec(Rect Work, double Cell, int Cols, int Rows, double OffsetX, double OffsetY);

    public static Spec Make(Rect work)
    {
        var cell = Math.Max(56, Math.Round(work.Width / Columns));
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

    /// <summary>Bir hücre kümesinin ekrandaki dikdörtgeni (kartın kapladığı alan; önizleme için).</summary>
    public static Rect CellsRect(int c, int r, (int W, int H) span, Spec g) =>
        new(g.Work.Left + g.OffsetX + c * g.Cell + Inset, g.Work.Top + g.OffsetY + r * g.Cell + Inset,
            span.W * g.Cell - 2 * Inset, span.H * g.Cell - 2 * Inset);

    /// <summary>Verilen dikdörtgenlerin dokunduğu tüm hücreler.</summary>
    public static HashSet<(int C, int R)> Occupied(IEnumerable<Rect> rects, Spec g)
    {
        var cells = new HashSet<(int, int)>();
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

        return cells;
    }

    /// <summary>
    /// <paramref name="span"/> büyüklüğündeki bir kartı, tercih edilen hücreye en yakın ve tamamen boş olan konuma yerleştirir.
    /// Boş yer yoksa null.
    /// </summary>
    public static (int C, int R)? FindFree((int C, int R) preferred, (int W, int H) span, HashSet<(int C, int R)> occupied, Spec g)
    {
        (int C, int R)? best = null;
        var bestDistance = double.MaxValue;
        for (var r = 0; r <= g.Rows - span.H; r++)
        {
            for (var c = 0; c <= g.Cols - span.W; c++)
            {
                if (!IsFree(c, r, span, occupied))
                {
                    continue;
                }

                var distance = (c - preferred.C) * (c - preferred.C) + (r - preferred.R) * (r - preferred.R);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (c, r);
                }
            }
        }

        return best;
    }

    private static bool IsFree(int c, int r, (int W, int H) span, HashSet<(int C, int R)> occupied)
    {
        for (var dc = 0; dc < span.W; dc++)
        {
            for (var dr = 0; dr < span.H; dr++)
            {
                if (occupied.Contains((c + dc, r + dr)))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
