using Telltale.Core.Calendar;
using Telltale.Core.Intervals;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

public class BusinessCalendarTests
{
    [Fact]
    public void A_full_weekday_is_one_business_day()
    {
        var tuesday = new Interval(Local(2026, 11, 10), Local(2026, 11, 11));

        var (time, days) = TeamCalendar().Measure(tuesday);

        Assert.Equal(TimeSpan.FromHours(24), time);
        Assert.Equal(1.0, days, 6);
    }

    [Fact]
    public void Friday_evening_to_Monday_morning_skips_the_weekend()
    {
        // 7 hours left on Friday + 9 hours of Monday.
        var span = new Interval(Local(2026, 11, 13, 17), Local(2026, 11, 16, 9));

        var (time, days) = TeamCalendar().Measure(span);

        Assert.Equal(TimeSpan.FromHours(16), time);
        Assert.Equal(16 / 24.0, days, 6);
    }

    [Fact]
    public void Time_entirely_on_a_weekend_counts_as_zero()
    {
        var span = new Interval(Local(2026, 11, 14, 10), Local(2026, 11, 15, 18));

        Assert.Equal(TimeSpan.Zero, TeamCalendar().BusinessTime(span));
    }

    [Fact]
    public void Holidays_are_excluded()
    {
        var thanksgiving = new DateOnly(2026, 11, 26);
        // Wednesday noon to Friday noon: 12h Wednesday + Thursday off + 12h Friday.
        var span = new Interval(Local(2026, 11, 25, 12), Local(2026, 11, 27, 12));

        var (time, days) = TeamCalendar(thanksgiving).Measure(span);

        Assert.Equal(TimeSpan.FromHours(24), time);
        Assert.Equal(1.0, days, 6);
    }

    [Fact]
    public void Utc_timestamps_are_assigned_to_the_teams_local_day()
    {
        // 03:00 to 06:00 UTC on Saturday is 21:00 to midnight on Friday in Chicago.
        var span = new Interval(
            new DateTimeOffset(2026, 11, 14, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 14, 6, 0, 0, TimeSpan.Zero));

        var (time, days) = TeamCalendar().Measure(span);

        Assert.Equal(TimeSpan.FromHours(3), time);
        Assert.Equal(3 / 24.0, days, 6);
    }

    [Fact]
    public void The_25_hour_day_when_daylight_saving_ends_is_still_one_day()
    {
        // Daylight saving ended on Sunday 2026-11-01. Treat every day as a business day to test it.
        var noWeekend = new BusinessCalendar(Chicago, weekend: Array.Empty<DayOfWeek>());
        var sunday = noWeekend.DayInterval(new DateOnly(2026, 11, 1));

        var (time, days) = noWeekend.Measure(sunday);

        Assert.Equal(TimeSpan.FromHours(25), time);
        Assert.Equal(1.0, days, 6);
    }

    [Fact]
    public void An_empty_interval_measures_zero()
    {
        var instant = Local(2026, 11, 10, 9);

        var (time, days) = TeamCalendar().Measure(new Interval(instant, instant));

        Assert.Equal(TimeSpan.Zero, time);
        Assert.Equal(0.0, days, 6);
    }
}
