using Telltale.Core.Intervals;

namespace Telltale.Core.Calendar;

/// <summary>
/// Measures elapsed time in business days, in the team's time zone. Weekends and
/// holidays don't count; every other day counts in full, all 24 hours of it.
/// </summary>
/// <remarks>
/// This is deliberately not "working hours". It removes the weekend and holiday
/// distortion without per-person schedules. A ticket in progress from 9:00 to 17:00
/// on a Tuesday counts as 8 hours, or one third of a business day.
/// </remarks>
public sealed class BusinessCalendar
{
    private static readonly DayOfWeek[] DefaultWeekend = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly HashSet<DateOnly> _holidays;
    private readonly HashSet<DayOfWeek> _weekend;

    public BusinessCalendar(TimeZoneInfo timeZone, IEnumerable<DateOnly>? holidays = null, IEnumerable<DayOfWeek>? weekend = null)
    {
        TimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        _holidays = new HashSet<DateOnly>(holidays ?? Enumerable.Empty<DateOnly>());
        _weekend = new HashSet<DayOfWeek>(weekend ?? DefaultWeekend);
    }

    /// <summary>Uses an IANA time zone ID such as "America/Chicago" (works on Windows too).</summary>
    public static BusinessCalendar ForTimeZone(string timeZoneId, IEnumerable<DateOnly>? holidays = null) =>
        new(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId), holidays);

    public TimeZoneInfo TimeZone { get; }

    public bool IsBusinessDay(DateOnly date) =>
        !_weekend.Contains(date.DayOfWeek) && !_holidays.Contains(date);

    public TimeSpan BusinessTime(Interval interval) => Measure(interval).Time;

    public double BusinessDays(Interval interval) => Measure(interval).Days;

    /// <summary>
    /// Business time and business days in one pass. Days are counted per local day, as
    /// that day's overlap divided by its length, so a 25-hour day at the end of daylight
    /// saving time still counts as one day.
    /// </summary>
    public (TimeSpan Time, double Days) Measure(Interval interval)
    {
        if (interval.IsEmpty)
        {
            return (TimeSpan.Zero, 0);
        }

        var time = TimeSpan.Zero;
        var days = 0d;
        var lastDate = LocalDate(interval.End);

        for (var date = LocalDate(interval.Start); date <= lastDate; date = date.AddDays(1))
        {
            if (!IsBusinessDay(date))
            {
                continue;
            }

            var day = DayInterval(date);
            if (interval.Intersect(day) is { } overlap)
            {
                time += overlap.Duration;
                days += overlap.Duration / day.Duration;
            }
        }

        return (time, days);
    }

    /// <summary>A local calendar day as absolute time: usually 24 hours, 23 or 25 at daylight saving changes.</summary>
    public Interval DayInterval(DateOnly date) => new(StartOfDay(date), StartOfDay(date.AddDays(1)));

    private DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    private DateTimeOffset StartOfDay(DateOnly date)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // Some zones change clocks at midnight, so local midnight may not exist. Use the first valid time.
        while (TimeZone.IsInvalidTime(localMidnight))
        {
            localMidnight = localMidnight.AddMinutes(15);
        }

        return new DateTimeOffset(localMidnight, TimeZone.GetUtcOffset(localMidnight));
    }
}
