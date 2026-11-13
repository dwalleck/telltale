using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The minimum group size (GLOSSARY, ADR-0003): a figure is shown only when the sprint has at least five
/// distinct assignees and the figure itself has at least three behind it. A row short of three keeps its issue
/// count (owner decision D5); a sprint short of five shows no count either (D41) and no rows (D45). Sprint 41 runs from 5 to 19 January 2026.
/// </summary>
public class SuppressionTests
{
    private const string CycleTimeHeader =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: |\n";

    private const string TimeInStatusHeading = "\n## Time in status\n";

    /// <summary>Both tables of a sprint short of five assignees: one line each, no rows (owner decision D45).</summary>
    private const string SuppressedTables = "\n## Cycle time by point bucket\n\nsuppressed\n" + TimeInStatusHeading + "\nsuppressed\n";

    /// <summary>What follows the two tables.</summary>
    private const string MethodHeading = "\n## Method\n";

    [Test]
    public async Task A_point_bucket_with_two_assignees_reads_suppressed_beside_its_issue_count_and_one_with_three_is_reported()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();

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
        pipeline.Jira.AddSprint41();
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
                new (double?, long?, long, double?, double?, double?, double?)[]
                {
                    (3, 3, 0, 2, 2, 2, 2),
                    (5, 3, 1, null, null, null, null),
                },
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_sprint_with_four_assignees_suppresses_every_figure_and_every_issue_count_and_shows_no_blank_or_zero()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();

        // Four people in one bucket: enough for the bucket, not for the sprint.
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-4", points: 3, People.Four);

        await pipeline.RunAsync("run", "--sprint", "41");

        // Owner decision D45: each table is one "suppressed" line. A row per bucket or status, even an empty
        // one, would show which buckets and statuses the sprint's few people had work in.
        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith(
            "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n"
            + "Delivered issues: suppressed\n"
            + SuppressedTables
            + MethodHeading);

        var delivered = await pipeline.QueryAsync<(long? DeliveredCount, long Suppressed)>("SELECT delivered_count, suppressed FROM delivered_per_sprint WHERE sprint_id = '41';");
        await Assert.That(delivered).IsEquivalentTo(new (long?, long)[] { (null, 1) });

        // The views say no more than the report: one row for the sprint, with no bucket and no status.
        var cycleTime = await pipeline.QueryAsync<CycleTimeRow>("SELECT * FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41';");
        await Assert.That(cycleTime.Select(row => (row.PointBucket, row.IssueCount, row.Suppressed, row.MedianBusinessDays, row.P85BusinessDays, row.MedianCalendarDays, row.P85CalendarDays)))
            .IsEquivalentTo(new (double?, long?, long, double?, double?, double?, double?)[] { (null, null, 1, null, null, null, null) });

        var timeInStatus = await pipeline.QueryAsync<(string? StatusId, string? StatusName, long? BoardPosition, long? IssueCount, long Suppressed, double? MedianBusinessDays, double? TotalBusinessDays)>(
            "SELECT status_id, status_name, board_position, issue_count, suppressed, median_business_days, total_business_days FROM time_in_status_per_sprint WHERE sprint_id = '41';");
        await Assert.That(timeInStatus).IsEquivalentTo(new (string?, string?, long?, long?, long, double?, double?)[] { (null, null, null, null, 1, null, null) });

