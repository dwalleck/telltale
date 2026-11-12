using Telltale.Core.Intervals;
using TUnit.Assertions.Enums;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

public class StatusTimelineTests
{
    private const string ToDo = "10000";
    private const string InProgress = "3";
    private const string Review = "10101";
    private const string Done = "10001";

    private static readonly DateTimeOffset Created = Local(2026, 11, 2, 9);

    [Test]
    public async Task An_issue_with_no_transitions_has_been_in_its_current_status_since_creation()
    {
        var timeline = StatusTimeline.Build(Created, ToDo, []);

        await Assert.That(timeline.Intervals).HasSingleItem();
        var only = timeline.Intervals.Single();
        await Assert.That(only).IsEqualTo(new StatusInterval(ToDo, Created, null));
        await Assert.That(timeline.Anomalies).IsEmpty();
    }

    [Test]
    public async Task Transitions_become_back_to_back_intervals()
    {
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress),
            new StatusChange("2", Local(2026, 11, 4, 9), InProgress, Review),
            new StatusChange("3", Local(2026, 11, 5, 9), Review, Done),
        ]);

        var expected = new[]
        {
            new StatusInterval(ToDo, Created, Local(2026, 11, 3, 9)),
            new StatusInterval(InProgress, Local(2026, 11, 3, 9), Local(2026, 11, 4, 9)),
            new StatusInterval(Review, Local(2026, 11, 4, 9), Local(2026, 11, 5, 9)),
            new StatusInterval(Done, Local(2026, 11, 5, 9), null),
        };
        await Assert.That(timeline.Intervals).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(timeline.Anomalies).IsEmpty();
    }

    [Test]
    public async Task Duplicate_history_entries_are_ignored()
    {
        // The bulk changelog endpoint can return the same entry twice depending on page size.
        var move = new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress);

        var timeline = StatusTimeline.Build(Created, InProgress, [move, move]);

        await Assert.That(timeline.Intervals.Count).IsEqualTo(2);
        await Assert.That(timeline.Anomalies).IsEmpty();
    }

    [Test]
    public async Task Changes_are_sorted_by_time_regardless_of_input_order()
    {
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("2", Local(2026, 11, 4, 9), InProgress, Review),
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress),
        ]);

        await Assert.That(timeline.Intervals.Select(i => i.StatusId)).IsEquivalentTo(new[] { ToDo, InProgress, Review }, CollectionOrdering.Matching);
        await Assert.That(timeline.Anomalies).IsEmpty();
    }

    [Test]
    public async Task Changes_at_the_same_instant_are_ordered_by_numeric_history_id()
    {
        var sameMoment = Local(2026, 11, 3, 9);

        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("10", sameMoment, InProgress, Review),
            new StatusChange("9", sameMoment, ToDo, InProgress),
        ]);

        // "9" comes before "10", so In Progress lasts zero time and is dropped.
        await Assert.That(timeline.Intervals.Select(i => i.StatusId)).IsEquivalentTo(new[] { ToDo, Review }, CollectionOrdering.Matching);
        await Assert.That(timeline.Anomalies).IsEmpty();
    }

    [Test]
    public async Task Returning_to_a_status_gives_it_a_second_interval()
    {
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, Review),
            new StatusChange("2", Local(2026, 11, 4, 9), Review, InProgress),
            new StatusChange("3", Local(2026, 11, 5, 9), InProgress, Review),
        ]);

        await Assert.That(timeline.Intervals.Count(i => i.StatusId == Review)).IsEqualTo(2);
    }

    [Test]
    public async Task A_change_that_does_not_match_the_timeline_is_reported_but_followed()
    {
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress),
            new StatusChange("2", Local(2026, 11, 4, 9), Review, Done),
        ]);

        await Assert.That(timeline.Intervals[^1].StatusId).IsEqualTo(Done);
        await Assert.That(timeline.Anomalies).HasSingleItem();
        var anomaly = timeline.Anomalies.Single();
        await Assert.That(anomaly).Contains("History 2");
    }

    [Test]
    public async Task Ending_in_a_different_status_than_the_issue_is_reported()
    {
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress),
        ]);

        await Assert.That(timeline.Anomalies).HasSingleItem();
        var anomaly = timeline.Anomalies.Single();
        await Assert.That(anomaly).Contains("current status");
    }
}
