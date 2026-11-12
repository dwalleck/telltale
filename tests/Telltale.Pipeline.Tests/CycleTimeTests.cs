using System.Text.Json.Nodes;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;
using Telltale.Pipeline.Tests.Harness;
using Telltale.Storage.Sqlite;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The cycle time table. January 2026 for reading the dates: the 5th, 12th and 19th are Mondays,
/// so the 10th-11th and 17th-18th are weekends.
/// </summary>
public class CycleTimeTests
{
    private const string TableHeader =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
        + "|---|---|---|---|---|---|\n";

    /// <summary>What follows the cycle time section. Asserting it after the last expected row shows the table has no further row.</summary>
    private const string NextSection = "\n## Time in status\n";

    [Test]
    public async Task The_report_shows_cycle_time_from_the_first_work_status_to_done_for_the_issues_bucket()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Thursday 09:00 to Monday 09:00: four calendar days, two of them a weekend.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).Estimated(3)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 12, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 4.0 | 4.0 |\n");
    }

    [Test]
    public async Task Time_in_blocked_before_any_work_status_does_not_start_the_cycle()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Blocked from Tuesday, development from Wednesday 09:00, done Friday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Blocked)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n");
    }

    [Test]
    public async Task An_issue_that_went_straight_to_done_is_delivered_but_contributes_no_cycle_time()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).Estimated(3)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 1\n");
        await Assert.That(report).Contains("\n## Cycle time by point bucket\n\nNo issue delivered in this sprint has a cycle time.\n" + NextSection);
        await Assert.That(await CycleTimeRowsAsync(pipeline, 41)).IsEmpty();
    }

    [Test]
    public async Task Time_in_blocked_after_the_cycle_started_counts_toward_cycle_time()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Development Tuesday 09:00, blocked Wednesday to Thursday, done Friday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Blocked)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 3.0 | 3.0 | 3.0 | 3.0 |\n");
    }

    [Test]
    [Arguments("Review")]
    [Arguments("QE")]
    public async Task An_issue_that_skips_development_starts_its_cycle_at_its_first_entry_into_review_or_qe(string firstWorkStatus)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Committed until Wednesday 09:00, then straight past Development; done Friday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Committed)
            .Moved(Local(2026, 1, 7, 9), firstWorkStatus == "Review" ? Workflow.Review : Workflow.QE)
            .Moved(Local(2026, 1, 8, 9), firstWorkStatus == "Review" ? Workflow.QE : Workflow.Review)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n");
    }

    [Test]
    public async Task A_move_from_done_to_shipped_does_not_move_the_cycle_end()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Development Tuesday 09:00, done Thursday 09:00, shipped the next Wednesday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done)
            .Moved(Local(2026, 1, 14, 9), Workflow.Shipped);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n");
    }

    [Test]
    public async Task A_reopened_issues_cycle_runs_from_its_first_work_status_to_its_final_entry_into_done()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Development Tuesday 09:00, done Wednesday, reopened Thursday, done again Friday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 3.0 | 3.0 | 3.0 | 3.0 |\n");
    }

    [Test]
    public async Task Each_issues_reopen_count_is_recorded_in_derived_data()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9))
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done);

        // Reopened twice. The move from Done to Shipped between them is not a reopen: both are done statuses.
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 5, 9))
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done)
            .Moved(Local(2026, 1, 8, 9), Workflow.Review)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Shipped)
            .Moved(Local(2026, 1, 13, 9), Workflow.Blocked)
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);

        // Reopened and not done again: not delivered, and still one reopen.
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 5, 9))
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Reopens are recorded but not reported in this slice, so no metric view carries them yet.
        var rows = await pipeline.QueryAsync<(string IssueKey, long ReopenCount)>(
            "SELECT i.issue_key, c.reopen_count FROM issue_cycle c JOIN issue i ON i.issue_id = c.issue_id ORDER BY i.issue_key;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("FAKE-1", 0L), ("FAKE-2", 2L), ("FAKE-3", 1L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_cycle_across_a_weekend_and_a_configured_holiday_has_fewer_business_days_than_calendar_days()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Config["holidays"] = new JsonArray("2026-01-19");

        // Friday noon to Tuesday noon: four calendar days. Saturday, Sunday and the Monday holiday do not
        // count, which leaves half of Friday and half of Tuesday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 15, 9)).Estimated(3)
            .Moved(Local(2026, 1, 16, 12), Workflow.Development)
            .Moved(Local(2026, 1, 20, 12), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(TableHeader + "| 3 | 1 | 1.0 | 1.0 | 4.0 | 4.0 |\n");
    }

    [Test]
    public async Task Without_the_holiday_the_same_cycle_counts_the_monday()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 15, 9)).Estimated(3)
            .Moved(Local(2026, 1, 16, 12), Workflow.Development)
            .Moved(Local(2026, 1, 20, 12), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 4.0 | 4.0 |\n");
    }

    // Median: the middle value, or the mean of the two middle values. P85: nearest rank, the smallest value
    // with at least 85% of the values at or below it. Expected figures are worked by hand from those rules.
    [Test]
    [Arguments(new[] { 3.0 }, 3.0, 3.0)]
    [Arguments(new[] { 3.0, 1.0 }, 2.0, 3.0)]
    [Arguments(new[] { 4.0, 2.0, 2.0 }, 2.0, 4.0)]
    [Arguments(new[] { 3.0, 2.0, 1.0, 2.0 }, 2.0, 3.0)]
    [Arguments(new[] { 4.0, 1.0, 3.0, 2.5 }, 2.75, 4.0)]
    [Arguments(new[] { 1.0, 4.0, 1.0, 1.0, 1.0, 1.0, 1.0 }, 1.0, 1.0)]
    [Arguments(new[] { 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0 }, 2.0, 3.0)]
    public async Task Median_is_the_middle_value_or_the_mean_of_the_two_middle_values_and_p85_is_nearest_rank(double[] cycleDays, double median, double p85)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Every cycle ends on Friday 16 January at noon and starts at most four days earlier, so none
        // touches a weekend and its business days equal its calendar days.
        var done = Local(2026, 1, 16, 12);
        foreach (var (days, number) in cycleDays.Select((days, index) => (days, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 6, 9)).Estimated(5)
                .Moved(done.AddDays(-days), Workflow.Development)
                .Moved(done, Workflow.Done);
        }

        await pipeline.RunAsync("run", "--sprint", "41");

        var rows = await CycleTimeRowsAsync(pipeline, 41);
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].IssueCount).IsEqualTo(cycleDays.Length);
        await Assert.That(rows[0].MedianBusinessDays!.Value).IsEqualTo(median).Within(0.000001);
        await Assert.That(rows[0].P85BusinessDays!.Value).IsEqualTo(p85).Within(0.000001);
        await Assert.That(rows[0].MedianCalendarDays!.Value).IsEqualTo(median).Within(0.000001);
        await Assert.That(rows[0].P85CalendarDays!.Value).IsEqualTo(p85).Within(0.000001);
    }

    [Test]
    public async Task The_table_has_a_row_per_point_bucket_with_its_issue_count_and_unestimated_issues_last()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Bucket 13: one issue, Tuesday to Thursday.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).Estimated(13)
            .Moved(Local(2026, 1, 13, 9), Workflow.Development)
            .Moved(Local(2026, 1, 15, 9), Workflow.Done);

        // Bucket 3: two issues. Friday noon to Monday noon is 1 business day and 3 calendar days;
        // Monday to Wednesday is 2 and 2. So the longer cycle in business days is the shorter in calendar days.
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).Estimated(3)
            .Moved(Local(2026, 1, 9, 12), Workflow.Development)
            .Moved(Local(2026, 1, 12, 12), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9)).Estimated(3)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);

        // No estimate: two issues, 1 day and 3 days.
        pipeline.Jira.AddIssue("FAKE-4", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 6, 10), Workflow.Development)
            .Moved(Local(2026, 1, 7, 10), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-5", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 6, 10), Workflow.Development)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);

        // Bucket 0.5: one issue, half a day.
        pipeline.Jira.AddIssue("FAKE-6", Local(2026, 1, 6, 9)).Estimated(0.5)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 21), Workflow.Done);

        // In no row of this sprint: delivered without a cycle, delivered in the next sprint, and not delivered.
        pipeline.Jira.AddIssue("FAKE-7", Local(2026, 1, 6, 9)).Estimated(13)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-8", Local(2026, 1, 6, 9)).Estimated(13)
            .Moved(Local(2026, 1, 14, 9), Workflow.Development)
            .Moved(Local(2026, 1, 21, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-9", Local(2026, 1, 6, 9)).Estimated(13)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development);

        await pipeline.RunAsync("run", "--sprint", "41");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 7\n");
        await Assert.That(report).Contains(
            TableHeader
            + "| 0.5 | 1 | 0.5 | 0.5 | 0.5 | 0.5 |\n"
            + "| 3 | 2 | 1.5 | 2.0 | 2.5 | 3.0 |\n"
            + "| 13 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| unestimated | 2 | 2.0 | 3.0 | 2.0 | 3.0 |\n"
            + NextSection);

        // The same rows by ad hoc SQL: an unestimated bucket is a null point_bucket.
        var rows = await CycleTimeRowsAsync(pipeline, 41);
        await Assert.That(rows.Select(row => (row.PointBucket, row.IssueCount)))
            .IsEquivalentTo(new (double?, long)[] { (0.5, 1), (3, 2), (13, 1), (null, 2) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Statuses_entered_and_left_at_the_same_instant_still_start_a_cycle_and_count_as_a_reopen()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // A bulk move: Development and Done in one instant.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        // Reopened and closed again in one instant, a day after it was done. That instant is its last move
        // into done, so its cycle runs from Tuesday 09:00 to Thursday 09:00.
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 5, 9)).Estimated(8)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done)
            .Moved(Local(2026, 1, 8, 9), Workflow.Review)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            TableHeader
            + "| 3 | 1 | 0.0 | 0.0 | 0.0 | 0.0 |\n"
            + "| 8 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + NextSection);
        var reopens = await pipeline.QueryAsync<(string IssueKey, long ReopenCount)>(
            "SELECT i.issue_key, c.reopen_count FROM issue_cycle c JOIN issue i ON i.issue_id = c.issue_id ORDER BY i.issue_key;");
        await Assert.That(reopens).IsEquivalentTo(new[] { ("FAKE-1", 0L), ("FAKE-2", 1L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Two_estimates_that_differ_only_in_the_third_decimal_are_two_rows_with_different_labels()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(2.001)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 7, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 5, 9)).Estimated(2.004)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            TableHeader
            + "| 2.001 | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + "| 2.004 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + NextSection);
    }

    [Test]
    public async Task An_abandoned_issue_that_was_worked_on_is_in_no_cycle_time_row()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = new JsonArray("300002");
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done)
            .Resolved("300001");
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 5, 9)).Estimated(3)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Resolved("300002");

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(TableHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + NextSection);
    }

    [Test]
    public async Task The_cycle_time_view_reads_only_rows_marked_delivered_whatever_else_they_carry()
    {
        // Derive gives an abandoned issue a cycle end and nothing else. The view must not lean on that:
        // "delivered" is the one test of a delivery (issue_cycle.sql), so a row that is not delivered stays
        // out even if it carries a cycle end, a sprint and a cycle time.
        await using var pipeline = new PipelineHarness();
        var store = new SqliteStore(pipeline.DatabasePath);
        await store.InitializeAsync();
        var start = Local(2026, 1, 6, 9);
        var end = Local(2026, 1, 8, 9);

        await store.ReplaceDerivedAsync(new DerivedData(
            SprintWindows: [],
            StatusIntervals: [],
            IssueCycles:
            [
                new IssueCycle("1", end, Delivered: true, DeliverySprintId: "41", ReopenCount: 0) { Cycle = new Cycle(start, 2, 2), PointBucket = 3 },
                new IssueCycle("2", end, Delivered: false, DeliverySprintId: "41", ReopenCount: 0) { Cycle = new Cycle(start, 6, 4), PointBucket = 3 },
            ],
            MembershipIntervals: [],
            SprintStatusTimes: [],
            BoardStatuses: [])
        {
            AsOf = end,
            Anomalies = [],
        }, new DerivedSource("synthetic-digest", ZoneId, Holidays: ""));

        var rows = await CycleTimeRowsAsync(pipeline, 41);
        await Assert.That(rows.Select(row => (row.PointBucket, row.IssueCount, row.P85BusinessDays, row.P85CalendarDays)))
            .IsEquivalentTo(new (double?, long, double?, double?)[] { (3, 1, 2, 2) });
    }

    [Test]
    public async Task An_issue_delivered_between_sprints_is_in_no_cycle_time_row()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // Done at 10:15 on the 19th: after sprint 41 was completed and before sprint 42 started.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).Estimated(3)
            .Moved(Local(2026, 1, 14, 9), Workflow.Development)
            .Moved(Local(2026, 1, 19, 10, 15), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        var rows = await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM cycle_time_per_sprint_and_bucket;");
        await Assert.That(rows).IsEquivalentTo(new[] { 0L });
    }

    /// <summary>The cycle time view's rows for one sprint, as ad hoc SQL reads them, smallest bucket first and unestimated last.</summary>
    private static Task<IReadOnlyList<CycleTimeRow>> CycleTimeRowsAsync(PipelineHarness pipeline, long sprintId) =>
        pipeline.QueryAsync<CycleTimeRow>(
            """
            SELECT point_bucket, issue_count, median_business_days, p85_business_days, median_calendar_days, p85_calendar_days
            FROM cycle_time_per_sprint_and_bucket
            WHERE sprint_id = @sprintId
            ORDER BY point_bucket IS NULL, point_bucket;
            """,
            new { sprintId = sprintId.ToString(System.Globalization.CultureInfo.InvariantCulture) });

    private sealed class CycleTimeRow
    {
        public double? PointBucket { get; init; }
        public long IssueCount { get; init; }
        public double? MedianBusinessDays { get; init; }
        public double? P85BusinessDays { get; init; }
        public double? MedianCalendarDays { get; init; }
        public double? P85CalendarDays { get; init; }
    }

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed, with a half-hour gap between them.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 20), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
