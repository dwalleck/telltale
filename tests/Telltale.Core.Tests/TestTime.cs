using Telltale.Core.Calendar;

namespace Telltale.Core.Tests;

/// <summary>Builds instants as wall-clock times in the team's zone, so tests read like the calendar.</summary>
internal static class TestTime
{
    public static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    public static DateTimeOffset Local(int year, int month, int day, int hour = 0, int minute = 0)
    {
        var wallClock = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, Chicago.GetUtcOffset(wallClock));
    }

    public static BusinessCalendar TeamCalendar(params DateOnly[] holidays) => new(Chicago, holidays);
}
