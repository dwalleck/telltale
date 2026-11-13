using Telltale.Core.Intervals;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

/// <summary>
/// The edges of the brief visit rule and of the overlap rule (GLOSSARY: Brief visit; owner decisions D59 and
/// D60; decision bv-D1 for a stay of no length): the boundary at the threshold, clock time across a
/// daylight-saving change, and a status entered and left at the same instant.
/// Sprint 41 runs from Monday 2 November 2026, 09:00, to Friday the 13th, 17:00.
/// </summary>
public class BriefVisitRuleTests
{
    private const string New = TestWorkflow.New;
    private const string Development = TestWorkflow.Development;
    private const string Review = TestWorkflow.Review;

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static readonly DateTimeOffset Created = Local(2026, 10, 30, 9);

    private static readonly DateTimeOffset SprintStart = Local(2026, 11, 2, 9);

    private static readonly DateTimeOffset SprintEnd = Local(2026, 11, 13, 17);

    private static readonly SprintWindow Sprint41 = new("41", SprintStart, Completed: SprintEnd);

    private static readonly DateTimeOffset AsOf = Local(2026, 11, 20, 12);

    private static readonly MembershipInterval[] MemberThroughout = [new("41", Created, null)];

    private static readonly Dictionary<string, TimeSpan> DevelopmentListed = new() { [Development] = FiveMinutes };

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, false)]
    [Arguments(1, false)]
    public async Task A_stay_is_brief_just_under_the_threshold_and_not_at_it_or_over_it(long ticksPastTheThreshold, bool expected)
    {
        // One tick is the smallest difference two instants can have.
        var entered = Local(2026, 11, 3, 10);
        var left = entered + FiveMinutes + TimeSpan.FromTicks(ticksPastTheThreshold);

        var brief = BriefVisits.IsBrief(new StatusInterval(Development, entered, left), FiveMinutes);

        await Assert.That(brief).IsEqualTo(expected);
    }

    [Test]
    public async Task A_stay_whose_start_and_end_are_the_same_instant_is_brief()
    {
        var instant = Local(2026, 11, 3, 10);

        var brief = BriefVisits.IsBrief(new StatusInterval(Development, instant, instant), TimeSpan.FromTicks(1));

        await Assert.That(brief).IsTrue();
    }

    [Test]
    public async Task Clocks_going_forward_inside_a_stay_do_not_lengthen_it()
    {
        // Sunday 8 March 2026: at 02:00 the team's clocks go to 03:00. From 01:30 to 03:30 on the wall is two
        // hours, and one hour between the two instants.
        var stay = new StatusInterval(Development, Local(2026, 3, 8, 1, 30), Local(2026, 3, 8, 3, 30));

        await Assert.That(BriefVisits.IsBrief(stay, TimeSpan.FromMinutes(61))).IsTrue();
        await Assert.That(BriefVisits.IsBrief(stay, TimeSpan.FromMinutes(60))).IsFalse();
    }

    [Test]
    public async Task Clocks_going_back_inside_a_stay_do_not_shorten_it()
    {
        // Sunday 1 November 2026: at 02:00 the team's clocks go back to 01:00. From 00:30 to 02:30 on the wall
        // is two hours, and three hours between the two instants.
        var stay = new StatusInterval(Development, Local(2026, 11, 1, 0, 30), Local(2026, 11, 1, 2, 30));

        await Assert.That(BriefVisits.IsBrief(stay, TimeSpan.FromMinutes(180))).IsFalse();
        await Assert.That(BriefVisits.IsBrief(stay, TimeSpan.FromMinutes(181))).IsTrue();
    }

    [Test]
    public async Task A_status_entered_and_left_at_the_same_instant_is_a_stay_of_no_length_beside_the_timelines_intervals()
    {
        var caughtUp = Local(2026, 11, 3, 10);
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", caughtUp, New, Development),
            new StatusChange("2", caughtUp, Development, Review),
        ]);

        var stays = BriefVisits.Stays(timeline);

        await Assert.That(stays).IsEquivalentTo(new[]
        {
            new StatusInterval(New, Created, caughtUp),
            new StatusInterval(Development, caughtUp, caughtUp),
            new StatusInterval(Review, caughtUp, null),
        });
    }

    [Test]
    public async Task The_status_an_issue_is_in_now_is_not_a_stay_of_no_length_even_at_the_instant_it_was_entered()
    {
        var entered = Local(2026, 11, 3, 10);
        var timeline = StatusTimeline.Build(Created, Development, [new StatusChange("1", entered, New, Development)]);

        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, asOf: entered), BriefVisits.Stays(timeline), DevelopmentListed);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_stay_of_no_length_inside_the_time_the_sprint_counts_counts_toward_it()
    {
        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf), ThroughDevelopmentAt(Local(2026, 11, 3, 10)), DevelopmentListed);

        await Assert.That(statuses).IsEquivalentTo(new[] { Development });
    }

    [Test]
    public async Task A_stay_of_no_length_before_the_sprint_started_does_not_count_toward_it()
    {
        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf), ThroughDevelopmentAt(Local(2026, 10, 30, 10)), DevelopmentListed);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_stay_of_no_length_while_the_issue_was_not_a_member_of_the_sprint_does_not_count_toward_it()
    {
        // The issue joins on Wednesday; the instant is on Tuesday, inside the sprint's window.
        MembershipInterval[] joinedOnWednesday = [new("41", Local(2026, 11, 4, 9), null)];

        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, joinedOnWednesday, AsOf), ThroughDevelopmentAt(Local(2026, 11, 3, 10)), DevelopmentListed);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_stay_of_no_length_at_the_instant_the_sprint_starts_counts_toward_it()
    {
        // The counted time is a half-open span: its first instant is inside it.
        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf), ThroughDevelopmentAt(SprintStart), DevelopmentListed);

        await Assert.That(statuses).IsEquivalentTo(new[] { Development });
    }

    [Test]
    public async Task A_stay_of_no_length_at_the_instant_the_sprint_ends_does_not_count_toward_it()
    {
        // The counted time is a half-open span: the instant it ends at is outside it, and no time in status
        // is counted from that instant on.
        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf), ThroughDevelopmentAt(SprintEnd), DevelopmentListed);

        await Assert.That(statuses).IsEmpty();
    }

    /// <summary>The stays of an issue created in New and moved to Development and on to Review at <paramref name="instant"/>, where it still is.</summary>
    private static IEnumerable<StatusInterval> ThroughDevelopmentAt(DateTimeOffset instant) =>
        BriefVisits.Stays(StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", instant, New, Development),
            new StatusChange("2", instant, Development, Review),
        ]));
}
