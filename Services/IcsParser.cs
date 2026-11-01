using System.Globalization;
using System.Text;

namespace DHud.Services;

/// <summary>Takvim etkinliğinin tek bir oluşumu (yerel saat).</summary>
public sealed record CalendarOccurrence(DateTime Start, DateTime End, bool AllDay, string Title);

/// <summary>
/// Basit bir iCalendar (.ics) okuyucu: VEVENT'lerin başlık, başlangıç/bitiş (UTC, TZID, kayan saat, tüm gün), durum ve tekrar kuralını
/// (FREQ DAILY/WEEKLY/MONTHLY/YEARLY, INTERVAL, COUNT, UNTIL, haftalık BYDAY, EXDATE) çözer. Karmaşık kurallar (BYSETPOS vb.) desteklenmez.
/// </summary>
public static class IcsParser
{
    private sealed class RawEvent
    {
        public string Title = string.Empty;
        public DateTime Start;
        public DateTime End;
        public bool AllDay;
        public bool HasEnd;
        public Dictionary<string, string> Rule = [];
        public HashSet<DateTime> Exclusions = [];
        public bool Cancelled;
    }

    /// <summary><paramref name="from"/> (dahil) ile <paramref name="to"/> (hariç) arasına düşen tüm oluşumlar, başlangıca göre sıralı.</summary>
    public static IReadOnlyList<CalendarOccurrence> Occurrences(string ics, DateTime from, DateTime to)
    {
        var result = new List<CalendarOccurrence>();
        foreach (var raw in ParseEvents(ics))
        {
            if (raw.Cancelled)
            {
                continue;
            }

            var length = raw.HasEnd ? raw.End - raw.Start : raw.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
            foreach (var start in Starts(raw, from.AddDays(-1) - length, to))
            {
                var end = start + length;
                var overlaps = length == TimeSpan.Zero ? start >= from && start < to : end > from && start < to;
                if (overlaps && !raw.Exclusions.Contains(raw.AllDay ? start.Date : start))
                {
                    result.Add(new CalendarOccurrence(start, end, raw.AllDay, raw.Title));
                }
            }
        }

        return result.OrderBy(o => o.Start).ThenBy(o => o.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---- Tekrar kuralı ----

    /// <summary>Tarih hesabı yıl aralığını aşarsa (kötü niyetli/bozuk kural) null döner; çağıran döngüyü bitirir.</summary>
    private static DateTime? Safe(Func<DateTime> compute)
    {
        try
        {
            return compute();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static IEnumerable<DateTime> Starts(RawEvent e, DateTime windowStart, DateTime windowEnd)
    {
        if (!e.Rule.TryGetValue("FREQ", out var freq))
        {
            yield return e.Start;
            yield break;
        }

        var interval = e.Rule.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var n) && n > 0 ? Math.Min(n, 1000) : 1; // dev aralık DateTime taşmasına yol açıyordu
        var count = e.Rule.TryGetValue("COUNT", out var c) && int.TryParse(c, out var cn) ? cn : int.MaxValue;
        DateTime? until = e.Rule.TryGetValue("UNTIL", out var u) ? ParseValue(u, null, out _) : null;
        var emitted = 0;
        var time = e.Start.TimeOfDay;

        IEnumerable<DateTime> Candidates()
        {
            switch (freq)
            {
                case "DAILY":
                    for (var i = 0; i < 5000; i++)
                    {
                        if (Safe(() => e.Start.AddDays((double)i * interval)) is not { } day)
                        {
                            break;
                        }

                        yield return day;
                    }

                    break;

                case "WEEKLY":
                {
                    var days = e.Rule.TryGetValue("BYDAY", out var by) ? ParseByDay(by) : [e.Start.DayOfWeek];
                    var weekStart = e.Start.Date.AddDays(-(((int)e.Start.DayOfWeek + 6) % 7)); // Pazartesi
                    for (var w = 0; w < 1500; w++)
                    {
                        foreach (var d in days.OrderBy(d => ((int)d + 6) % 7))
                        {
                            if (Safe(() => weekStart.AddDays((double)w * 7 * interval + ((int)d + 6) % 7) + time) is not { } candidate)
                            {
                                yield break;
                            }

                            if (candidate >= e.Start)
                            {
                                yield return candidate;
                            }
                        }
                    }

                    break;
                }

                case "MONTHLY":
                    for (var i = 0; i < 600; i++)
                    {
                        if (Safe(() => e.Start.AddMonths(i * interval)) is not { } month)
                        {
                            break;
                        }

                        if (month.Day == e.Start.Day)
                        {
                            yield return month;
                        }
                    }

                    break;

                case "YEARLY":
                    for (var i = 0; i < 200; i++)
                    {
                        if (Safe(() => e.Start.AddYears(i * interval)) is not { } year)
                        {
                            break;
                        }

                        if (year.Month == e.Start.Month && year.Day == e.Start.Day)
                        {
                            yield return year;
                        }
                    }

                    break;

                default:
                    yield return e.Start;
                    break;
            }
        }

        foreach (var candidate in Candidates())
        {
            if (candidate >= windowEnd || (until is DateTime limit && candidate > limit.AddDays(e.AllDay ? 0 : 0)) || emitted >= count)
            {
                yield break;
            }

            emitted++;
            if (candidate >= windowStart)
            {
                yield return candidate;
            }
        }
    }

    private static List<DayOfWeek> ParseByDay(string by)
    {
        var map = new Dictionary<string, DayOfWeek>
        {
            ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday, ["TH"] = DayOfWeek.Thursday,
            ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday, ["SU"] = DayOfWeek.Sunday
        };
        var days = new List<DayOfWeek>();
        foreach (var part in by.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = part.Length >= 2 ? part[^2..] : part; // "1MO" gibi sıra ekleri yok sayılır
            if (map.TryGetValue(code, out var day))
            {
                days.Add(day);
            }
        }

        return days.Count == 0 ? [DayOfWeek.Monday] : days;
    }

    // ---- Dosya çözümü ----

    private static IEnumerable<RawEvent> ParseEvents(string ics)
    {
        RawEvent? current = null;
        foreach (var line in Unfold(ics))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new RawEvent();
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && current.Start != default)
                {
                    yield return current;
                }

                current = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var head = line[..colon];
            var value = line[(colon + 1)..];
            var parts = head.Split(';');
            var name = parts[0].ToUpperInvariant();
            var parameters = parts.Skip(1).Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].Trim('"'));

            switch (name)
            {
                case "SUMMARY":
                    current.Title = Unescape(value);
                    break;
                case "STATUS":
                    current.Cancelled = value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
                    break;
                case "DTSTART":
                    current.Start = ParseValue(value, parameters, out current.AllDay) ?? default;
                    break;
                case "DTEND":
                    current.End = ParseValue(value, parameters, out _) ?? default;
                    current.HasEnd = current.End != default;
                    break;
                case "RRULE":
                    // Aynı anahtar iki kez gelirse (bozuk/kötü niyetli akış) sonuncusu geçerli; ToDictionary burada çöküyordu.
                    var rule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Where(p => p.Length == 2))
                    {
                        rule[pair[0].ToUpperInvariant()] = pair[1];
                    }

                    current.Rule = rule;
                    break;
                case "EXDATE":
                    foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (ParseValue(item, parameters, out var allDay) is DateTime ex)
                        {
                            current.Exclusions.Add(allDay ? ex.Date : ex);
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>"20261101T100000Z", "20261101T100000" (+TZID) veya "20261101" (tüm gün) değerini yerel saate çevirir.</summary>
    private static DateTime? ParseValue(string value, Dictionary<string, string>? parameters, out bool allDay)
    {
        allDay = false;
        value = value.Trim();
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            allDay = true;
            return date;
        }

        var utc = value.EndsWith('Z');
        var text = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(text, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        if (utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(local, DateTimeKind.Utc), TimeZoneInfo.Local);
        }

        if (parameters is not null && parameters.TryGetValue("TZID", out var tzid) && FindZone(tzid) is { } zone)
        {
            try
            {
                return TimeZoneInfo.ConvertTime(local, zone, TimeZoneInfo.Local);
            }
            catch (ArgumentException)
            {
                return local; // geçersiz (boşlukta kalan) yerel saat
            }
        }

        return local; // kayan saat: olduğu gibi yerel kabul edilir
    }

    private static TimeZoneInfo? FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> Unfold(string ics)
    {
        var builder = new StringBuilder();
        foreach (var raw in ics.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                builder.Append(line.AsSpan(1));
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }

            builder.Append(line);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static string Unescape(string value) =>
        value.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
