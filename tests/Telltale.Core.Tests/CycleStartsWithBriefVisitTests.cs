using Telltale.Core.Intervals;
using Telltale.Core.Workflow;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

/// <summary>
/// Whether a cycle starts with a brief visit (owner decisions D90 and D91 of the brief visit treatment spec):
/// the stay that begins at the cycle's start, which is the issue's first stay in a work-role status, is a brief
/// visit under its own status's threshold. Development, Review and QE have the work role; Development is listed
/// under five minutes and Review and QE are not listed, unless a test says otherwise. The issue is created in New on
/// Tuesday 3 November 2026 at 10:00 unless a test says otherwise.
/// </summary>
public class CycleStartsWithBriefVisitTests
{
    private const string New = TestWorkflow.New;
    private const string Development = TestWorkflow.Development;
    private const string Review = TestWorkflow.Review;
    private const string QE = TestWorkflow.QE;
    private const string Done = TestWorkflow.Done;

    private static readonly DateTimeOffset Created = Local(2026, 11, 3, 10);

    private static readonly Dictionary<string, TimeSpan> DevelopmentUnderFiveMinutes = new() { [Development] = TimeSpan.FromMinutes(5) };

    [Test]
    public async Task A_cycle_whose_first_stay_in_a_work_status_ended_within_the_threshold_starts_with_a_brief_visit()
    {
        // The catch-up sequence: Development for twenty seconds, then two days in Review.
        var caughtUp = Created.AddDays(1);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", caughtUp, New, Development),
            new StatusChange("2", caughtUp.AddSeconds(20), Development, Review),
            new StatusChange("3", caughtUp.AddDays(2), Review, Done),
        ]);

        await Assert.That(StartsWithBriefVisit(timeline, DevelopmentUnderFiveMinutes)).IsTrue();
    }

    [Test]
    public async Task A_cycle_whose_first_stay_lasted_as_long_as_the_threshold_or_longer_does_not()
    {
        var started = Created.AddDays(1);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", started, New, Development),
            new StatusChange("2", started.AddMinutes(5), Development, Review),
            new StatusChange("3", started.AddDays(2), Review, Done),
        ]);

        await Assert.That(StartsWithBriefVisit(timeline, DevelopmentUnderFiveMinutes)).IsFalse();
    }

    [Test]
    public async Task A_brief_visit_later_in_the_cycle_does_not_make_the_cycle_start_with_one()
    {
        // Two days in Review first, then Development for twenty seconds: the cycle started in Review.
        var started = Created.AddDays(1);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", started, New, Review),
            new StatusChange("2", started.AddDays(2), Review, Development),
            new StatusChange("3", started.AddDays(2).AddSeconds(20), Development, Done),
        ]);
        var bothListed = new Dictionary<string, TimeSpan> { [Development] = TimeSpan.FromMinutes(5), [Review] = TimeSpan.FromMinutes(5) };

        await Assert.That(StartsWithBriefVisit(timeline, bothListed)).IsFalse();
    }

    [Test]
    public async Task A_first_stay_bounded_by_a_changelog_anomaly_is_not_a_brief_visit_so_the_cycle_does_not_start_with_one()
    {
        // In Development from 11:00:00. The next change, five seconds later, is from Review to QE: nothing says
        // when the issue left Development.
        var entered = Created.AddHours(1);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", entered, New, Development),
            new StatusChange("2", entered.AddSeconds(5), Review, QE),
            new StatusChange("3", entered.AddDays(2), QE, Done),
        ]);

        await Assert.That(timeline.Anomalies).HasSingleItem();
        await Assert.That(StartsWithBriefVisit(timeline, DevelopmentUnderFiveMinutes)).IsFalse();
    }

    [Test]
    public async Task An_issue_created_in_a_work_status_and_moved_on_within_the_threshold_has_a_cycle_that_starts_with_a_brief_visit()
    {
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Created.AddSeconds(20), Development, Review),
            new StatusChange("2", Created.AddDays(2), Review, Done),
        ]);

        await Assert.That(StartsWithBriefVisit(timeline, DevelopmentUnderFiveMinutes)).IsTrue();
    }

    [Test]
    public async Task A_first_stay_in_a_work_status_that_is_not_listed_is_not_a_brief_visit_whatever_follows_it()
    {
        // Twenty seconds in Review, which is not listed, and then twenty seconds in Development, which is.
        var started = Created.AddDays(1);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", started, New, Review),
            new StatusChange("2", started.AddSeconds(20), Review, Development),
            new StatusChange("3", started.AddSeconds(40), Development, QE),
            new StatusChange("4", started.AddDays(2), QE, Done),
        ]);

        await Assert.That(StartsWithBriefVisit(timeline, DevelopmentUnderFiveMinutes)).IsFalse();
    }

    [Test]
    public async Task A_move_into_a_work_status_and_straight_back_out_counts_though_the_real_start_came_days_later()
    {
        // A mis-click (D91): Development for ten seconds and back to New. Three days later the work starts.
        var misClick = Created.AddHours(1);
        var realStart = Created.AddDays(3);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", misClick, New, Development),
            new StatusChange("2", misClick.AddSeconds(10), Development, New),
            new StatusChange("3", realStart, New, Development),
            new StatusChange("4", realStart.AddDays(2), Development, Done),
        ]);

        await Assert.That(StartsWithBriefVisit(timeline, DevelopmentUnderFiveMinutes)).IsTrue();
    }

    [Test]
    public async Task An_issue_that_never_entered_a_work_status_has_no_cycle_to_start_with_one()
    {
        // New to Done in twenty seconds, with New and Done both listed: neither has the work role.
        var timeline = StatusTimeline.Build(Created, Done, [new StatusChange("1", Created.AddSeconds(20), New, Done)]);
        var listed = new Dictionary<string, TimeSpan> { [New] = TimeSpan.FromMinutes(5), [Done] = TimeSpan.FromMinutes(5) };

        await Assert.That(StartsWithBriefVisit(timeline, listed)).IsFalse();
    }

    private static bool StartsWithBriefVisit(StatusTimeline timeline, IReadOnlyDictionary<string, TimeSpan> thresholds) =>
        Cycles.StartsWithBriefVisit(BriefVisits.Stays(timeline), TestWorkflow.Roles, thresholds);
}
