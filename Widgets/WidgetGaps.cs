using System.Windows;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Bir widget kapanınca altındaki widget'ların yukarı kayması ve widget geri açılınca eski yerlerine dönmesi için hesaplar
/// (pencerelerden bağımsız saf mantık; <see cref="WidgetManager"/> uygular).
/// </summary>
public static class WidgetGaps
{
    /// <summary>Kayabilecek bir kart: kimliği, dikdörtgeni ve bulunduğu monitörün çalışma alanı.</summary>
    public sealed record Item(string Id, Rect Rect, Rect? Work);

    /// <summary>Hesabın sonucu: bu kimlikli kartlar <see cref="Dy"/> kadar yukarı kayar.</summary>
    public sealed record Shift(double Dy, List<Item> Moved);

    private static bool Horizontal(Rect a, Rect b) => a.Left < b.Right - 0.5 && a.Right > b.Left + 0.5;

    /// <summary>
    /// Kapanan kartın (<paramref name="closed"/>) altındaki sütunun, boşluğu kapatacak kadar yukarı kaymasını hesaplar.
    /// Kayma, hiçbir kartın başka bir karta, <paramref name="obstacles"/>'e (ör. üstteki bar) veya çalışma alanının üstüne
    /// girmeyeceği kadar sınırlanır. Kayacak bir şey yoksa null.
    /// </summary>
    public static Shift? Compute(Rect closed, IReadOnlyList<Item> items, IReadOnlyList<Rect> obstacles, double gap)
    {
        var live = items.Where(i => i.Rect.Width > 1 && i.Rect.Height > 1).ToList();
        var direct = live.Where(i => i.Rect.Top >= closed.Bottom - 1 && Horizontal(i.Rect, closed)).ToList();
        if (direct.Count == 0)
        {
            return null;
        }

        // Doğrudan altındakilerin altındakiler de (kendi sütunlarında) birlikte kayar; aralarındaki mesafe korunur.
        var moving = new List<Item>(direct);
        for (var i = 0; i < moving.Count; i++)
        {
            foreach (var below in live.Where(x => !moving.Contains(x) && x.Rect.Top >= moving[i].Rect.Bottom - 1 && Horizontal(x.Rect, moving[i].Rect)))
            {
                moving.Add(below);
            }
        }

        var wanted = direct.Min(i => i.Rect.Top) - closed.Top;   // en üstteki, kapananın yerine çıkar
        var fixedRects = live.Where(i => !moving.Contains(i)).Select(i => i.Rect).Concat(obstacles).ToList();
        var dy = wanted;
        foreach (var m in moving)
        {
            var limit = m.Work?.Top ?? double.MinValue;
            foreach (var o in fixedRects.Where(o => o.Bottom <= m.Rect.Top + 0.5 && Horizontal(o, m.Rect)))
            {
                limit = Math.Max(limit, o.Bottom + gap);
            }

            var free = m.Rect.Top - limit;
            if (free < dy)
            {
                dy = free;
            }
        }

        return dy >= 2 ? new Shift(dy, moving) : null;
    }

    /// <summary>
    /// <paramref name="openId"/> geri açılırken, onun yüzünden kaydırılmış kartları eski yerine döndürür. Kullanıcı kartı bu arada kendisi
    /// taşıdıysa (şimdiki üst konumu kayıtla uyuşmuyorsa) o karta dokunulmaz ve kayıtları silinir; kartın pencereden kalkmış olması
    /// (<paramref name="currentTop"/> null) kaydı korur. Eski yerine döndürülen kartların kimliklerini verir.
    /// </summary>
    public static List<string> Restore(List<ShiftRecord> ledger, string openId, Func<string, double?> currentTop, Action<string, double> moveBy)
    {
        var restored = new List<string>();
        foreach (var record in ledger.Where(r => r.Closed == openId).ToList())
        {
            if (currentTop(record.Moved) is not double top)
            {
                continue;   // kart şu an açık değil; kayıt kalır
            }

            var last = ledger.Last(r => r.Moved == record.Moved);
            if (Math.Abs(top - last.NewTop) > 3)
            {
                ledger.RemoveAll(r => r.Moved == record.Moved);   // kullanıcı taşımış: otomatik düzen bozulmasın
                continue;
            }

            var index = ledger.IndexOf(record);
            ledger.RemoveAt(index);
            foreach (var later in ledger.Skip(index).Where(r => r.Moved == record.Moved))
            {
                later.NewTop += record.Dy;   // aynı kart başka kapanışlarla da kaymıştı: onların beklenen konumu da aşağı iner
            }

            moveBy(record.Moved, record.Dy);
            restored.Add(record.Moved);
        }

        return restored;
    }
}