        // Owner decision D41: nothing countable is left either. The counts of a small sprint are its people's
        // throughput, and summed they would give back the delivered count that is withheld.
        var sums = await pipeline.QueryAsync<(long? Delivered, long? InBuckets, long? InStatuses)>(
            """
            SELECT (SELECT SUM(delivered_count) FROM delivered_per_sprint WHERE sprint_id = '41'),
                   (SELECT SUM(issue_count) FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41'),
                   (SELECT SUM(issue_count) FROM time_in_status_per_sprint WHERE sprint_id = '41');
            """);
        await Assert.That(sums).IsEquivalentTo(new (long?, long?, long?)[] { (null, null, null) });
    }

    /// <summary>The cycle time table of a sprint whose delivered count is withheld: one line, no rows (owner decisions D41, D45, D54).</summary>
    private const string SuppressedCycleTime = "\n## Cycle time by point bucket\n\nsuppressed\n" + TimeInStatusHeading;

    [Test]
    [Arguments("two people delivered issues in three buckets")]
    [Arguments("nothing was delivered")]
    [Arguments("two people delivered straight from New")]
    public async Task A_sprint_whose_delivered_count_is_withheld_shows_its_cycle_time_table_as_one_suppressed_line_whatever_was_delivered(string what)
    {
        // Owner decision D49: the delivered count needs three distinct assignees among the delivered issues as
        // well as a team of five. Owner decision D54: the cycle time table of such a sprint is one line, as
        // for a sprint under five. Bucket rows would bound the count from below, their counts add up to it,
        // and a sentence about an empty table would tell zero from more. A team of five in each case.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            InSprintFromItsStart(pipeline.Jira, $"FAKE-9{number}", person);
        }

        switch (what)
        {
            case "two people delivered issues in three buckets":
                Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
                Delivered(pipeline.Jira, "FAKE-2", points: 3, People.One);
                Delivered(pipeline.Jira, "FAKE-3", points: 5, People.Two);
                InSprintFromItsStart(pipeline.Jira, "FAKE-4", People.Two).Moved(Local(2026, 1, 8, 9), Workflow.Done);
                break;
            case "two people delivered straight from New":
                pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One).Moved(Local(2026, 1, 7, 9), Workflow.Done);
                pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).AssignedTo(People.Two).Moved(Local(2026, 1, 7, 9), Workflow.Done);
                break;
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        pipeline.Out.GetStringBuilder().Clear();
        var query = await pipeline.RunAsync("query", "SELECT sprint_id, point_bucket, issue_count, median_business_days, suppressed, (SELECT COUNT(*) FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41') AS row_count, (SELECT SUM(issue_count) FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41') AS counted FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41';");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: suppressed\n" + SuppressedCycleTime);
        await Assert.That(await pipeline.ReadReportAsync(41)).DoesNotContain("has a cycle time");

        // The same through query: one row for the sprint, with no bucket and no count, in every case.
        await Assert.That(query).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            """
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed | row_count | counted |
            | --- | --- | --- | --- | --- | --- | --- |
            | 41 | NULL | NULL | NULL | 1 | 1 | NULL |

            """.ReplaceLineEndings("\n"));
        var delivered = await pipeline.QueryAsync<(long? DeliveredCount, long Suppressed, long TeamSuppressed)>("SELECT delivered_count, suppressed, team_suppressed FROM delivered_per_sprint WHERE sprint_id = '41';");
        await Assert.That(delivered).IsEquivalentTo(new (long?, long, long)[] { (null, 1, 0) });
    }

    [Test]
    public async Task The_time_in_status_counts_of_a_sprint_whose_delivered_count_is_withheld_stay_and_the_method_note_says_what_they_can_show()
    {
        // Owner decision D55: these counts are of every issue that spent time in a status, delivered or not.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        InSprintFromItsStart(pipeline.Jira, "FAKE-4", People.Three);
        InSprintFromItsStart(pipeline.Jira, "FAKE-5", People.Four);
        InSprintFromItsStart(pipeline.Jira, "FAKE-6", People.Five);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains(
            "\nDelivered issues: suppressed\n" + SuppressedCycleTime
            + "\n| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n"
            + "| Fake New | 5 | 1.0 | 5.0 |\n"
            + "| Fake Development | 5 | 9.0 | 31.1 |\n"
            + MethodHeading);
        await Assert.That(report).Contains(
            "the delivered count reads \"suppressed\" when fewer than three distinct assignees are among the delivered issues, and the cycle time table then reads \"suppressed\" as well, with no rows, because the number of its rows and their counts (the delivered issues that have a cycle time) would each give a lower bound of the withheld count. "
            + "The time in status table of such a sprint keeps its counts: they count every issue that spent time in a status, delivered or not, so the count of a late status can be close to the withheld delivered count. ");
    }

    [Test]
    public async Task A_bucket_short_of_three_assignees_keeps_its_count_in_a_sprint_whose_delivered_count_is_shown()
    {
        // Owner decision D5 still holds where D54 does not apply: three people delivered, so the delivered
        // count is shown, and the bucket with one assignee shows its count beside its withheld figures.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 5, People.Three);
        Delivered(pipeline.Jira, "FAKE-7", points: 5, People.Three);
        InSprintFromItsStart(pipeline.Jira, "FAKE-4", People.Four);
        InSprintFromItsStart(pipeline.Jira, "FAKE-5", People.Five);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 4\n"
            + CycleTimeHeader
            + "| 3 | 2 | suppressed | suppressed | suppressed | suppressed |\n"
            + "| 5 | 2 | suppressed | suppressed | suppressed | suppressed |\n"
            + TimeInStatusHeading);
    }

    [Test]
    public async Task A_delivered_count_with_three_assignees_among_the_delivered_issues_is_shown()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-7", points: 3, assignee: null);
        InSprintFromItsStart(pipeline.Jira, "FAKE-4", People.Four);
        InSprintFromItsStart(pipeline.Jira, "FAKE-5", People.Five);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 4\n");
        var delivered = await pipeline.QueryAsync<(long? DeliveredCount, long Suppressed, long TeamSuppressed)>("SELECT delivered_count, suppressed, team_suppressed FROM delivered_per_sprint WHERE sprint_id = '41';");
        await Assert.That(delivered).IsEquivalentTo(new (long?, long, long)[] { (4, 0, 0) });
    }

    [Test]
    public async Task A_sprint_with_five_assignees_across_all_its_issues_but_four_among_the_delivered_ones_is_not_suppressed()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
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
        pipeline.Jira.AddSprint41();

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

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            TimeInStatusHeading
            + "\n| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n"
            + "| Fake New | 7 | 1.0 | 7.0 |\n"
            + "| Fake Development | 7 | 2.0 | 14.0 |\n"
            + "| Fake Review | 4 | suppressed | suppressed |\n"
            + "| Fake QE | 3 | 1.0 | 3.0 |\n"
            + MethodHeading);

        var rows = await pipeline.QueryAsync<(string StatusName, long IssueCount, long Suppressed, double? MedianBusinessDays, double? TotalBusinessDays)>(
            "SELECT status_name, issue_count, suppressed, median_business_days, total_business_days FROM time_in_status_per_sprint WHERE sprint_id = '41' ORDER BY board_position;");
        await Assert.That(rows).IsEquivalentTo(
            new (string, long, long, double?, double?)[] { ("Fake New", 7, 0, 1, 7), ("Fake Development", 7, 0, 2, 14), ("Fake Review", 4, 1, null, null), ("Fake QE", 3, 0, 1, 3) },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Unassigned_issues_do_not_raise_a_buckets_assignee_count()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
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
        pipeline.Jira.AddSprint41();
        Delivered(pipeline.Jira, "FAKE-1", points: 3, People.One);
        Delivered(pipeline.Jira, "FAKE-2", points: 3, People.Two);
        Delivered(pipeline.Jira, "FAKE-3", points: 3, People.Three);
        Delivered(pipeline.Jira, "FAKE-4", points: 3, People.Four);
        Delivered(pipeline.Jira, "FAKE-5", points: 3, assignee: null);
        Delivered(pipeline.Jira, "FAKE-6", points: 3, assignee: null);

        await pipeline.RunAsync("run", "--sprint", "41");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).Contains(SuppressedTables + MethodHeading);
    }

    [Test]
    public async Task A_sprint_short_of_five_with_nothing_in_its_tables_reads_suppressed_like_one_that_has()
    {
        // Owner decision D45: "no issue has a cycle time" would say the sprint's few people delivered nothing
        // with a cycle, and "no issue spent time" that none of them had work in the sprint.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One);

        await pipeline.RunAsync("run", "--sprint", "41");

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).StartsWith(
            "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n"
            + "Delivered issues: suppressed\n"
            + SuppressedTables
            + MethodHeading);
        await Assert.That(report).DoesNotContain("No issue");

        var rows = await pipeline.QueryAsync<(long InBuckets, long InStatuses)>(
            """
            SELECT (SELECT COUNT(*) FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41' AND suppressed = 1 AND point_bucket IS NULL AND issue_count IS NULL),
                   (SELECT COUNT(*) FROM time_in_status_per_sprint WHERE sprint_id = '41' AND suppressed = 1 AND status_id IS NULL AND issue_count IS NULL AND total_business_days IS NULL);
            """);
        await Assert.That(rows).IsEquivalentTo(new[] { (1L, 1L) });
    }

    [Test]
    public async Task A_sprint_with_a_team_of_five_and_nothing_in_its_tables_still_says_so()
    {
        // The other side of D45: with a team, an empty table is a fact about the work and is stated.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            // Delivered in the sprint straight from New: a delivery with no cycle time, and no time in the sprint as a member.
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 6, 9)).AssignedTo(person)
                .Moved(Local(2026, 1, 7, 9), Workflow.Done);
        }

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith(
            "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n"
            + "Delivered issues: 5\n"
            + "\n## Cycle time by point bucket\n\nNo issue delivered in this sprint has a cycle time.\n"
            + TimeInStatusHeading + "\nNo issue of this sprint spent time in a status that is not done.\n"
            + MethodHeading);

        var rows = await pipeline.QueryAsync<long>(
            "SELECT (SELECT COUNT(*) FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41') + (SELECT COUNT(*) FROM time_in_status_per_sprint WHERE sprint_id = '41');");
        await Assert.That(rows).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    [Arguments(1)]
    [Arguments(0)]
    public async Task The_report_prints_no_figure_from_a_row_the_view_marks_suppressed_even_if_the_row_carries_one(int sprintSuppressed)
    {
        // The flag decides, not the figure being null: a view that set the flag and still let a figure through
        // (a fault, or a view changed by hand) must not get that figure into the report.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, $"FAKE-{number}", points: 3, person);
        }

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        // For a sprint the views mark suppressed, each table's view has its one row with no bucket or status
        // and no count; here that row still carries figures. Otherwise a row has a count and its own flag.
        var (bucket, status, statusName, position, count) = sprintSuppressed == 1
            ? ("NULL", "NULL", "NULL", "NULL", "NULL")
            : ("3.0", "'600004'", "'Fake Development'", "4", "5");
        await pipeline.ExecuteAsync(
            $$"""
            DROP VIEW cycle_time_per_sprint_and_bucket;
            CREATE VIEW cycle_time_per_sprint_and_bucket AS
            SELECT '41' AS sprint_id, {{bucket}} AS point_bucket, {{count}} AS issue_count, 2.0 AS median_business_days, 2.0 AS p85_business_days,
                   2.0 AS median_calendar_days, 2.0 AS p85_calendar_days, 1 AS suppressed;
            DROP VIEW time_in_status_per_sprint;
            CREATE VIEW time_in_status_per_sprint AS
            SELECT '41' AS sprint_id, {{status}} AS status_id, {{statusName}} AS status_name, {{position}} AS board_position, {{count}} AS issue_count,
                   2.0 AS median_business_days, 10.0 AS total_business_days, 1 AS suppressed;
            DROP VIEW delivered_per_sprint;
            CREATE VIEW delivered_per_sprint AS
            SELECT '41' AS sprint_id, 'Fake Sprint 41' AS sprint_name, 'closed' AS state, '2026-01-05T15:00:00.000Z' AS started,
                   '2026-01-19T16:00:00.000Z' AS ended, '2026-01-19T15:00:00.000Z' AS planned_end, 5 AS delivered_count, {{sprintSuppressed}} AS suppressed, {{sprintSuppressed}} AS team_suppressed;
            """);

        await pipeline.RunAllAsync(["report", "--sprint", "41"]);

        // A sprint the view marks suppressed shows no row at all (owner decision D45), whatever its rows carry.
        // In one it does not, a row's own flag withholds the row's figures.
        var expected = sprintSuppressed == 1
            ? "Delivered issues: suppressed\n" + SuppressedTables
            : "Delivered issues: 5\n"
                + CycleTimeHeader
                + "| 3 | 5 | suppressed | suppressed | suppressed | suppressed |\n"
                + TimeInStatusHeading
                + "\n| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n"
                + "| Fake Development | 5 | suppressed | suppressed |\n";
        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith("# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n" + expected + MethodHeading);
    }

    private sealed class CycleTimeRow
    {
        public double? PointBucket { get; init; }
        public long? IssueCount { get; init; }
        public long Suppressed { get; init; } = -1;
        public double? MedianBusinessDays { get; init; }
        public double? P85BusinessDays { get; init; }
        public double? MedianCalendarDays { get; init; }
        public double? P85CalendarDays { get; init; }
    }


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
