using Telltale.Core.Intervals;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

public class TimeInStatusTests
{
    private const string InProgress = "3";
    private const string Review = "10101";
    private const string Done = "10001";

    // Sprint 41: Monday Nov 2, 09:00 to Friday Nov 13, completed at 17:00.
    private static readonly SprintWindow Sprint41 = new("41", Local(2026, 11, 2, 9), Local(2026, 11, 13, 17), Local(2026, 11, 13, 17));

    private static readonly DateTimeOffset AsOf = Local(2026, 11, 20, 12);

    private static readonly StatusInterval[] Statuses =
    [
        new(InProgress, Local(2026, 10, 30, 9), Local(2026, 11, 9, 9)),
        new(Review, Local(2026, 11, 9, 9), Local(2026, 11, 10, 15)),
        new(Done, Local(2026, 11, 10, 15), null),
    ];

    [Fact]
    public void Status_time_is_clipped_to_when_the_issue_was_in_the_sprint()
    {
        // Added to the sprint on Wednesday Nov 4 at 09:00, two days after it started.
        MembershipInterval[] membership = [new("41", Local(2026, 11, 4, 9), null)];

        var result = TimeInStatus.ForSprint(Sprint41, Statuses, membership, TeamCalendar(), AsOf);

        // In Progress, Wed 09:00 to Mon 09:00: 15h Wed + 24h Thu + 24h Fri + weekend + 9h Mon.
        Assert.Equal(TimeSpan.FromDays(5), result[InProgress].CalendarTime);
        Assert.Equal(TimeSpan.FromHours(72), result[InProgress].BusinessTime);
        Assert.Equal(3.0, result[InProgress].BusinessDays, 6);

        // Review, Mon 09:00 to Tue 15:00.
        Assert.Equal(TimeSpan.FromHours(30), result[Review].BusinessTime);
        Assert.Equal(1.25, result[Review].BusinessDays, 6);

        // Done, Tue 15:00 until the sprint completed Fri 17:00: 9h + 24h + 24h + 17h.
        Assert.Equal(TimeSpan.FromHours(74), result[Done].BusinessTime);
        Assert.Equal(74 / 24.0, result[Done].BusinessDays, 6);
    }

    [Fact]
    public void An_issue_removed_mid_sprint_only_counts_while_it_was_a_member()
    {
        MembershipInterval[] membership = [new("41", Local(2026, 11, 2, 9), Local(2026, 11, 3, 9))];

        var result = TimeInStatus.ForSprint(Sprint41, Statuses, membership, TeamCalendar(), AsOf);

        Assert.Equal(new[] { InProgress }, result.Keys);
        Assert.Equal(1.0, result[InProgress].BusinessDays, 6);
    }

    [Fact]
    public void An_active_sprint_runs_until_as_of()
    {
        var active = Sprint41 with { Completed = null };
        var asOfTuesday = Local(2026, 11, 10, 12);
        MembershipInterval[] membership = [new("41", Local(2026, 11, 2, 9), null)];

        var result = TimeInStatus.ForSprint(active, Statuses, membership, TeamCalendar(), asOfTuesday);

        Assert.Equal(TimeSpan.FromHours(27), result[Review].BusinessTime);
        Assert.False(result.ContainsKey(Done));
    }

    [Fact]
    public void Membership_in_another_sprint_is_ignored()
    {
        MembershipInterval[] membership = [new("42", Local(2026, 11, 2, 9), null)];

        var result = TimeInStatus.ForSprint(Sprint41, Statuses, membership, TeamCalendar(), AsOf);

        Assert.Empty(result);
    }
}
