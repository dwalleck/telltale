namespace Telltale.Tests.Shared;

/// <summary>
/// Builds instants as wall-clock times in the synthetic team's zone, so tests read like the calendar. The one
/// home of the team's zone for both test projects: this file is compiled into each.
/// </summary>
public static class TeamTime
{
    public const string ZoneId = "America/Chicago";

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    public static DateTimeOffset Local(int year, int month, int day, int hour = 0, int minute = 0)
    {
        var wallClock = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, Zone.GetUtcOffset(wallClock));
    }
}
