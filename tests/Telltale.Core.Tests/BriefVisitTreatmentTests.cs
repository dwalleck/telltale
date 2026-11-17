using Telltale.Core.Intervals;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

/// <summary>
/// The treatment of brief visits for time in status (GLOSSARY: Treatment; owner decisions D83 and D84): which
/// of an issue's status intervals are measured. Under <c>count</c> all of them are. Under <c>exclude</c> the
/// span of every brief visit to a listed status is left out, and nothing else is.
/// Every history here is of one issue created in New on Friday 30 October 2026.
/// </summary>
public class BriefVisitTreatmentTests
{
    private const string New = TestWorkflow.New;
    private const string Development = TestWorkflow.Development;
    private const string Review = TestWorkflow.Review;
    private const string Done = TestWorkflow.Done;

    private static readonly DateTimeOffset Created = Local(2026, 10, 30, 9);

    private static readonly DateTimeOffset Tuesday = Local(2026, 11, 3, 10);

    private static readonly Dictionary<string, TimeSpan> DevelopmentListed = new() { [Development] = TimeSpan.FromMinutes(5) };

    /// <summary>New until Tuesday 10:00, Development for twenty seconds, Review for two days, then Done.</summary>
    private static StatusTimeline CaughtUp() => StatusTimeline.Build(Created, Done,
    [
        new StatusChange("1", Tuesday, New, Development),
        new StatusChange("2", Tuesday.AddSeconds(20), Development, Review),
        new StatusChange("3", Tuesday.AddDays(2), Review, Done),
    ]);

    private static IReadOnlyList<StatusInterval> Measured(StatusTimeline timeline, BriefVisitTreatment treatment, IReadOnlyDictionary<string, TimeSpan>? thresholds = null) =>
        BriefVisitTreatments.IntervalsToMeasure(timeline.Intervals, BriefVisits.Stays(timeline), thresholds ?? DevelopmentListed, treatment);

    [Test]
    public async Task Under_count_every_interval_is_measured_as_it_is()
    {
        var timeline = CaughtUp();

        var measured = Measured(timeline, new CountBriefVisits());

        await Assert.That(measured).IsEquivalentTo(new StatusInterval[]
        {
            new(New, Created, Tuesday),
            new(Development, Tuesday, Tuesday.AddSeconds(20)),
            new(Review, Tuesday.AddSeconds(20), Tuesday.AddDays(2)),
            new(Done, Tuesday.AddDays(2), null),
        });
    }

    [Test]
    public async Task When_nothing_is_left_out_the_intervals_come_back_as_the_same_list_so_the_caller_need_not_measure_twice()
    {
        // Counted, and left out with no brief visit to leave out: a threshold of ten seconds, and a stay of twenty.
        var timeline = CaughtUp();
        var tenSeconds = new Dictionary<string, TimeSpan> { [Development] = TimeSpan.FromSeconds(10) };

        var counted = Measured(timeline, new CountBriefVisits());
        var nothingBrief = Measured(timeline, new ExcludeBriefVisits(), tenSeconds);
        var leftOut = Measured(timeline, new ExcludeBriefVisits());

        await Assert.That(counted).IsSameReferenceAs(timeline.Intervals);
        await Assert.That(nothingBrief).IsSameReferenceAs(timeline.Intervals);
        await Assert.That(leftOut).IsNotSameReferenceAs(timeline.Intervals);
    }

    [Test]
    public async Task Under_exclude_the_span_of_a_brief_visit_to_a_listed_status_is_not_measured()
    {
        var timeline = CaughtUp();

        var measured = Measured(timeline, new ExcludeBriefVisits());

        await Assert.That(measured).IsEquivalentTo(new StatusInterval[]
        {
            new(New, Created, Tuesday),
            new(Review, Tuesday.AddSeconds(20), Tuesday.AddDays(2)),
            new(Done, Tuesday.AddDays(2), null),
        });
    }

    [Test]
    public async Task A_longer_stay_in_the_same_status_is_still_measured_beside_a_brief_one()
    {
        // Development for twenty seconds, back to New for an hour, then Development for a day. The first of the
        // two has a change from Development to itself inside it, which cuts the interval and not the stay.
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", Tuesday, New, Development),
            new StatusChange("2", Tuesday.AddSeconds(10), Development, Development),
            new StatusChange("3", Tuesday.AddSeconds(20), Development, New),
            new StatusChange("4", Tuesday.AddHours(1), New, Development),
            new StatusChange("5", Tuesday.AddHours(25), Development, Review),
        ]);

        var measured = Measured(timeline, new ExcludeBriefVisits());

        await Assert.That(measured).IsEquivalentTo(new StatusInterval[]
        {
            new(New, Created, Tuesday),
            new(New, Tuesday.AddSeconds(20), Tuesday.AddHours(1)),
            new(Development, Tuesday.AddHours(1), Tuesday.AddHours(25)),
            new(Review, Tuesday.AddHours(25), null),
        });
    }

    [Test]
    public async Task A_brief_visit_to_a_status_that_is_not_listed_is_still_measured()
    {
        var timeline = CaughtUp();

        var measured = Measured(timeline, new ExcludeBriefVisits(), new Dictionary<string, TimeSpan> { [Review] = TimeSpan.FromMinutes(5) });

        await Assert.That(measured).IsEquivalentTo(timeline.Intervals);
        await Assert.That(measured).Contains(new StatusInterval(Development, Tuesday, Tuesday.AddSeconds(20)));
    }

    [Test]
    public async Task A_short_stay_bounded_by_a_changelog_anomaly_is_still_measured()
    {
        // The change out of Development says the issue came from Review: a change is missing, so nothing says
        // when the issue left Development, and the twenty seconds are not known to be the stay's length.
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Tuesday, New, Development),
            new StatusChange("2", Tuesday.AddSeconds(20), Review, Done),
        ]);

        var measured = Measured(timeline, new ExcludeBriefVisits());

        await Assert.That(measured).IsEquivalentTo(timeline.Intervals);
        await Assert.That(measured).Contains(new StatusInterval(Development, Tuesday, Tuesday.AddSeconds(20)));
    }

    [Test]
    public async Task A_stay_the_issue_is_still_in_is_measured_however_short_it_is_so_far()
    {
        var timeline = StatusTimeline.Build(Created, Development, [new StatusChange("1", Tuesday, New, Development)]);

        var measured = Measured(timeline, new ExcludeBriefVisits());

        await Assert.That(measured).IsEquivalentTo(new StatusInterval[]
        {
            new(New, Created, Tuesday),
            new(Development, Tuesday, null),
        });
    }

    [Test]
    public async Task A_stay_of_no_length_has_no_time_to_leave_out()
    {
        // Development is entered and left at the same instant: a brief visit under any threshold, with no
        // interval of its own. The intervals on either side of it are of other statuses and stay whole.
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", Tuesday, New, Development),
            new StatusChange("2", Tuesday, Development, Review),
        ]);

        var measured = Measured(timeline, new ExcludeBriefVisits());

        await Assert.That(measured).IsEquivalentTo(new StatusInterval[]
        {
            new(New, Created, Tuesday),
            new(Review, Tuesday, null),
        });
    }
}
