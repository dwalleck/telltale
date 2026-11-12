using TUnit.Assertions.Enums;
using Telltale.Core.Intervals;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

public class TimeInStatusTests
{
    private const string InProgress = "3";
    private const string Review = "10101";
    private const string Done = "10001";

    // Sprint 41: Monday Nov 2, 09:00 to Friday Nov 13, completed at 17:00.
    private static readonly SprintWindow Sprint41 = new("41", Local(2026, 11, 2, 9), Completed: Local(2026, 11, 13, 17));

    private static readonly DateTimeOffset AsOf = Local(2026, 11, 20, 12);

    private static readonly StatusInterval[] Statuses =
    [
        new(InProgress, Local(2026, 10, 30, 9), Local(2026, 11, 9, 9)),
        new(Review, Local(2026, 11, 9, 9), Local(2026, 11, 10, 15)),
        new(Done, Local(2026, 11, 10, 15), null),
    ];

    [Test]
    public async Task Status_time_is_clipped_to_when_the_issue_was_in_the_sprint()
    {
        // Added to the sprint on Wednesday Nov 4 at 09:00, two days after it started.
        MembershipInterval[] membership = [new("41", Local(2026, 11, 4, 9), null)];

        var result = TimeInStatus.ForSprint(Sprint41, Statuses, membership, TeamCalendar(), AsOf);

        // In Progress, Wed 09:00 to Mon 09:00: 15h Wed + 24h Thu + 24h Fri + weekend + 9h Mon.
        await Assert.That(result[InProgress].CalendarTime).IsEqualTo(TimeSpan.FromDays(5));
        await Assert.That(result[InProgress].BusinessTime).IsEqualTo(TimeSpan.FromHours(72));
        await Assert.That(result[InProgress].BusinessDays).IsEqualTo(3.0).Within(0.000001);

        // Review, Mon 09:00 to Tue 15:00.
        await Assert.That(result[Review].BusinessTime).IsEqualTo(TimeSpan.FromHours(30));
        await Assert.That(result[Review].BusinessDays).IsEqualTo(1.25).Within(0.000001);

        // Done, Tue 15:00 until the sprint completed Fri 17:00: 9h + 24h + 24h + 17h.
        await Assert.That(result[Done].BusinessTime).IsEqualTo(TimeSpan.FromHours(74));
        await Assert.That(result[Done].BusinessDays).IsEqualTo(74 / 24.0).Within(0.000001);
    }

    [Test]
    public async Task An_issue_removed_mid_sprint_only_counts_while_it_was_a_member()
    {
        MembershipInterval[] membership = [new("41", Local(2026, 11, 2, 9), Local(2026, 11, 3, 9))];

        var result = TimeInStatus.ForSprint(Sprint41, Statuses, membership, TeamCalendar(), AsOf);

        await Assert.That(result.Keys).IsEquivalentTo(new[] { InProgress }, CollectionOrdering.Matching);
        await Assert.That(result[InProgress].BusinessDays).IsEqualTo(1.0).Within(0.000001);
    }

    [Test]
    public async Task An_active_sprint_runs_until_as_of()
    {
        var active = Sprint41 with { Completed = null };
        var asOfTuesday = Local(2026, 11, 10, 12);
        MembershipInterval[] membership = [new("41", Local(2026, 11, 2, 9), null)];

        var result = TimeInStatus.ForSprint(active, Statuses, membership, TeamCalendar(), asOfTuesday);

        await Assert.That(result[Review].BusinessTime).IsEqualTo(TimeSpan.FromHours(27));
        await Assert.That(result.ContainsKey(Done)).IsFalse();
    }

    [Test]
    public async Task Membership_in_another_sprint_is_ignored()
    {
        MembershipInterval[] membership = [new("42", Local(2026, 11, 2, 9), null)];

        var result = TimeInStatus.ForSprint(Sprint41, Statuses, membership, TeamCalendar(), AsOf);

        await Assert.That(result).IsEmpty();
    }
}
