using Telltale.Core.Intervals;
using TUnit.Assertions.Enums;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

/// <summary>
/// A stay whose start or end comes from a changelog anomaly is never a brief visit, whatever its length
/// (GLOSSARY: Brief visit; the owner's decision of 2026-11-13, bv-D15 of the brief-status-visits run): its real
/// length is unknown. The two anomalies are a change dated before the issue was created, which the timeline
/// treats as made at the creation, and a change whose from-status is not the status the issue was in.
/// Sprint 41 runs from Monday 2 November 2026, 09:00, to Friday the 13th, 17:00. The issue is created on
/// Tuesday the 3rd at 10:00, inside the sprint, and is a member of it throughout.
/// </summary>
public class StaysBoundedByAnAnomalyTests
{
    private const string New = TestWorkflow.New;
    private const string Development = TestWorkflow.Development;
    private const string Review = TestWorkflow.Review;
    private const string QE = TestWorkflow.QE;
    private const string Done = TestWorkflow.Done;

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static readonly DateTimeOffset Created = Local(2026, 11, 3, 10);

    private static readonly SprintWindow Sprint41 = new("41", Local(2026, 11, 2, 9), Completed: Local(2026, 11, 13, 17));

    private static readonly DateTimeOffset AsOf = Local(2026, 11, 20, 12);

    private static readonly MembershipInterval[] MemberThroughout = [new("41", Created, null)];

    [Test]
    public async Task A_stay_that_begins_with_a_change_dated_before_the_creation_is_not_brief_though_it_ended_two_minutes_after_the_creation()
    {
        // The move into Development is dated a day before the issue was created, and the move out of it two
        // minutes after. The stay as recorded began a day earlier than the creation it is treated as made at.
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", Created.AddDays(-1), New, Development),
            new StatusChange("2", Created.AddMinutes(2), Development, Review),
        ]);

        var statuses = BriefVisitsTo(timeline, Development);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_stay_ended_by_a_change_that_names_another_from_status_is_not_brief()
    {
        // In Development from 11:00:00. The next change, five seconds later, is from Review to QE: the move
        // from Development to Review is missing, so nothing says when the issue left Development.
        var entered = Created.AddHours(1);
        var timeline = StatusTimeline.Build(Created, QE,
        [
            new StatusChange("1", entered, New, Development),
            new StatusChange("2", entered.AddSeconds(5), Review, QE),
        ]);

        var statuses = BriefVisitsTo(timeline, Development);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_stay_begun_by_a_change_that_names_another_from_status_is_not_brief()
    {
        // The change into QE names Review as the status it came from, and the issue was in Development: a
        // change is missing, so the issue may have been in QE before this one. QE is left twenty seconds later.
        var entered = Created.AddHours(1);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", entered, New, Development),
            new StatusChange("2", entered.AddDays(1), Review, QE),
            new StatusChange("3", entered.AddDays(1).AddSeconds(20), QE, Done),
        ]);

        var statuses = BriefVisitsTo(timeline, QE);

        await Assert.That(statuses).IsEmpty();
    }

    [Test]
    public async Task A_brief_visit_between_two_changes_that_line_up_still_counts_in_a_history_that_has_an_anomaly_elsewhere()
    {
        // The first change is dated before the creation. Days later the issue passes through Review in twenty
        // seconds, between two changes that are dated as written and each start where the last one ended.
        var later = Created.AddDays(2);
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Created.AddDays(-1), New, Development),
            new StatusChange("2", later, Development, Review),
            new StatusChange("3", later.AddSeconds(20), Review, Done),
        ]);

        var statuses = BriefVisitsTo(timeline, Review);

        await Assert.That(timeline.Anomalies).HasSingleItem();
        await Assert.That(statuses).IsEquivalentTo(new[] { Review });
    }

    [Test]
    public async Task A_change_that_names_no_from_status_bounds_a_stay_like_any_other()
    {
        // Old history can leave the from-status out. That is not a mismatch, and the timeline records no anomaly.
        var entered = Created.AddHours(1);
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", entered, New, Development),
            new StatusChange("2", entered.AddSeconds(20), FromStatusId: null, Review),
        ]);

        var statuses = BriefVisitsTo(timeline, Development);

        await Assert.That(timeline.Anomalies).IsEmpty();
        await Assert.That(statuses).IsEquivalentTo(new[] { Development });
    }

    [Test]
    public async Task A_change_back_into_the_status_the_issue_is_in_from_another_one_ends_the_stay_and_begins_a_new_one()
    {
        // In Development from 11:00. Two days later a change says Review to Development: the issue left
        // Development and came back, and the change that took it out is missing. Twenty seconds after that it
        // moves on to QE. Neither part is one unbroken stay of known length, so neither is a brief visit.
        var entered = Created.AddHours(1);
        var cameBack = entered.AddDays(2);
        var timeline = StatusTimeline.Build(Created, QE,
        [
            new StatusChange("1", entered, New, Development),
            new StatusChange("2", cameBack, Review, Development),
            new StatusChange("3", cameBack.AddSeconds(20), Development, QE),
        ]);

        var stays = BriefVisits.Stays(timeline);

        await Assert.That(stays).IsEquivalentTo(new[]
        {
            new Stay(New, Created, entered),
            new Stay(Development, entered, cameBack) { BoundedByAnAnomaly = true },
            new Stay(Development, cameBack, cameBack.AddSeconds(20)) { BoundedByAnAnomaly = true },
            new Stay(QE, cameBack.AddSeconds(20), null),
        }, CollectionOrdering.Matching);
        await Assert.That(BriefVisitsTo(timeline, Development)).IsEmpty();
    }

    [Test]
    public async Task The_timelines_intervals_are_what_they_were_for_a_history_with_both_anomalies()
    {
        // Marking a change takes nothing from the timeline and adds nothing to it: the intervals every other
        // figure is measured from follow each change's target status, as before.
        var entered = Created.AddHours(1);
        var timeline = StatusTimeline.Build(Created, QE,
        [
            new StatusChange("1", Created.AddDays(-1), New, Development),
            new StatusChange("2", entered, Review, Development),
            new StatusChange("3", entered.AddSeconds(20), Development, QE),
        ]);

        await Assert.That(timeline.Intervals).IsEquivalentTo(new[]
        {
            new StatusInterval(Development, Created, entered),
            new StatusInterval(Development, entered, entered.AddSeconds(20)),
            new StatusInterval(QE, entered.AddSeconds(20), null),
        }, CollectionOrdering.Matching);
        await Assert.That(timeline.Anomalies.Count).IsEqualTo(2);
    }

    /// <summary>The statuses with a brief visit that counts toward sprint 41, with <paramref name="listed"/> the one status checked, under five minutes.</summary>
    private static IReadOnlySet<string> BriefVisitsTo(StatusTimeline timeline, string listed) =>
        BriefVisits.During(
            TimeInStatus.CountedTime(Sprint41, MemberThroughout, AsOf),
            BriefVisits.Stays(timeline),
            new Dictionary<string, TimeSpan> { [listed] = FiveMinutes });
}
