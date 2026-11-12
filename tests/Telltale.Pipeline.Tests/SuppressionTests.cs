using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The minimum group size (GLOSSARY, ADR-0003): a figure is shown only when the sprint has at least five
/// distinct assignees and the figure itself has at least three behind it. Sprint 41 runs from 5 to 19 January 2026.
/// </summary>
public class SuppressionTests
{
    private const string CycleTimeHeader =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
        + "|---|---|---|---|---|---|\n";

    private const string TimeInStatusHeading = "\n## Time in status\n";

    [Test]
    public async Task A_point_bucket_with_two_assignees_reads_suppressed_beside_its_issue_count_and_one_with_three_is_reported()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Bucket 3: three people, one issue each. Bucket 5: three issues, but only two people.
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-4", points: 5, People.Four);
        Delivered(pipeline.Jira, "FAKE-5", points: 5, People.Four);
        Delivered(pipeline.Jira, "FAKE-6", points: 5, People.Five);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 3 | suppressed | suppressed | suppressed | suppressed |\n"
            + TimeInStatusHeading);
    }

    [Test]
    public async Task A_direct_query_of_the_cycle_time_view_gives_null_figures_a_suppressed_flag_and_the_issue_count_for_that_bucket()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-4", points: 5, People.Four);
        Delivered(pipeline.Jira, "FAKE-5", points: 5, People.Four);
        Delivered(pipeline.Jira, "FAKE-6", points: 5, People.Five);

        await pipeline.RunAsync("run", "--sprint", "41");

        var rows = await pipeline.QueryAsync<CycleTimeRow>("SELECT * FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41' ORDER BY point_bucket;");
        await Assert.That(rows.Select(row => (row.PointBucket, row.IssueCount, row.Suppressed, row.MedianBusinessDays, row.P85BusinessDays, row.MedianCalendarDays, row.P85CalendarDays)))
            .IsEquivalentTo(
                new (double?, long, long, double?, double?, double?, double?)[]
                {
                    (3, 3, 0, 2, 2, 2, 2),
                    (5, 3, 1, null, null, null, null),
                },
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_sprint_with_four_assignees_suppresses_every_figure_including_the_delivered_count_and_shows_no_blank_or_zero()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Four people in one bucket: enough for the bucket, not for the sprint.
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-4", points: 3, People.Four);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).IsEqualTo(
            "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n"
            + "Delivered issues: suppressed\n"
            + CycleTimeHeader
            + "| 3 | 4 | suppressed | suppressed | suppressed | suppressed |\n"
            + TimeInStatusHeading
            + "\n| Status | Issues | Median business days |\n| --- | ---: | ---: |\n"
            + "| Fake New | 4 | suppressed |\n"
            + "| Fake Development | 4 | suppressed |\n");

        var delivered = await pipeline.QueryAsync<(long? DeliveredCount, long Suppressed)>("SELECT delivered_count, suppressed FROM delivered_per_sprint WHERE sprint_id = '41';");
        await Assert.That(delivered).IsEquivalentTo(new (long?, long)[] { (null, 1) });

        var cycleTime = await pipeline.QueryAsync<CycleTimeRow>("SELECT * FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41';");
        await Assert.That(cycleTime.Select(row => (row.IssueCount, row.Suppressed, row.MedianBusinessDays, row.P85BusinessDays, row.MedianCalendarDays, row.P85CalendarDays)))
            .IsEquivalentTo(new (long, long, double?, double?, double?, double?)[] { (4, 1, null, null, null, null) });

        var timeInStatus = await pipeline.QueryAsync<(string StatusName, long IssueCount, long Suppressed, double? MedianBusinessDays)>(
            "SELECT status_name, issue_count, suppressed, median_business_days FROM time_in_status_per_sprint WHERE sprint_id = '41' ORDER BY board_position;");
        await Assert.That(timeInStatus).IsEquivalentTo(
            new (string, long, long, double?)[] { ("Fake New", 4, 1, null), ("Fake Development", 4, 1, null) },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_sprint_with_five_assignees_across_all_its_issues_but_four_among_the_delivered_ones_is_not_suppressed()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-4", points: 3, People.Four);

        // The fifth person's issue is in the sprint and still in development when it ends.
        InSprintFromItsStart(pipeline.Jira, "FAKE-5", People.Five);

        await pipeline.RunAsync("run", "--sprint", "41");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 4\n");
        await Assert.That(report).Contains(CycleTimeHeader + "| 3 | 4 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + TimeInStatusHeading);
    }

    [Test]
    public async Task A_status_with_two_assignees_reads_suppressed_beside_its_issue_count_and_one_with_three_is_reported()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);

        // Everyone's issue spends a day in New and two in Development. Three people's issues then spend a day in QE.
        foreach (var (key, person) in new[] { ("FAKE-1", People.Three), ("FAKE-2", People.Four), ("FAKE-3", People.Five) })
        {
            InSprintFromItsStart(pipeline.Jira, key, person)
                .Moved(Local(2026, 1, 8, 9), Workflow.QE)
                .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        }

        // Four issues spend a day in Review, but only two people have them: one issue is unassigned.
        foreach (var (key, person) in new[] { ("FAKE-4", People.One), ("FAKE-5", People.One), ("FAKE-6", People.Two), ("FAKE-7", null) })
        {
            InSprintFromItsStart(pipeline.Jira, key, person)
                .Moved(Local(2026, 1, 8, 9), Workflow.Review)
                .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        }

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            TimeInStatusHeading
            + "\n| Status | Issues | Median business days |\n| --- | ---: | ---: |\n"
            + "| Fake New | 7 | 1.0 |\n"
            + "| Fake Development | 7 | 2.0 |\n"
            + "| Fake Review | 4 | suppressed |\n"
            + "| Fake QE | 3 | 1.0 |\n");

        var rows = await pipeline.QueryAsync<(string StatusName, long IssueCount, long Suppressed, double? MedianBusinessDays)>(
            "SELECT status_name, issue_count, suppressed, median_business_days FROM time_in_status_per_sprint WHERE sprint_id = '41' ORDER BY board_position;");
        await Assert.That(rows).IsEquivalentTo(
            new (string, long, long, double?)[] { ("Fake New", 7, 0, 1), ("Fake Development", 7, 0, 2), ("Fake Review", 4, 1, null), ("Fake QE", 3, 0, 1) },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Unassigned_issues_do_not_raise_a_buckets_assignee_count()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);

        // Bucket 5: two people, and two issues nobody is assigned to.
        Delivered(pipeline.Jira, "FAKE-4", points: 5, People.Four);
        Delivered(pipeline.Jira, "FAKE-5", points: 5, People.Five);
        Delivered(pipeline.Jira, "FAKE-6", points: 5, assignee: null);
        Delivered(pipeline.Jira, "FAKE-7", points: 5, assignee: null);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 4 | suppressed | suppressed | suppressed | suppressed |\n"
            + TimeInStatusHeading);
    }

    [Test]
    public async Task Unassigned_issues_do_not_raise_a_sprints_assignee_count()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-4", points: 3, People.Four);
        Delivered(pipeline.Jira, "FAKE-5", points: 3, assignee: null);
        Delivered(pipeline.Jira, "FAKE-6", points: 3, assignee: null);

        await pipeline.RunAsync("run", "--sprint", "41");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).Contains(CycleTimeHeader + "| 3 | 6 | suppressed | suppressed | suppressed | suppressed |\n" + TimeInStatusHeading);
    }

    private sealed class CycleTimeRow
    {
        public double? PointBucket { get; init; }
        public long IssueCount { get; init; }
        public long Suppressed { get; init; } = -1;
        public double? MedianBusinessDays { get; init; }
        public double? P85BusinessDays { get; init; }
        public double? MedianCalendarDays { get; init; }
        public double? P85CalendarDays { get; init; }
    }

    private static void AddSprint(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));

    /// <summary>
    /// An issue of sprint 41, delivered in it with a cycle of two days: one business day in New, development
    /// from Tuesday 6 January 09:00, done Thursday 09:00.
    /// </summary>
    private static FakeIssue Delivered(FakeJira jira, string key, double points, FakePerson? assignee) =>
        InSprintFromItsStart(jira, key, assignee).Estimated(points).Moved(Local(2026, 1, 8, 9), Workflow.Done);

    /// <summary>
    /// An issue created in sprint 41 as the sprint starts (Monday 5 January 09:00) and moved to Development on
    /// Tuesday 09:00, where it still is. A null <paramref name="assignee"/> leaves it unassigned.
    /// </summary>
    private static FakeIssue InSprintFromItsStart(FakeJira jira, string key, FakePerson? assignee)
    {
        var issue = jira.AddIssue(key, Local(2026, 1, 5, 9)).InSprints(41).Moved(Local(2026, 1, 6, 9), Workflow.Development);
        return assignee is null ? issue : issue.AssignedTo(assignee);
    }
}
