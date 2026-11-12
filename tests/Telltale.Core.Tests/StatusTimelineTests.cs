using Telltale.Core.Intervals;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

public class StatusTimelineTests
{
    private const string ToDo = "10000";
    private const string InProgress = "3";
    private const string Review = "10101";
    private const string Done = "10001";

    private static readonly DateTimeOffset Created = Local(2026, 11, 2, 9);

    [Fact]
    public void An_issue_with_no_transitions_has_been_in_its_current_status_since_creation()
    {
        var timeline = StatusTimeline.Build(Created, ToDo, []);

        var only = Assert.Single(timeline.Intervals);
        Assert.Equal(new StatusInterval(ToDo, Created, null), only);
        Assert.Empty(timeline.Anomalies);
    }

    [Fact]
    public void Transitions_become_back_to_back_intervals()
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
        Assert.Equal(expected, timeline.Intervals);
        Assert.Empty(timeline.Anomalies);
    }

    [Fact]
    public void Duplicate_history_entries_are_ignored()
    {
        // The bulk changelog endpoint can return the same entry twice depending on page size.
        var move = new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress);

        var timeline = StatusTimeline.Build(Created, InProgress, [move, move]);

        Assert.Equal(2, timeline.Intervals.Count);
        Assert.Empty(timeline.Anomalies);
    }

    [Fact]
    public void Changes_are_sorted_by_time_regardless_of_input_order()
    {
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("2", Local(2026, 11, 4, 9), InProgress, Review),
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress),
        ]);

        Assert.Equal(new[] { ToDo, InProgress, Review }, timeline.Intervals.Select(i => i.StatusId));
        Assert.Empty(timeline.Anomalies);
    }

    [Fact]
    public void Changes_at_the_same_instant_are_ordered_by_numeric_history_id()
    {
        var sameMoment = Local(2026, 11, 3, 9);

        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("10", sameMoment, InProgress, Review),
            new StatusChange("9", sameMoment, ToDo, InProgress),
        ]);

        // "9" comes before "10", so In Progress lasts zero time and is dropped.
        Assert.Equal(new[] { ToDo, Review }, timeline.Intervals.Select(i => i.StatusId));
        Assert.Empty(timeline.Anomalies);
    }

    [Fact]
    public void Returning_to_a_status_gives_it_a_second_interval()
    {
        var timeline = StatusTimeline.Build(Created, Review,
        [
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, Review),
            new StatusChange("2", Local(2026, 11, 4, 9), Review, InProgress),
            new StatusChange("3", Local(2026, 11, 5, 9), InProgress, Review),
        ]);

        Assert.Equal(2, timeline.Intervals.Count(i => i.StatusId == Review));
    }

    [Fact]
    public void A_change_that_does_not_match_the_timeline_is_reported_but_followed()
    {
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress),
            new StatusChange("2", Local(2026, 11, 4, 9), Review, Done),
        ]);

        Assert.Equal(Done, timeline.Intervals[^1].StatusId);
        var anomaly = Assert.Single(timeline.Anomalies);
        Assert.Contains("History 2", anomaly);
    }

    [Fact]
    public void Ending_in_a_different_status_than_the_issue_is_reported()
    {
        var timeline = StatusTimeline.Build(Created, Done,
        [
            new StatusChange("1", Local(2026, 11, 3, 9), ToDo, InProgress),
        ]);

        var anomaly = Assert.Single(timeline.Anomalies);
        Assert.Contains("current status", anomaly);
    }
}
