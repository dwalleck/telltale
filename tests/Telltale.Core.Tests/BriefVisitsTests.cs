using Telltale.Core.Intervals;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

/// <summary>
/// The brief visit rule (GLOSSARY: Brief visit; owner decision D60) and which sprint a brief visit counts
/// toward (the overlap rule). Sprint 41 runs from Monday 2 November 2026, 09:00, to Friday the 13th, 17:00.
/// </summary>
public class BriefVisitsTests
{
    private const string Committed = "3";
    private const string Development = "4";
    private const string Review = "5";

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static readonly SprintWindow Sprint41 = new("41", Local(2026, 11, 2, 9), Completed: Local(2026, 11, 13, 17));

    private static readonly DateTimeOffset AsOf = Local(2026, 11, 20, 12);

    private static readonly MembershipInterval[] MemberThroughout = [new("41", Local(2026, 10, 30, 9), null)];

    [Test]
    public async Task A_stay_that_ended_after_less_than_the_threshold_is_brief()
    {
        var entered = Local(2026, 11, 3, 10);

        var brief = BriefVisits.IsBrief(new Stay(Development, entered, entered.AddSeconds(20)), FiveMinutes);

        await Assert.That(brief).IsTrue();
    }

    [Test]
    public async Task A_stay_as_long_as_a_working_day_is_not_brief()
    {
        var entered = Local(2026, 11, 3, 9);

        var brief = BriefVisits.IsBrief(new Stay(Development, entered, Local(2026, 11, 3, 17)), FiveMinutes);

        await Assert.That(brief).IsFalse();
    }

    [Test]
    public async Task A_stay_the_issue_is_still_in_is_not_a_visit_however_short_it_is_so_far()
    {
        var brief = BriefVisits.IsBrief(new Stay(Development, AsOf.AddSeconds(-20), End: null), FiveMinutes);

        await Assert.That(brief).IsFalse();
    }

    [Test]
    public async Task A_catch_up_through_a_listed_status_inside_the_sprint_counts_for_that_status_and_no_other()
    {
        // Committed until Tuesday 10:00, then Development for twenty seconds, then Review, where it stays for a day.
        var caughtUp = Local(2026, 11, 3, 10);
        Stay[] history =
        [
            new(Committed, Local(2026, 10, 30, 9), caughtUp),
            new(Development, caughtUp, caughtUp.AddSeconds(20)),
            new(Review, caughtUp.AddSeconds(20), Local(2026, 11, 4, 10)),
            new("8", Local(2026, 11, 4, 10), null),
        ];
        var thresholds = new Dictionary<string, TimeSpan> { [Development] = FiveMinutes, [Review] = FiveMinutes };

        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf), history, thresholds);

        await Assert.That(statuses).IsEquivalentTo(new[] { Development });
    }

    [Test]
    public async Task A_brief_visit_to_a_status_that_is_not_listed_counts_for_nothing()
    {
        var caughtUp = Local(2026, 11, 3, 10);
        Stay[] history =
        [
            new(Committed, Local(2026, 10, 30, 9), caughtUp),
            new(Development, caughtUp, caughtUp.AddSeconds(20)),
            new(Review, caughtUp.AddSeconds(20), null),
        ];
        var thresholds = new Dictionary<string, TimeSpan> { [Review] = FiveMinutes };

        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf), history, thresholds);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_brief_visit_before_the_sprint_started_does_not_count_for_the_sprint()
    {
        // Twenty seconds in Development on the Friday before the sprint, then back in it for the sprint's first week.
        var friday = Local(2026, 10, 30, 9);
        Stay[] history =
        [
            new(Development, friday, friday.AddSeconds(20)),
            new(Committed, friday.AddSeconds(20), Local(2026, 11, 2, 9)),
            new(Development, Local(2026, 11, 2, 9), Local(2026, 11, 6, 9)),
            new("8", Local(2026, 11, 6, 9), null),
        ];
        var thresholds = new Dictionary<string, TimeSpan> { [Development] = FiveMinutes };

        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf), history, thresholds);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_brief_visit_made_while_the_issue_was_not_a_member_of_the_sprint_does_not_count_for_it()
    {
        // The catch-up is on Tuesday; the issue joins the sprint on Wednesday, in Review.
        var caughtUp = Local(2026, 11, 3, 10);
        Stay[] history =
        [
            new(Committed, Local(2026, 10, 30, 9), caughtUp),
            new(Development, caughtUp, caughtUp.AddSeconds(20)),
            new(Review, caughtUp.AddSeconds(20), null),
        ];
        MembershipInterval[] joinedOnWednesday = [new("41", Local(2026, 11, 4, 9), null)];
        var thresholds = new Dictionary<string, TimeSpan> { [Development] = FiveMinutes };

        var statuses = BriefVisits.During(TimeInStatus.CountedTime(Sprint41, joinedOnWednesday, AsOf), history, thresholds);

        await Assert.That(statuses).IsEmpty();
    }
}
