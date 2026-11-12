using System.Text.Json.Nodes;
using Telltale.Storage.Sqlite;
using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The baseline (GLOSSARY): figures pooled over the sprints listed in <c>baselineSprintIds</c>, shown beside each
/// sprint's own. Sprint 41 runs from Monday 5 to Friday 16 January 2026 and sprint 42 from Monday 19 to Friday 30 January.
/// </summary>
public class BaselineTests
{
    private const string CycleTimeHeader =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) | Baseline median (business days) | Baseline P85 (business days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n";

    private const string TimeInStatusHeader =
        "\n## Time in status\n\n"
        + "| Status | Issues | Median (business days) | Baseline median (business days) |\n| --- | ---: | ---: | ---: |\n";

    [Test]
    public async Task With_baseline_sprints_configured_both_tables_show_figures_pooled_over_all_issues_of_those_sprints()
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
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
        await Assert.That(await pipeline.ReadReportAsync(42)).EndsWith(
            CycleTimeHeader
            + "| 3 | 3 | 7.0 | 8.0 | 9.0 | 10.0 | 4.5 | 7.0 |\n"
            + "| 5 | 3 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 1.0 |\n"
            + "| Fake Development | 6 | 3.5 | 3.0 |\n");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task With_no_baseline_configured_no_baseline_columns_appear_and_the_baseline_views_are_empty(bool settingPresentAndEmpty)
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
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
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            "\n## Cycle time by point bucket\n\n"
            + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
            + "| --- | ---: | ---: | ---: | ---: | ---: |\n"
            + "| 3 | 5 | 3.0 | 5.0 | 3.0 | 7.0 |\n"
            + "\n## Time in status\n\n"
            + "| Status | Issues | Median (business days) |\n| --- | ---: | ---: |\n"
            + "| Fake New | 5 | 1.0 |\n"
            + "| Fake Development | 5 | 3.0 |\n");

        await Assert.That(await pipeline.QueryAsync<string>("SELECT sprint_id FROM baseline_sprints;")).IsEmpty();
        await Assert.That(await pipeline.QueryAsync<long>("SELECT suppressed FROM baseline_cycle_time_per_bucket;")).IsEmpty();
        await Assert.That(await pipeline.QueryAsync<long>("SELECT suppressed FROM baseline_time_in_status;")).IsEmpty();
    }

    [Test]
    public async Task The_sprint_being_reported_may_be_the_only_baseline_sprint_and_then_reads_against_its_own_figures()
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            CycleTimeHeader
            + "| 3 | 5 | 3.0 | 5.0 | 3.0 | 7.0 | 3.0 | 5.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 5 | 1.0 | 1.0 |\n"
            + "| Fake Development | 5 | 3.0 | 3.0 |\n");
    }

    [Test]
    public async Task A_query_over_the_views_reads_the_three_baseline_views()
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var store = new SqliteStore(pipeline.DatabasePath);

        var rows = await store.QueryViewsAsync<(long Sprints, long Buckets, long Statuses)>(
            """
            SELECT (SELECT COUNT(*) FROM baseline_sprints),
                   (SELECT COUNT(*) FROM baseline_cycle_time_per_bucket),
                   (SELECT COUNT(*) FROM baseline_time_in_status);
            """);

        await Assert.That(rows).IsEquivalentTo(new[] { (1L, 1L, 2L) });
    }

    [Test]
    [Arguments("SELECT sprint_id FROM baseline_sprint;")]
    [Arguments("SELECT COUNT(*) FROM baseline_sprint;")]
    [Arguments("SELECT s.sprint_id FROM baseline_sprints s JOIN baseline_sprint t ON t.sprint_id = s.sprint_id;")]
    public async Task A_query_over_the_views_cannot_read_the_derived_table_of_baseline_sprints(string sql)
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        pipeline.Jira.AddIdleTeam(41);
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await store.QueryViewsAsync<string>(sql))
            .Throws<InvalidOperationException>().WithMessageContaining("reads only the metric views");
    }

    [Test]
    public async Task Unestimated_issues_have_a_baseline_row_of_their_own()
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
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
        AddSprints(pipeline.Jira);
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
        await Assert.That(await pipeline.ReadReportAsync(42)).EndsWith(
            CycleTimeHeader
            + "| 3 | 3 | 1.0 | 1.0 | 1.0 | 1.0 | 3.0 | 5.0 |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | suppressed | suppressed |\n"
            + "| 8 | 3 | 3.0 | 3.0 | 3.0 | 3.0 | none | none |\n"
            + TimeInStatusHeader
            + "| Fake New | 9 | 1.0 | 1.0 |\n"
            + "| Fake Development | 9 | 2.0 | 1.5 |\n"
            + "| Fake Review | 3 | 1.0 | none |\n");

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
        AddSprints(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "42");

        // The pooled group passes on its own: four assignees in bucket 3 and in Review, and a team of six.
        // Bucket 5 pooled is 2, 2, 2, 3, 3, 3, 3, 3 and Development is 1, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 3.
        await Assert.That(await pipeline.ReadReportAsync(42)).EndsWith(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | suppressed | suppressed |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 3.0 | 3.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 2.0 |\n"
            + "| Fake Review | 3 | 2.0 | suppressed |\n");

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

    // Replaced (owner decision D47): a test here asserted that a baseline sprint short of five assignees
    // withheld the baseline rows it had an issue in. The review showed that the pattern of withheld, shown
    // and absent rows gave back which buckets and statuses of that sprint were not empty, which the sprint's
    // own report withholds. Such a sprint is now refused as a baseline sprint.
    [Test]
    [Arguments("41,42", "lists 41, which has fewer than five assignees. A sprint with fewer than five assignees cannot be a baseline sprint: take it out of the list and run again.")]
    [Arguments("41", "lists 41, which has fewer than five assignees. A sprint with fewer than five assignees cannot be a baseline sprint: take it out of the list and run again.")]
    [Arguments("41,42,43", "lists 41, 43, which each have fewer than five assignees. A sprint with fewer than five assignees cannot be a baseline sprint: take them out of the list and run again.")]
    public async Task A_sprint_short_of_five_assignees_cannot_be_a_baseline_sprint_and_derive_refuses_the_list_naming_it(string listed, string refusal)
    {
        // Sprint 41 has four people, sprint 42 six, and sprint 43 completed with no issue at all. Pooled, 41
        // and 42 have a team of six.
        await using var pipeline = new PipelineHarness(Local(2026, 2, 17, 12));
        AddSprints(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17), completed: Local(2026, 2, 13, 17));
        AddSprintFortyOneWithFourAssignees(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var before = await pipeline.ReadStoreContentAsync();
        pipeline.Config["baselineSprintIds"] = new JsonArray([.. listed.Split(',').Select(id => (JsonNode)id)]);

        var exit = await pipeline.RunAsync("derive");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"baselineSprintIds in the configuration file {refusal}\n");

        // Nothing was rebuilt: the store is as the derive without a baseline left it.
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task A_run_whose_baseline_holds_a_sprint_short_of_five_assignees_writes_no_report()
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
        AddSprintFortyOneWithFourAssignees(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file lists 41, which has fewer than five assignees. "
            + "A sprint with fewer than five assignees cannot be a baseline sprint: take it out of the list and run again.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();
    }

    [Test]
    public async Task A_report_refuses_a_baseline_list_that_gained_a_sprint_short_of_five_assignees_naming_it()
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
        AddSprintFortyOneWithFourAssignees(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("42");
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file lists 41, which has fewer than five assignees. "
            + "A sprint with fewer than five assignees cannot be a baseline sprint: take it out of the list and run again.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();
    }

    [Test]
    public async Task Two_baseline_lists_that_differ_by_one_sprint_never_show_two_figures_whose_difference_is_that_sprints_suppressed_row()
    {
        // The list is meant to be fixed, but it is a setting, and reports made under two lists can be laid side
        // by side. Sprint 41 has one assignee in bucket 3 and in Review.
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
        AddSprintWithOneAssigneeInBucketThreeAndInReview(pipeline.Jira);
        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);

        pipeline.Config["baselineSprintIds"] = new JsonArray("42");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var underSprint42Alone = await pipeline.ReadReportAsync(42);

        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var underBothSprints = await pipeline.ReadReportAsync(42);

        await Assert.That(underSprint42Alone).EndsWith(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | 3.0 | 4.0 |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 1.5 |\n"
            + "| Fake Review | 3 | 2.0 | 2.0 |\n");

        // Adding sprint 41 changes a baseline figure only where sprint 41's own row is shown.
        await Assert.That(underBothSprints).EndsWith(
            CycleTimeHeader
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | suppressed | suppressed |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 3.0 | 3.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 2.0 |\n"
            + "| Fake Review | 3 | 2.0 | suppressed |\n");
    }

    [Test]
    public async Task A_report_is_refused_when_the_baseline_list_changed_since_derive_so_it_never_shows_another_lists_baseline()
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
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
        AddSprints(pipeline.Jira);
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
        AddSprints(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray(" 41 ", "41");
        foreach (var (person, days) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.QueryAsync<string>("SELECT sprint_id FROM baseline_sprints;")).IsEquivalentTo(new[] { "41" });
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
        AddSprints(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17));
        pipeline.Jira.AddFutureSprint(44, "Fake Sprint 44");
        pipeline.Jira.AddSprint(77, "Fake Other Team Sprint", Local(2026, 1, 12, 9), plannedEnd: Local(2026, 1, 26, 9), completed: Local(2026, 1, 26, 9), originBoardId: 499999);
        pipeline.Jira.AddIdleTeam(41);
        await pipeline.RunAllAsync(["backfill"]);
        pipeline.Config["baselineSprintIds"] = new JsonArray([.. listed.Split(',').Select(id => (JsonNode)id)]);

        var exit = await pipeline.RunAsync("derive");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"baselineSprintIds in the configuration file lists {refused}, and the store holds no completed sprint of board 400001 with such an ID. "
            + "The baseline is drawn from completed sprints only. Correct the list, or run backfill if the sprint has completed since, and run again.\n");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT count(*) FROM sqlite_master WHERE name = 'baseline_sprints';")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    [Arguments("99")]
    [Arguments("43")]
    public async Task A_baseline_sprint_id_that_is_not_a_completed_sprint_of_the_configured_board_stops_report_too(string listed)
    {
        await using var pipeline = new PipelineHarness();
        AddSprints(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17));
        pipeline.Jira.AddIdleTeam(41);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Config["baselineSprintIds"] = new JsonArray(listed);

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"baselineSprintIds in the configuration file lists {listed}, and the store holds no completed sprint of board 400001 with such an ID. "
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

    /// <summary>
    /// Sprint 42 with a team of six: three people's 3-point issues, a day in Development and 1, 2 and 3 in Review
    /// (cycle times 2, 3 and 4), and three more people's 5-point issues, two days each.
    /// </summary>
    private static void AddSprintFortyTwoWithThreeAssigneesInEveryRow(FakeJira jira)
    {
        Delivered(jira, 42, "FAKE-11", points: 3, People.One, days: 1, reviewDays: 1);
        Delivered(jira, 42, "FAKE-12", points: 3, People.Two, days: 1, reviewDays: 2);
        Delivered(jira, 42, "FAKE-13", points: 3, People.Three, days: 1, reviewDays: 3);
        foreach (var (person, number) in new[] { (People.Four, 21), (People.Five, 22), (People.Six, 23) })
        {
            Delivered(jira, 42, $"FAKE-{number}", points: 5, person, days: 2);
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

    private static void AddSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 16, 17), completed: Local(2026, 1, 16, 17));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 9), plannedEnd: Local(2026, 1, 30, 17), completed: Local(2026, 1, 30, 17));
    }

    /// <summary>
    /// An issue of sprint 41 or 42, created as the sprint starts (Monday 09:00) and in development from Tuesday
    /// 09:00 for <paramref name="days"/> business days; then, with <paramref name="reviewDays"/>, in Review for
    /// that many; then done. Null <paramref name="points"/> leaves it unestimated. It spends a day in New, and its cycle time is the two together (at most eight
    /// business days, so it is done inside the sprint).
    /// </summary>
    private static FakeIssue Delivered(FakeJira jira, long sprintId, string key, double? points, FakePerson? assignee, int days, int reviewDays = 0)
    {
        var monday = sprintId == 41 ? Local(2026, 1, 5, 9) : Local(2026, 1, 19, 9);
        var tuesday = monday.AddDays(1);

        // Tuesday plus four business days is the next Monday: a weekend lies between.
        DateTimeOffset After(int businessDays) => tuesday.AddDays(businessDays < 4 ? businessDays : businessDays + 2);

        var issue = jira.AddIssue(key, monday).InSprints(sprintId).Moved(tuesday, Workflow.Development);
        if (points is { } estimate)
        {
            issue.Estimated(estimate);
        }

        if (reviewDays > 0)
        {
            issue.Moved(After(days), Workflow.Review);
        }

        issue.Moved(After(days + reviewDays), Workflow.Done);
        return assignee is null ? issue : issue.AssignedTo(assignee);
    }
}
