using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using Telltale.Storage.Sqlite;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The baseline (GLOSSARY): figures pooled over the sprints listed in <c>baselineSprintIds</c>, shown beside each
/// sprint's own. Sprint 41 runs from Monday 5 to Friday 16 January 2026 and sprint 42 from Monday 19 to Friday 30 January.
/// </summary>
public class BaselineTests
{
    private const string CycleTimeHeader = ReportText.CycleTimeHeaderWithBaseline;

    /// <summary>What follows the time in status table: the method note's heading.</summary>
    private const string AfterTheTables = ReportText.MethodHeading;

    private const string TimeInStatusHeader = ReportText.TimeInStatusHeaderWithBaseline;

    [Test]
    public async Task With_baseline_sprints_configured_both_tables_show_figures_pooled_over_all_issues_of_those_sprints()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        // Sprint 41, bucket 3: five people, cycle times 1 to 5 business days.
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        // Sprint 42, bucket 3: three people, 6, 7 and 8 business days. Bucket 5: three more people, one day each.
        Delivered(pipeline.Jira, 42, "FAKE-6", points: 3, People.One, days: 6);
        Delivered(pipeline.Jira, 42, "FAKE-7", points: 3, People.Two, days: 7);
        Delivered(pipeline.Jira, 42, "FAKE-8", points: 3, People.Three, days: 8);
        Delivered(pipeline.Jira, 42, "FAKE-9", points: 5, People.Four, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-10", points: 5, People.Five, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-11", points: 5, People.Six, days: 1);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);

        // Bucket 3 pooled is 1 to 8: the median is the mean of 4 and 5, and P85 is the seventh of eight values.
        // Development pooled is 1, 1, 1, 1, 2, 3, 4, 5, 6, 7, 8: the sixth of eleven is 3.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 7.0 | 8.0 | 9.0 | 10.0 | 4.5 | 7.0 |\n"
            + "| 5 | 3 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 | 1.0 |\n"
            + "| Fake Development | 6 | 3.5 | 24.0 | 3.0 |\n" + AfterTheTables);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task With_no_baseline_configured_no_baseline_columns_appear_and_the_baseline_views_are_empty(bool settingPresentAndEmpty)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        if (settingPresentAndEmpty)
        {
            pipeline.Config["baselineSprintIds"] = new JsonArray();
        }
        else
        {
            pipeline.Config.Remove("baselineSprintIds");
        }

        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\n## Cycle time by point bucket\n\n"
            + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
            + "| --- | ---: | ---: | ---: | ---: | ---: |\n"
            + "| 3 | 5 | 3.0 | 5.0 | 3.0 | 7.0 |\n"
            + "\n## Time in status\n\n"
            + "| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n"
            + "| Fake New | 5 | 1.0 | 5.0 |\n"
            + "| Fake Development | 5 | 3.0 | 15.0 |\n" + AfterTheTables);

        await Assert.That(await pipeline.QueryAsync<long>("SELECT suppressed FROM baseline_cycle_time_per_bucket;")).IsEmpty();
        await Assert.That(await pipeline.QueryAsync<long>("SELECT suppressed FROM baseline_time_in_status;")).IsEmpty();
    }

    [Test]
    public async Task With_a_baseline_the_method_note_says_what_it_is_pooled_over_and_when_a_baseline_cell_reads_suppressed_or_none()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("42", "41");
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "- An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. An unassigned issue counts toward nobody.\n"
            + "- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints 41, 42), each a completed sprint. "
            + "A baseline figure reads \"suppressed\" when any baseline sprint has that row suppressed in its own report, so that a pooled figure cannot give back a withheld one. "
            + "It reads \"none\" when no issue of the baseline sprints is in the row. "
            + "Every baseline figure reads \"suppressed\" when a baseline sprint has its delivered count withheld: its team is smaller than five, or fewer than three distinct assignees are among its delivered issues.\n"
            + "- An interval that is still open");
    }

    [Test]
    public async Task Without_a_baseline_the_report_says_nothing_about_one()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).DoesNotContain("aseline");
    }

    [Test]
    public async Task The_sprint_being_reported_may_be_the_only_baseline_sprint_and_then_reads_against_its_own_figures()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            CycleTimeHeader
            + "| 3 | 5 | 3.0 | 5.0 | 3.0 | 7.0 | 3.0 | 5.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 5 | 1.0 | 5.0 | 1.0 |\n"
            + "| Fake Development | 5 | 3.0 | 15.0 | 3.0 |\n" + AfterTheTables);
    }

    [Test]
    public async Task A_query_over_the_views_reads_the_two_baseline_views()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var store = new SqliteStore(pipeline.DatabasePath);

        var rows = await store.QueryViewsAsync<(long Buckets, long Statuses)>(
            """
            SELECT (SELECT COUNT(*) FROM baseline_cycle_time_per_bucket),
                   (SELECT COUNT(*) FROM baseline_time_in_status);
            """);

        await Assert.That(rows).IsEquivalentTo(new[] { (1L, 2L) });
    }

    [Test]
    [Arguments("SELECT baseline_sprint_ids FROM run;")]
    [Arguments("SELECT COUNT(*) FROM run;")]
    [Arguments("SELECT b.point_bucket FROM baseline_cycle_time_per_bucket b JOIN json_each((SELECT baseline_sprint_ids FROM run)) j;")]
    public async Task A_query_over_the_views_cannot_read_the_derived_table_that_holds_the_baseline_list(string sql)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        pipeline.Jira.AddTeam(41);
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await store.QueryViewsAsync<string>(sql))
            .Throws<InvalidOperationException>().WithMessageContaining("reads only the metric views");
    }

    [Test]
    public async Task Unestimated_issues_have_a_baseline_row_of_their_own()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: null, person, days);
        }

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(CycleTimeHeader + "| unestimated | 5 | 3.0 | 5.0 | 3.0 | 7.0 | 3.0 | 5.0 |\n" + TimeInStatusHeader);
    }

    [Test]
    public async Task A_baseline_figure_with_fewer_than_three_assignees_is_suppressed_in_the_view_and_the_report_and_a_row_the_baseline_lacks_reads_none()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");

        // The baseline sprint. Bucket 3: five people, 1 to 5 days. Bucket 5: two people and an unassigned issue.
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        Delivered(pipeline.Jira, 41, "FAKE-6", points: 5, People.One, days: 1);
        Delivered(pipeline.Jira, 41, "FAKE-7", points: 5, People.Two, days: 1);
        Delivered(pipeline.Jira, 41, "FAKE-8", points: 5, assignee: null, days: 1);

        // The reported sprint is not a baseline sprint. Three people in each of buckets 3, 5 and 8; the baseline
        // has no 8-point issue, and none that was in Review.
        foreach (var (person, number) in new[] { (People.One, 11), (People.Two, 12), (People.Three, 13) })
        {
            Delivered(pipeline.Jira, 42, $"FAKE-{number}", points: 3, person, days: 1);
            Delivered(pipeline.Jira, 42, $"FAKE-{number + 20}", points: 8, person, days: 2, reviewDays: 1);
        }

        foreach (var (person, number) in new[] { (People.Four, 21), (People.Five, 22), (People.Six, 23) })
        {
            Delivered(pipeline.Jira, 42, $"FAKE-{number}", points: 5, person, days: 2);
        }

        await pipeline.RunAsync("run", "--sprint", "42");

        // Baseline Development is 1, 1, 1, 1, 2, 3, 4, 5: the mean of 1 and 2.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 1.0 | 1.0 | 1.0 | 1.0 | 3.0 | 5.0 |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | suppressed | suppressed |\n"
            + "| 8 | 3 | 3.0 | 3.0 | 3.0 | 3.0 | none | none |\n"
            + TimeInStatusHeader
            + "| Fake New | 9 | 1.0 | 9.0 | 1.0 |\n"
            + "| Fake Development | 9 | 2.0 | 15.0 | 1.5 |\n"
            + "| Fake Review | 3 | 1.0 | 3.0 | none |\n" + AfterTheTables);

        var cycleTime = await pipeline.QueryAsync<BaselineCycleTimeRow>("SELECT * FROM baseline_cycle_time_per_bucket ORDER BY point_bucket;");
        await Assert.That(cycleTime.Select(row => (row.PointBucket, row.Suppressed, row.MedianBusinessDays, row.P85BusinessDays, row.MedianCalendarDays, row.P85CalendarDays)))
            .IsEquivalentTo(
                new (double?, long, double?, double?, double?, double?)[]
                {
                    (3, 0, 3, 5, 3, 7),
                    (5, 1, null, null, null, null),
                },
                CollectionOrdering.Matching);

        await Assert.That(await ReadBaselineTimeInStatusAsync(pipeline)).IsEquivalentTo(
            new (string, long, double?)[] { ("Fake New", 0, 1), ("Fake Development", 0, 1.5) },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_baseline_figure_is_withheld_when_a_baseline_sprint_has_that_row_suppressed_so_the_pooled_figure_cannot_give_it_away()
    {
        // Owner decision D44. Without it the pooled bucket 3 would read 2, 3, 4, 6: its P85 is the fourth of
        // four values, which is the one cycle time sprint 41's own report withholds.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "42");

        // The pooled group passes on its own: four assignees in bucket 3 and in Review, and a team of six.
        // Bucket 5 pooled is 2, 2, 2, 3, 3, 3, 3, 3 and Development is 1, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 3.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | suppressed | suppressed |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 3.0 | 3.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | 2.0 |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | suppressed |\n" + AfterTheTables);

        var cycleTime = await pipeline.QueryAsync<BaselineCycleTimeRow>("SELECT * FROM baseline_cycle_time_per_bucket ORDER BY point_bucket;");
        await Assert.That(cycleTime.Select(row => (row.PointBucket, row.Suppressed, row.MedianBusinessDays, row.P85BusinessDays, row.MedianCalendarDays, row.P85CalendarDays)))
            .IsEquivalentTo(
                new (double?, long, double?, double?, double?, double?)[]
                {
                    (3, 1, null, null, null, null),
                    (5, 0, 3, 3, 3, 3),
                },
                CollectionOrdering.Matching);

        await Assert.That(await ReadBaselineTimeInStatusAsync(pipeline)).IsEquivalentTo(
            new (string, long, double?)[] { ("Fake New", 0, 1), ("Fake Development", 0, 2), ("Fake Review", 1, null) },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_baseline_sprint_that_started_before_the_start_date_the_store_was_filled_from_stops_derive_naming_the_setting_the_sprint_and_the_date()
    {
        // The store holds only issues updated since jira.backfillStart, so a sprint that started before it can
        // lack issues delivered in it, and a baseline pooled from it would be too low without a sign.
        // Sprint 41 started on 5 January and sprint 42 on 19 January.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["jira"]!["backfillStart"] = "2026-01-10";
        await pipeline.RunAllAsync(["backfill"]);
        var before = await pipeline.ReadStoreContentAsync();
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        var exit = await pipeline.RunAsync("derive");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file lists 41, which started before 2026-01-10, the jira.backfillStart the store was last filled from, "
            + "so issues delivered in it can be missing and the baseline would be too low. "
            + "Take it out of the list, or set jira.backfillStart to a date before it started and run backfill.\n");
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task A_report_refuses_a_baseline_list_that_gained_a_sprint_from_before_the_start_date_and_a_sprint_after_it_is_accepted()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["jira"]!["backfillStart"] = "2026-01-10";

        // Sprint 42 started after the start date: it is a baseline sprint like any other.
        pipeline.Config["baselineSprintIds"] = new JsonArray("42");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var written = await pipeline.ReadReportAsync(42);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(written).Contains(CycleTimeHeader + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | 3.0 | 4.0 |\n");
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file lists 41, which started before 2026-01-10, the jira.backfillStart the store was last filled from, "
            + "so issues delivered in it can be missing and the baseline would be too low. "
            + "Take it out of the list, or set jira.backfillStart to a date before it started and run backfill.\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).IsEqualTo(written);
    }

    [Test]
    public async Task A_report_asks_the_start_date_rule_again_for_an_unchanged_list_after_a_backfill_from_a_later_start_date()
    {
        // B7 of the final review. Every issue was updated after the later start date, so the backfill from it
        // stores the same raw rows and the derived data is not stale; only the date the store is complete
        // from has moved, past the start of baseline sprint 41.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        foreach (var issue in pipeline.Jira.Issues)
        {
            issue.Moved(Local(2026, 2, 2, 9), Workflow.Shipped);
        }

        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var written = await pipeline.ReadReportAsync(42);
        var digest = await pipeline.QueryAsync<string>("SELECT digest FROM raw_state;");
        pipeline.Config["jira"]!["backfillStart"] = "2026-01-10";
        await pipeline.RunAllAsync(["backfill"]);

        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(await pipeline.QueryAsync<string>("SELECT digest FROM raw_state;")).IsEquivalentTo(digest);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file lists 41, which started before 2026-01-10, the jira.backfillStart the store was last filled from, "
            + "so issues delivered in it can be missing and the baseline would be too low. "
            + "Take it out of the list, or set jira.backfillStart to a date before it started and run backfill.\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).IsEqualTo(written);
    }

    // Owner decision D50 (changes D47, which refused such a list). A reassignment in Jira can take a fixed
    // baseline sprint below five, and the owner should not have to edit a fixed list to get a report. So
    // nothing stops: every baseline figure is withheld, and the console and the report name the sprint. The
    // baseline views have no row at all then: which rows they had would say which buckets and statuses of
    // the small sprint were not empty, and its own report withholds that.
    [Test]
    [Arguments("41,42", "Baseline sprint 41 has fewer than five assignees, or fewer than three among its delivered issues")]
    [Arguments("41", "Baseline sprint 41 has fewer than five assignees, or fewer than three among its delivered issues")]
    [Arguments("41,42,43", "Baseline sprints 41 and 43 each have fewer than five assignees, or fewer than three among their delivered issues")]
    public async Task A_baseline_sprint_short_of_five_assignees_stops_nothing_withholds_every_baseline_figure_and_is_named_on_the_console(string listed, string named)
    {
        // Sprint 41 has four people, sprint 42 six, and sprint 43 completed with no issue at all. Pooled, 41
        // and 42 have a team of six.
        await using var pipeline = new PipelineHarness(Local(2026, 2, 17, 12));
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17), completed: Local(2026, 2, 13, 17));
        AddSprintFortyOneWithFourAssignees(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        pipeline.Config["baselineSprintIds"] = new JsonArray([.. listed.Split(',').Select(id => (JsonNode)id)]);

        var exit = await pipeline.RunAsync("derive");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"{named}, so every baseline figure is withheld (baselineSprintIds in the configuration file).\n");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM baseline_cycle_time_per_bucket;")).IsEquivalentTo(new[] { 0L });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM baseline_time_in_status;")).IsEquivalentTo(new[] { 0L });
        var sprints = await pipeline.QueryAsync<(string SprintId, long Suppressed)>("SELECT sprint_id, suppressed FROM baseline_sprint ORDER BY sprint_id;");
        await Assert.That(sprints).IsEquivalentTo(listed.Split(',').Select(id => (id, id == "42" ? 0L : 1L)), CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_run_whose_baseline_holds_a_sprint_short_of_five_assignees_writes_the_report_with_every_baseline_cell_suppressed_and_names_the_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintFortyOneWithFourAssignees(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Baseline sprint 41 has fewer than five assignees, or fewer than three among its delivered issues, so every baseline figure is withheld (baselineSprintIds in the configuration file).\n");

        // Bucket 5 is in sprint 42 only: without the small sprint its cell would hold a figure, and no cell reads "none".
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | suppressed | suppressed |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | suppressed | suppressed |\n");
        await Assert.That(report).Contains(
            TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 | suppressed |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | suppressed |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | suppressed |\n");
        await Assert.That(report).Contains(
            "- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints 41, 42), each a completed sprint. "
            + "A baseline figure reads \"suppressed\" when any baseline sprint has that row suppressed in its own report, so that a pooled figure cannot give back a withheld one. "
            + "It reads \"none\" when no issue of the baseline sprints is in the row. "
            + "Every baseline figure reads \"suppressed\" when a baseline sprint has its delivered count withheld: its team is smaller than five, or fewer than three distinct assignees are among its delivered issues.\n"
            + "- Baseline sprint 41 has its delivered count withheld, so every baseline figure in this report reads \"suppressed\".\n"
            + "- An interval that is still open");
    }

    [Test]
    public async Task A_report_under_a_baseline_list_that_gained_a_sprint_short_of_five_assignees_asks_for_derive_like_any_changed_list()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintFortyOneWithFourAssignees(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("42");
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file changed since the derived data was built. Run derive first.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();
    }

    [Test]
    public async Task A_baseline_sprint_that_fell_below_five_assignees_after_a_reassignment_in_jira_no_longer_stops_the_run()
    {
        // The finding behind D50: the list is fixed, and the assignees are read as of the last sync.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var before = await pipeline.ReadReportAsync(42);

        pipeline.Jira.Issues[4].AssignedTo(People.One).Moved(Local(2026, 2, 2, 9), Workflow.Shipped);
        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(before).Contains(CycleTimeHeader + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | 3.0 | 5.0 |\n");
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(CycleTimeHeader + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | suppressed | suppressed |\n");
    }

    [Test]
    [Arguments("two of its six people delivered")]
    [Arguments("nothing was delivered in it")]
    public async Task A_baseline_sprint_whose_delivered_count_is_withheld_withholds_the_whole_baseline_and_is_named_like_one_under_five(string what)
    {
        // Owner decision D56. Sprint 41 has a team of six and its delivered count is withheld (D49). Withholding
        // only the baseline rows of its buckets would show which buckets it has, and so tell nothing delivered
        // from something. So every baseline figure is withheld, in both tables, whatever it delivered, and
        // nothing stops. Sprint 42 has three assignees in every row and buckets 3 and 5.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        if (what == "two of its six people delivered")
        {
            Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.One, days: 1);
            Delivered(pipeline.Jira, 41, "FAKE-2", points: null, People.Two, days: 2);
        }

        foreach (var (person, number) in People.Team.Append(People.Six).Select((person, index) => (person, index + 3)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development);
        }

        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        var run = await pipeline.RunAsync("run", "--sprint", "42");
        var consoleOfRun = pipeline.Error.ToString().ReplaceLineEndings("\n");
        pipeline.Error.GetStringBuilder().Clear();
        await pipeline.RunAllAsync(["report", "--sprint", "41"]);
        var consoleOfReport = pipeline.Error.ToString().ReplaceLineEndings("\n");
        pipeline.Out.GetStringBuilder().Clear();
        var query = await pipeline.RunAsync(
            "query",
            "SELECT (SELECT COUNT(*) FROM baseline_cycle_time_per_bucket) AS cycle_time_rows, (SELECT COUNT(*) FROM baseline_time_in_status) AS status_rows, "
            + "(SELECT group_concat(sprint_id || '=' || suppressed, ' ') FROM (SELECT sprint_id, suppressed FROM baseline_sprint ORDER BY sprint_id)) AS baseline_sprints;");

        // The console names the sprint: once in a run (derive and report are one command), and again when
        // report runs on its own (review round 4, finding 27).
        const string Named = "Baseline sprint 41 has fewer than five assignees, or fewer than three among its delivered issues, so every baseline figure is withheld (baselineSprintIds in the configuration file).\n";
        await Assert.That(run).IsEqualTo(0);
        await Assert.That(consoleOfRun).IsEqualTo(Named);
        await Assert.That(consoleOfReport).IsEqualTo(Named);

        // Another sprint's report: every baseline cell, of a bucket sprint 41 has and of one it has not.
        var other = await pipeline.ReadReportAsync(42);
        await Assert.That(other).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | suppressed | suppressed |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | suppressed | suppressed |\n");
        await Assert.That(other).Contains(
            TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 | suppressed |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | suppressed |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | suppressed |\n");
        await Assert.That(other).Contains("- Baseline sprint 41 has its delivered count withheld, so every baseline figure in this report reads \"suppressed\".\n");

        // Its own report: no cycle time rows (D54), and its time in status rows with a withheld baseline.
        var own = await pipeline.ReadReportAsync(41);
        await Assert.That(own).Contains("\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n");
        await Assert.That(own).Contains(TimeInStatusHeader + "| Fake New | ");
        await Assert.That(own).DoesNotContain(" | none |");
        await Assert.That(own.Split('\n').Where(line => line.StartsWith("| Fake ", StringComparison.Ordinal)).All(line => line.EndsWith(" | suppressed |", StringComparison.Ordinal))).IsTrue();

        // And through query: the baseline views have no row, whatever sprint 41 delivered.
        await Assert.That(query).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            """
            | cycle_time_rows | status_rows | baseline_sprints |
            | --- | --- | --- |
            | 0 | 0 | 41=1 42=0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task Two_baseline_sprints_one_under_five_and_one_with_two_delivering_assignees_are_named_together_in_one_sentence()
    {
        // Review round 4, finding 33. Sprint 41 has four people; sprint 42 has six, two of whom delivered.
        // Sprint 43 is the one reported: six people delivered in it.
        await using var pipeline = new PipelineHarness(Local(2026, 2, 17, 12));
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17), completed: Local(2026, 2, 13, 17));
        AddSprintFortyOneWithFourAssignees(pipeline.Jira);
        Delivered(pipeline.Jira, 42, "FAKE-11", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-12", points: 3, People.Two, days: 2);
        foreach (var (person, number) in People.Team.Append(People.Six).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-2{number}", Local(2026, 1, 19, 9)).AssignedTo(person).InSprints(42);
            pipeline.Jira.AddIssue($"FAKE-3{number}", Local(2026, 2, 2, 9)).AssignedTo(person).Estimated(3).InSprints(43)
                .Moved(Local(2026, 2, 3, 9), Workflow.Development)
                .Moved(Local(2026, 2, 5, 9), Workflow.Done);
        }

        pipeline.Config["baselineSprintIds"] = new JsonArray("42", "41");

        var exit = await pipeline.RunAsync("run", "--sprint", "43");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Baseline sprints 41 and 42 each have fewer than five assignees, or fewer than three among their delivered issues, so every baseline figure is withheld (baselineSprintIds in the configuration file).\n");
        var report = await pipeline.ReadReportAsync(43);
        await Assert.That(report).Contains(CycleTimeHeader + "| 3 | 6 | 2.0 | 2.0 | 2.0 | 2.0 | suppressed | suppressed |\n");
        await Assert.That(report).Contains("- Baseline sprints 41 and 42 each have their delivered count withheld, so every baseline figure in this report reads \"suppressed\".\n");
    }

    [Test]
    public async Task A_baseline_sprint_jira_gives_no_start_date_has_no_sprint_window_and_is_refused_with_a_message_of_its_own()
    {
        // B8 of the final review: such a sprint was reported as having "fewer than five assignees".
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddClosedSprintWithoutStart(43, "Fake Sprint 43", completed: Local(2026, 2, 2, 9));
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("42", "43");

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file lists 43, which has no sprint window: Jira gives it no start date, or no completion time and no planned end, so nothing says when it ran. "
            + "A sprint without a window has no figures and cannot be a baseline sprint: take it out of the list and run again.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();
    }

    [Test]
    public async Task Two_baseline_lists_that_differ_by_one_sprint_never_show_two_figures_whose_difference_is_that_sprints_suppressed_row()
    {
        // The list is meant to be fixed, but it is a setting, and reports made under two lists can be laid side
        // by side. Sprint 41 has one assignee in bucket 3 and in Review.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);

        pipeline.Config["baselineSprintIds"] = new JsonArray("42");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var underSprint42Alone = await pipeline.ReadReportAsync(42);

        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var underBothSprints = await pipeline.ReadReportAsync(42);

        await Assert.That(underSprint42Alone).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | 3.0 | 4.0 |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | 1.5 |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | 2.0 |\n" + AfterTheTables);

        // Adding sprint 41 changes a baseline figure only where sprint 41's own row is shown.
        await Assert.That(underBothSprints).Contains(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | suppressed | suppressed |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 3.0 | 3.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | 2.0 |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | suppressed |\n" + AfterTheTables);
    }

    [Test]
    public async Task A_report_is_refused_when_the_baseline_list_changed_since_derive_so_it_never_shows_another_lists_baseline()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("42");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var before = await pipeline.ReadReportAsync(42);

        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file changed since the derived data was built. Run derive first.\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).IsEqualTo(before);
    }

    [Test]
    [Arguments("""["Fake Sprint 41"]""")]
    [Arguments("""["41", ""]""")]
    [Arguments("""["41", null]""")]
    [Arguments("""["4 1"]""")]
    [Arguments("""["-41"]""")]
    public async Task A_baseline_sprint_id_that_is_not_digits_is_refused_before_anything_is_fetched(string ids)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = JsonNode.Parse(ids);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: baselineSprintIds: every entry must be a Jira sprint ID (digits only), and one is not.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }

    [Test]
    public async Task Baseline_sprint_ids_are_trimmed_and_one_listed_twice_counts_once()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray(" 41 ", "41");
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(CycleTimeHeader + "| 3 | 5 | 3.0 | 5.0 | 3.0 | 7.0 | 3.0 | 5.0 |\n");
    }

    [Test]
    [Arguments("99", "99")]
    [Arguments("77", "77")]
    [Arguments("41,99,98", "98, 99")]
    [Arguments("41,43", "43")]
    [Arguments("44", "44")]
    public async Task A_baseline_sprint_id_that_is_not_a_completed_sprint_of_the_configured_board_stops_derive_naming_the_setting_and_the_id(string listed, string refused)
    {
        // Sprint 77 was created on another board; Jira lists it on this one, and it is not this board's sprint.
        // Sprint 43 is running and sprint 44 has not started: a baseline is fixed, so it is drawn from completed sprints.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17));
        pipeline.Jira.AddFutureSprint(44, "Fake Sprint 44");
        pipeline.Jira.AddSprint(77, "Fake Other Team Sprint", Local(2026, 1, 12, 9), plannedEnd: Local(2026, 1, 26, 9), completed: Local(2026, 1, 26, 9), originBoardId: 499999);
        pipeline.Jira.AddTeam(41);
        await pipeline.RunAllAsync(["backfill"]);
        var before = await pipeline.ReadStoreContentAsync();
        pipeline.Config["baselineSprintIds"] = new JsonArray([.. listed.Split(',').Select(id => (JsonNode)id)]);

        var exit = await pipeline.RunAsync("derive");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"baselineSprintIds in the configuration file lists {refused}, and the store holds no completed sprint with such an ID. "
            + "The baseline is drawn from completed sprints only. Correct the list, or run backfill if the sprint has completed since, and run again.\n");
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments("99")]
    [Arguments("43")]
    public async Task A_baseline_sprint_id_that_is_not_a_completed_sprint_of_the_configured_board_stops_report_too(string listed)
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17));
        pipeline.Jira.AddTeam(41);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Config["baselineSprintIds"] = new JsonArray(listed);

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"baselineSprintIds in the configuration file lists {listed}, and the store holds no completed sprint with such an ID. "
            + "The baseline is drawn from completed sprints only. Correct the list, or run backfill if the sprint has completed since, and run again.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
    }

    /// <summary>
    /// Sprint 41 with a team of six: one person's 3-point issue, a day in Development and five in Review (a
    /// cycle time of 6), and five people's 5-point issues, three days each. Its own report withholds bucket 3 and Review.
    /// </summary>
    private static void AddSprintWithOneAssigneeInBucketThreeAndInReview(FakeJira jira)
    {
        Delivered(jira, 41, "FAKE-1", points: 3, People.Four, days: 1, reviewDays: 5);
        foreach (var (person, number) in new[] { (People.One, 2), (People.Two, 3), (People.Three, 4), (People.Five, 5), (People.Six, 6) })
        {
            Delivered(jira, 41, $"FAKE-{number}", points: 5, person, days: 3);
        }
    }

    /// <summary>Sprint 41 with four people, each with a 3-point issue, 1 to 4 days: enough for the bucket, not for the sprint.</summary>
    private static void AddSprintFortyOneWithFourAssignees(FakeJira jira)
    {
        foreach (var (person, days) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Delivered(jira, 41, $"FAKE-{days}", points: 3, person, days);
        }
    }

    private static Task<IReadOnlyList<(string StatusName, long Suppressed, double? MedianBusinessDays)>> ReadBaselineTimeInStatusAsync(PipelineHarness pipeline) =>
        pipeline.QueryAsync<(string StatusName, long Suppressed, double? MedianBusinessDays)>(
            "SELECT status_name, suppressed, median_business_days FROM baseline_time_in_status ORDER BY board_position;");

    private sealed class BaselineCycleTimeRow
    {
        public double? PointBucket { get; init; }
        public long Suppressed { get; init; } = -1;
        public double? MedianBusinessDays { get; init; }
        public double? P85BusinessDays { get; init; }
        public double? MedianCalendarDays { get; init; }
        public double? P85CalendarDays { get; init; }
    }
}
