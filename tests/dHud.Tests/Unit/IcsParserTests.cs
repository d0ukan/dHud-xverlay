using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

[Trait(Cat.Name, Cat.Fast)]
public class IcsParserTests
{
    // 2 Kasım 2026 Pazartesi: 7 günlük pencere [2 Kasım, 9 Kasım)
    private static readonly DateTime From = new(2026, 11, 2);
    private static readonly DateTime To = From.AddDays(7);

    private static string Calendar(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Concat(events.Select(e => $"BEGIN:VEVENT\r\n{e}\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

    private static IReadOnlyList<CalendarOccurrence> Run(string ics) => IcsParser.Occurrences(ics, From, To);

    [Fact]
    public void Single_event_with_floating_time()
    {
        var list = Run(Calendar("SUMMARY:Toplanti\r\nDTSTART:20261103T100000\r\nDTEND:20261103T110000"));
        var e = Assert.Single(list);
        Assert.Equal("Toplanti", e.Title);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0), e.Start);
        Assert.False(e.AllDay);
    }

    [Fact]
    public void Events_outside_the_window_are_ignored()
    {
        Assert.Empty(Run(Calendar("SUMMARY:Gec\r\nDTSTART:20261201T100000\r\nDTEND:20261201T110000")));
        Assert.Empty(Run(Calendar("SUMMARY:Erken\r\nDTSTART:20261001T100000\r\nDTEND:20261001T110000")));
    }

    [Fact]
    public void All_day_event()
    {
        var e = Assert.Single(Run(Calendar("SUMMARY:Tatil\r\nDTSTART;VALUE=DATE:20261105\r\nDTEND;VALUE=DATE:20261107")));
        Assert.True(e.AllDay);
        Assert.Equal(new DateTime(2026, 11, 5), e.Start);
    }

    [Fact]
    public void Utc_time_is_converted_to_local()
    {
        var e = Assert.Single(Run(Calendar("SUMMARY:UTC\r\nDTSTART:20261104T120000Z\r\nDTEND:20261104T130000Z")));
        var expected = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Local);
        Assert.Equal(expected, e.Start);
    }

    [Fact]
    public void Weekly_rule_with_byday()
    {
        var list = Run(Calendar("SUMMARY:Spor\r\nDTSTART:20261026T180000\r\nDTEND:20261026T190000\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE"));
        Assert.Equal([2, 4], list.Select(o => o.Start.Day)); // 9 Kasım pencerenin dışında (hariç uç)
    }

    [Fact]
    public void Daily_rule_with_count_and_until()
    {
        var count = Run(Calendar("SUMMARY:Ilac\r\nDTSTART:20261101T080000\r\nRRULE:FREQ=DAILY;COUNT=3"));
        Assert.Equal([2, 3], count.Select(o => o.Start.Day)); // 1,2,3 Kasım: pencerede 2 ve 3

        var until = Run(Calendar("SUMMARY:Kurs\r\nDTSTART:20261102T090000\r\nRRULE:FREQ=DAILY;UNTIL=20261104T235959"));
        Assert.Equal(3, until.Count);
    }

    [Fact]
    public void Monthly_and_yearly_rules()
    {
        var monthly = Assert.Single(Run(Calendar("SUMMARY:Fatura\r\nDTSTART:20260805T090000\r\nRRULE:FREQ=MONTHLY")));
        Assert.Equal(new DateTime(2026, 11, 5, 9, 0, 0), monthly.Start);

        var yearly = Assert.Single(IcsParser.Occurrences(
            Calendar("SUMMARY:Dogum gunu\r\nDTSTART;VALUE=DATE:20000106\r\nRRULE:FREQ=YEARLY"), new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)));
        Assert.True(yearly.AllDay);
        Assert.Equal(new DateTime(2026, 1, 6), yearly.Start);
    }

    [Fact]
    public void Exdate_and_cancelled_events_are_skipped()
    {
        var list = Run(Calendar("SUMMARY:Gunluk\r\nDTSTART:20261102T080000\r\nRRULE:FREQ=DAILY;COUNT=5\r\nEXDATE:20261103T080000"));
        Assert.Equal(4, list.Count);
        Assert.DoesNotContain(list, o => o.Start.Day == 3);

        Assert.Empty(Run(Calendar("SUMMARY:Iptal\r\nSTATUS:CANCELLED\r\nDTSTART:20261103T100000")));
    }

    [Fact]
    public void Folded_lines_and_escaped_characters()
    {
        var ics = "BEGIN:VEVENT\r\nSUMMARY:Cok uzun bir\r\n  baslik\\, virgullu\r\nDTSTART:20261103T100000\r\nEND:VEVENT\r\n";
        Assert.Equal("Cok uzun bir baslik, virgullu", Assert.Single(Run(ics)).Title);
    }

    [Fact]
    public void Results_are_sorted_by_start_time()
    {
        var list = Run(Calendar("SUMMARY:B\r\nDTSTART:20261103T150000", "SUMMARY:A\r\nDTSTART:20261103T090000"));
        Assert.Equal(["A", "B"], list.Select(o => o.Title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bozuk icerik")]
    [InlineData("BEGIN:VEVENT\r\nEND:VEVENT")]
    [InlineData("BEGIN:VEVENT\r\nDTSTART:abc\r\nEND:VEVENT")]
    public void Garbage_never_throws(string ics) => Assert.Empty(Run(ics));
}
