using Telltale.Core.Calendar;
using Telltale.Core.Intervals;
using static Telltale.Core.Tests.TestTime;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

public class BusinessCalendarTests
{
    [Test]
    public async Task A_full_weekday_is_one_business_day()
    {
        var tuesday = new Interval(Local(2026, 11, 10), Local(2026, 11, 11));

        var (time, days) = TeamCalendar().Measure(tuesday);

        await Assert.That(time).IsEqualTo(TimeSpan.FromHours(24));
        await Assert.That(days).IsEqualTo(1.0).Within(0.000001);
    }

    [Test]
    public async Task Friday_evening_to_Monday_morning_skips_the_weekend()
    {
        // 7 hours left on Friday + 9 hours of Monday.
        var span = new Interval(Local(2026, 11, 13, 17), Local(2026, 11, 16, 9));

        var (time, days) = TeamCalendar().Measure(span);

        await Assert.That(time).IsEqualTo(TimeSpan.FromHours(16));
        await Assert.That(days).IsEqualTo(16 / 24.0).Within(0.000001);
    }

    [Test]
    public async Task Time_entirely_on_a_weekend_counts_as_zero()
    {
        var span = new Interval(Local(2026, 11, 14, 10), Local(2026, 11, 15, 18));

        await Assert.That(TeamCalendar().BusinessTime(span)).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Holidays_are_excluded()
    {
        var thanksgiving = new DateOnly(2026, 11, 26);
        // Wednesday noon to Friday noon: 12h Wednesday + Thursday off + 12h Friday.
        var span = new Interval(Local(2026, 11, 25, 12), Local(2026, 11, 27, 12));

        var (time, days) = TeamCalendar(thanksgiving).Measure(span);

        await Assert.That(time).IsEqualTo(TimeSpan.FromHours(24));
        await Assert.That(days).IsEqualTo(1.0).Within(0.000001);
    }

    [Test]
    public async Task Utc_timestamps_are_assigned_to_the_teams_local_day()
    {
        // 03:00 to 06:00 UTC on Saturday is 21:00 to midnight on Friday in Chicago.
        var span = new Interval(
            new DateTimeOffset(2026, 11, 14, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 14, 6, 0, 0, TimeSpan.Zero));

        var (time, days) = TeamCalendar().Measure(span);

        await Assert.That(time).IsEqualTo(TimeSpan.FromHours(3));
        await Assert.That(days).IsEqualTo(3 / 24.0).Within(0.000001);
    }

    [Test]
    public async Task The_25_hour_day_when_daylight_saving_ends_is_still_one_day()
    {
        // Daylight saving ended on Sunday 2026-11-01. Treat every day as a business day to test it.
        var noWeekend = new BusinessCalendar(Zone, weekend: Array.Empty<DayOfWeek>());
        var sunday = noWeekend.DayInterval(new DateOnly(2026, 11, 1));

        var (time, days) = noWeekend.Measure(sunday);

        await Assert.That(time).IsEqualTo(TimeSpan.FromHours(25));
        await Assert.That(days).IsEqualTo(1.0).Within(0.000001);
    }

    [Test]
    public async Task An_empty_interval_measures_zero()
    {
        var instant = Local(2026, 11, 10, 9);

        var (time, days) = TeamCalendar().Measure(new Interval(instant, instant));

        await Assert.That(time).IsEqualTo(TimeSpan.Zero);
        await Assert.That(days).IsEqualTo(0.0).Within(0.000001);
    }
}
