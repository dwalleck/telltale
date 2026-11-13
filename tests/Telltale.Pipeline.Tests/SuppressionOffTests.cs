using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Pipeline.Tests.Harness.PrivacyScan;
using static Telltale.Pipeline.Tests.Harness.ReportText;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Suppression off: both numbers of <c>minimumGroupSize</c> are zero (owner decision D65), and nothing is
/// withheld for the size of the group behind it. Each test here is a scenario in which the default numbers
/// withhold a figure (<see cref="SuppressionTests"/>, <see cref="BaselineTests"/>,
/// <see cref="MethodNoteAndFooterTests"/> say which), run with suppression off: the figure is shown, with its
/// value, in the report and through <c>query</c>. What the setting does not touch (owner decision D68) is at
/// the end. Sprint 41 runs from Monday 5 to Friday 16 January 2026, sprint 42 from Monday 19 to Friday
/// 30 January and sprint 43 from Monday 2 to Friday 13 February.
/// </summary>
public class SuppressionOffTests
{
    private const string DeliveredSql = "SELECT sprint_id, delivered_count, suppressed, team_suppressed FROM delivered_per_sprint ORDER BY sprint_id;";

    private const string CycleTimeSql =
        "SELECT sprint_id, point_bucket, issue_count, median_business_days, p85_business_days, median_calendar_days, p85_calendar_days, suppressed FROM cycle_time_per_sprint_and_bucket ORDER BY sprint_id, point_bucket IS NULL, point_bucket;";

    private const string TimeInStatusSql =
        "SELECT sprint_id, status_name, issue_count, median_business_days, total_business_days, suppressed FROM time_in_status_per_sprint ORDER BY sprint_id, board_position;";

    /// <summary>The three baseline views in one result: the buckets by estimate, then the statuses in board order, then the baseline sprints by ID.</summary>
    private const string BaselineSql =
        "SELECT baseline_row, median_business_days, p85_business_days, median_calendar_days, p85_calendar_days, suppressed FROM ("
        + "SELECT 1 AS section, point_bucket AS position, 'bucket ' || CAST(point_bucket AS INTEGER) AS baseline_row, median_business_days, p85_business_days, median_calendar_days, p85_calendar_days, suppressed FROM baseline_cycle_time_per_bucket "
        + "UNION ALL SELECT 2, board_position, status_name, median_business_days, NULL, NULL, NULL, suppressed FROM baseline_time_in_status "
        + "UNION ALL SELECT 3, CAST(sprint_id AS INTEGER), 'sprint ' || sprint_id, NULL, NULL, NULL, NULL, suppressed FROM baseline_sprint"
        + ") ORDER BY section, position;";

    /// <summary>
    /// The metric views: what <c>query</c> may read. A view added later is added here, and to what
    /// <see cref="The_metric_views_are_these_nine_with_suppression_off_and_none_holds_a_person"/> says of each.
    /// </summary>
    private static readonly string[] MetricViews =
    [
        "baseline_cycle_time_per_bucket", "baseline_sprint", "baseline_time_in_status", "cycle_time_per_sprint_and_bucket", "data_quality",
        "delivered_per_sprint", "minimum_group_size", "sprint_team", "time_in_status_per_sprint",
    ];

    private const string DataQualitySql = "SELECT anomaly_issue_count, delivered_between_sprints_count, suppressed FROM data_quality;";

    [Test]
    public async Task A_point_bucket_with_two_assignees_shows_its_median_and_p85()
    {
        // Bucket 3: three people, two days each. Bucket 5: three issues of two people, of 1, 2 and 3 days.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.One, days: 2);
        Delivered(pipeline.Jira, 41, "FAKE-2", points: 3, People.Two, days: 2);
        Delivered(pipeline.Jira, 41, "FAKE-3", points: 3, People.Three, days: 2);
        Delivered(pipeline.Jira, 41, "FAKE-4", points: 5, People.Four, days: 1);
        Delivered(pipeline.Jira, 41, "FAKE-5", points: 5, People.Four, days: 3);
        Delivered(pipeline.Jira, 41, "FAKE-6", points: 5, People.Five, days: 2);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 6\n"
            + CycleTimeHeader
            + "| 3 | 3 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 3 | 2.0 | 3.0 | 2.0 | 3.0 |\n"
            + "\n## Time in status\n");
        await Assert.That(await QueryAsync(pipeline, CycleTimeSql)).IsEqualTo(
            """
            | sprint_id | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- | --- | --- |
            | 41 | 3 | 3 | 2 | 2 | 2 | 2 | 0 |
            | 41 | 5 | 3 | 2 | 3 | 2 | 3 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_time_in_status_row_with_two_assignees_shows_its_median_and_total()
    {
        // Three people's issues spend two days in Development. Four more spend one day there and then one or
        // two in Review, and only two people have those: one of the four is unassigned.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.Three, days: 2);
        Delivered(pipeline.Jira, 41, "FAKE-2", points: 3, People.Four, days: 2);
        Delivered(pipeline.Jira, 41, "FAKE-3", points: 3, People.Five, days: 2);
        Delivered(pipeline.Jira, 41, "FAKE-4", points: 3, People.One, days: 1, reviewDays: 1);
        Delivered(pipeline.Jira, 41, "FAKE-5", points: 3, People.One, days: 1, reviewDays: 2);
        Delivered(pipeline.Jira, 41, "FAKE-6", points: 3, People.Two, days: 1, reviewDays: 2);
        Delivered(pipeline.Jira, 41, "FAKE-7", points: 3, assignee: null, days: 1, reviewDays: 1);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        // Review is 1, 1, 2, 2: a median of 1.5 and a total of 6.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            TimeInStatusHeader
            + "| Fake New | 7 | 1.0 | 7.0 |\n"
            + "| Fake Development | 7 | 1.0 | 10.0 |\n"
            + "| Fake Review | 4 | 1.5 | 6.0 |\n"
            + MethodHeading);
        await Assert.That(await QueryAsync(pipeline, TimeInStatusSql)).IsEqualTo(
            """
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | Fake New | 7 | 1 | 7 | 0 |
            | 41 | Fake Development | 7 | 1 | 10 | 0 |
            | 41 | Fake Review | 4 | 1.5 | 6 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_delivered_count_with_two_deliverers_is_shown_and_the_sprints_cycle_time_table_has_its_bucket_rows()
    {
        // Owner decisions D49 and D54 at the default numbers: a team of five, of whom two delivered, has its
        // delivered count withheld and its cycle time table with it. Here two people delivered four issues.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            StillInDevelopment(pipeline.Jira, 41, $"FAKE-9{number}", person);
        }

        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 41, "FAKE-2", points: 3, People.One, days: 3);
        Delivered(pipeline.Jira, 41, "FAKE-3", points: 5, People.Two, days: 2);
        Delivered(pipeline.Jira, 41, "FAKE-4", points: null, People.Two, days: 1);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 4\n"
            + CycleTimeHeader
            + "| 3 | 2 | 2.0 | 3.0 | 2.0 | 3.0 |\n"
            + "| 5 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| unestimated | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + "\n## Time in status\n");
        await Assert.That(await QueryAsync(pipeline, DeliveredSql)).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 41 | 4 | 0 | 0 |
            | 42 | 0 | 0 | 0 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, CycleTimeSql)).IsEqualTo(
            """
            | sprint_id | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- | --- | --- |
            | 41 | 3 | 2 | 2 | 3 | 2 | 3 | 0 |
            | 41 | 5 | 1 | 2 | 2 | 2 | 2 | 0 |
            | 41 | NULL | 1 | 1 | 1 | 1 | 1 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_baseline_that_includes_a_sprint_under_five_assignees_is_shown_and_no_sprint_is_named()
    {
        // Owner decision D50 at the default numbers: sprint 41 has four people, so every baseline figure is
        // withheld and the console names it. Its 3-point issues take 1 to 4 days; sprint 42's take 2, 3 and 4.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        foreach (var (person, days) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{days}", points: 3, person, days);
        }

        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Pooled: bucket 3 is 1, 2, 2, 3, 3, 4, 4 business days (the sixth of seven is the P85), and 1, 2, 2,
        // 3, 3, 6, 6 calendar days. Development is 1, 1, 1, 1, 2, 2, 2, 2, 3, 4.
        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains(
            CycleTimeHeaderWithBaseline
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | 3.0 | 4.0 |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 6 | 1.0 | 6.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | 2.0 |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | 2.0 |\n"
            + MethodHeading);
        await Assert.That(report).DoesNotContain("\n- Baseline sprint");
        await Assert.That(await QueryAsync(pipeline, BaselineSql)).IsEqualTo(
            """
            | baseline_row | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | bucket 3 | 3 | 4 | 3 | 6 | 0 |
            | bucket 5 | 2 | 2 | 2 | 2 | 0 |
            | Fake New | 1 | NULL | NULL | NULL | 0 |
            | Fake Development | 2 | NULL | NULL | NULL | 0 |
            | Fake Review | 2 | NULL | NULL | NULL | 0 |
            | sprint 41 | NULL | NULL | NULL | NULL | 0 |
            | sprint 42 | NULL | NULL | NULL | NULL | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_baseline_that_includes_a_sprint_whose_delivered_count_was_withheld_is_shown_and_no_sprint_is_named()
    {
        // Owner decision D56 at the default numbers: sprint 41 has a team of six, two of whom delivered, so its
        // delivered count is withheld and with it every baseline figure. One delivered a 3-point issue in a
        // day and the other an unestimated one in two; six issues stay in Development to the sprint's end.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 41, "FAKE-2", points: null, People.Two, days: 2);
        foreach (var (person, number) in People.Team.Append(People.Six).Select((person, index) => (person, index + 3)))
        {
            StillInDevelopment(pipeline.Jira, 41, $"FAKE-{number}", person);
        }

        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        await pipeline.RunAllAsync(["run", "--sprint", "42"], ["report", "--sprint", "41"]);

        // Pooled: bucket 3 is 1, 2, 3, 4. Development is 1, 1, 1, 1, 2, 2, 2, 2 and six of eight days and
        // eight hours (Tuesday 6 January 09:00 to Friday the 16th 17:00, less a weekend): the middle two are 2.
        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        var other = await pipeline.ReadReportAsync(42);
        await Assert.That(other).Contains(
            CycleTimeHeaderWithBaseline
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | 2.5 | 4.0 |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 6 | 1.0 | 6.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | 2.0 |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | 2.0 |\n"
            + MethodHeading);
        await Assert.That(other).DoesNotContain("\n- Baseline sprint");

        // Its own report: the count, its bucket rows, and a baseline figure beside each row.
        var own = await pipeline.ReadReportAsync(41);
        await Assert.That(own).Contains(
            "\nDelivered issues: 2\n"
            + CycleTimeHeaderWithBaseline
            + "| 3 | 1 | 1.0 | 1.0 | 1.0 | 1.0 | 2.5 | 4.0 |\n"
            + "| unestimated | 1 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 8 | 1.0 | 8.0 | 1.0 |\n"
            + "| Fake Development | 8 | 8.3 | 53.0 | 2.0 |\n"
            + MethodHeading);
        await Assert.That(own).DoesNotContain("\n- Baseline sprint");
        await Assert.That(await QueryAsync(
            pipeline,
            "SELECT (SELECT COUNT(*) FROM baseline_cycle_time_per_bucket WHERE suppressed = 0) AS cycle_time_rows_shown, (SELECT COUNT(*) FROM baseline_time_in_status WHERE suppressed = 0) AS status_rows_shown, "
            + "(SELECT group_concat(sprint_id || '=' || suppressed, ' ') FROM (SELECT sprint_id, suppressed FROM baseline_sprint ORDER BY sprint_id)) AS baseline_sprints;")).IsEqualTo(
            """
            | cycle_time_rows_shown | status_rows_shown | baseline_sprints |
            | --- | --- | --- |
            | 3 | 3 | 41=0 42=0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_pooled_baseline_row_whose_row_one_baseline_sprint_had_suppressed_is_shown()
    {
        // Owner decision D44 at the default numbers: sprint 41 has one person in bucket 3 (a cycle time of 6)
        // and in Review (5 days), so the pooled bucket 3 and the pooled Review are withheld.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.Four, days: 1, reviewDays: 5);
        foreach (var (person, number) in new[] { (People.One, 2), (People.Two, 3), (People.Three, 4), (People.Five, 5), (People.Six, 6) })
        {
            Delivered(pipeline.Jira, 41, $"FAKE-{number}", points: 5, person, days: 3);
        }

        AddSprintFortyTwoWithThreeAssigneesInEveryRow(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Pooled: bucket 3 is 2, 3, 4, 6 business days (a median of 3.5; the fourth of four is the P85) and
        // 2, 3, 6, 8 calendar days; Review is 1, 2, 3, 5.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeaderWithBaseline
            + "| 3 | 3 | 3.0 | 4.0 | 3.0 | 6.0 | 3.5 | 6.0 |\n"
            + "| 5 | 3 | 2.0 | 2.0 | 2.0 | 2.0 | 3.0 | 3.0 |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 6 | 1.0 | 6.0 | 1.0 |\n"
            + "| Fake Development | 6 | 1.5 | 9.0 | 2.0 |\n"
            + "| Fake Review | 3 | 2.0 | 6.0 | 2.5 |\n"
            + MethodHeading);
        await Assert.That(await QueryAsync(pipeline, BaselineSql)).IsEqualTo(
            """
            | baseline_row | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | bucket 3 | 3.5 | 6 | 4.5 | 8 | 0 |
            | bucket 5 | 3 | 3 | 3 | 3 | 0 |
            | Fake New | 1 | NULL | NULL | NULL | 0 |
            | Fake Development | 2 | NULL | NULL | NULL | 0 |
            | Fake Review | 2.5 | NULL | NULL | NULL | 0 |
            | sprint 41 | NULL | NULL | NULL | NULL | 0 |
            | sprint 42 | NULL | NULL | NULL | NULL | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task The_footers_two_counts_are_shown_with_two_assignees_behind_them()
    {
        // A team of five in the store. Two people have the three issues with an anomaly (created in Done) and
        // the three delivered between the sprints, on Saturday 17 January.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        foreach (var (person, number) in new[] { (People.One, 1), (People.Two, 2), (People.One, 3) })
        {
            pipeline.Jira.AddIssue($"FAKE-1{number}", Local(2026, 1, 8, 9), createdIn: Workflow.Done).AssignedTo(person);
            pipeline.Jira.AddIssue($"FAKE-2{number}", Local(2026, 1, 6, 9)).AssignedTo(person).Moved(Local(2026, 1, 17, 10), Workflow.Done);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(Footer(anomalies: "3", betweenSprints: "3"));
        await Assert.That(await QueryAsync(pipeline, DataQualitySql)).IsEqualTo(DataQuality(anomalies: "3", betweenSprints: "3"));
    }

    [Test]
    public async Task The_footers_two_counts_are_shown_when_the_whole_store_has_four_assignees()
    {
        // One issue with an anomaly and one delivered between the sprints, among four people's five issues.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 8, 9), createdIn: Workflow.Done).AssignedTo(People.One);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).AssignedTo(People.Two).Moved(Local(2026, 1, 17, 10), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9)).AssignedTo(People.Three);
        pipeline.Jira.AddIssue("FAKE-4", Local(2026, 1, 6, 9)).AssignedTo(People.Four);
        pipeline.Jira.AddIssue("FAKE-5", Local(2026, 1, 6, 9));

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(Footer(anomalies: "1", betweenSprints: "1"));
        await Assert.That(await QueryAsync(pipeline, DataQualitySql)).IsEqualTo(DataQuality(anomalies: "1", betweenSprints: "1"));
    }

    [Test]
    public async Task A_footer_count_of_nothing_is_shown_as_0()
    {
        // No issue has an anomaly and none was delivered between the sprints: "none" is told apart from "withheld".
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(Footer(anomalies: "0", betweenSprints: "0"));
        await Assert.That(await QueryAsync(pipeline, DataQualitySql)).IsEqualTo(DataQuality(anomalies: "0", betweenSprints: "0"));
    }

    [Test]
    public async Task Rows_made_only_of_issues_with_no_assignee_show_their_figures()
    {
        // Nobody is assigned anything in the store: the sprint, each of its rows and the footer's counts have
        // no assignee behind them. One issue takes a day in Development and one in Review, the other three days.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, assignee: null, days: 1, reviewDays: 1);
        Delivered(pipeline.Jira, 41, "FAKE-2", points: 3, assignee: null, days: 3);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).StartsWith(
            "# Fake Sprint 41 (2026-01-05 to 2026-01-16)\n");
        await Assert.That(report).Contains(
            "\nDelivered issues: 2\n"
            + CycleTimeHeader
            + "| 3 | 2 | 2.5 | 3.0 | 2.5 | 3.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 2 | 1.0 | 2.0 |\n"
            + "| Fake Development | 2 | 2.0 | 4.0 |\n"
            + "| Fake Review | 1 | 1.0 | 1.0 |\n"
            + MethodHeading);
        await Assert.That(report).EndsWith(Footer(anomalies: "0", betweenSprints: "0"));
        await Assert.That(await QueryAsync(pipeline, CycleTimeSql)).IsEqualTo(
            """
            | sprint_id | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- | --- | --- |
            | 41 | 3 | 2 | 2.5 | 3 | 2.5 | 3 | 0 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, TimeInStatusSql)).IsEqualTo(
            """
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | Fake New | 2 | 1 | 2 | 0 |
            | 41 | Fake Development | 2 | 2 | 4 | 0 |
            | 41 | Fake Review | 1 | 1 | 1 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_store_that_trips_every_rule_at_once_gives_reports_without_the_word_suppressed_in_their_tables_or_footer_and_no_flag_in_any_view()
    {
        await using var byDefault = new PipelineHarness(Local(2026, 2, 17, 12));
        await using var off = SuppressionOff(Local(2026, 2, 17, 12));
        foreach (var pipeline in new[] { byDefault, off })
        {
            AddStoreThatTripsEveryRule(pipeline.Jira);
            pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42", "43");
            await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"], ["report", "--sprint", "43"]);
        }

        // What the default numbers withhold of this store (AddStoreThatTripsEveryRule says which rules those are).
        const string WithheldFooter = "- Issues with changelog anomalies, in the whole store: suppressed\n- Issues delivered between sprints, in the whole store: suppressed\n";
        await Assert.That(TablesAndFooter(await byDefault.ReadReportAsync(41))).Contains(
            "\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n\n## Time in status\n\nsuppressed\n");
        await Assert.That(TablesAndFooter(await byDefault.ReadReportAsync(42))).Contains(
            "\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n" + TimeInStatusHeaderWithBaseline + "| Fake New | 8 | 1.0 | 8.0 | suppressed |\n");
        var team = TablesAndFooter(await byDefault.ReadReportAsync(43));
        await Assert.That(team).Contains("\nDelivered issues: 5\n");
        await Assert.That(team).Contains("| 5 | 2 | suppressed | suppressed | suppressed | suppressed | suppressed | suppressed |\n");
        await Assert.That(team).Contains("| Fake Review | 1 | suppressed | suppressed | suppressed |\n");
        await Assert.That(team).EndsWith(WithheldFooter);
        await Assert.That(byDefault.Error.ToString()).Contains("Baseline sprints 41 and 42 each have fewer than five assignees");
        await Assert.That(await byDefault.ReadReportAsync(43)).Contains("\n- Baseline sprints 41 and 42 each have their delivered count withheld");

        // With suppression off, nothing of it is.
        foreach (var sprintId in new[] { 41, 42, 43 })
        {
            var report = await off.ReadReportAsync(sprintId);
            await Assert.That(TablesAndFooter(report)).DoesNotContain("suppressed");
            await Assert.That(TablesAndFooter(report)).EndsWith(
                "- Issues with changelog anomalies, in the whole store: 1\n- Issues delivered between sprints, in the whole store: 0\n");
            await Assert.That(report).DoesNotContain("\n- Baseline sprint");
        }

        await Assert.That(off.Error.ToString()).DoesNotContain("Baseline sprint");
        await Assert.That(off.Error.ToString()).DoesNotContain("withheld");

        // Every flag of every metric view, found in the store and not listed here, so that a view added later
        // is checked too: how many rows the view has, and in how many the flag is anything but 0 (the report
        // shows a figure only when it is exactly 0, so a null counts as set).
        var flags = await off.QueryAsync<(string View, string Flag)>(
            "SELECT o.name, c.name FROM derived_object o JOIN pragma_table_info(o.name) c WHERE o.kind = 'view' AND c.name LIKE '%suppressed%' ORDER BY o.name, c.name;");
        var flagged = string.Join(
            " UNION ALL ",
            flags.Select(flag => $"SELECT '{flag.View}.{flag.Flag}' AS flag, COUNT(*) AS row_count, COALESCE(SUM({flag.Flag} IS NOT 0), 0) AS flagged FROM {flag.View}"));
        await Assert.That(await QueryAsync(off, $"SELECT flag, row_count, flagged FROM ({flagged}) ORDER BY flag;")).IsEqualTo(
            """
            | flag | row_count | flagged |
            | --- | --- | --- |
            | baseline_cycle_time_per_bucket.suppressed | 3 | 0 |
            | baseline_sprint.suppressed | 3 | 0 |
            | baseline_time_in_status.suppressed | 3 | 0 |
            | cycle_time_per_sprint_and_bucket.suppressed | 7 | 0 |
            | data_quality.suppressed | 1 | 0 |
            | delivered_per_sprint.suppressed | 3 | 0 |
            | delivered_per_sprint.team_suppressed | 3 | 0 |
            | sprint_team.delivered_suppressed | 3 | 0 |
            | sprint_team.team_suppressed | 3 | 0 |
            | time_in_status_per_sprint.suppressed | 8 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_sprint_in_which_nothing_was_delivered_and_one_no_issue_belonged_to_say_so_as_they_do_with_a_team()
    {
        // No data is not a withheld figure: a bucket with no issue has no row, and an empty table is a
        // sentence. Sprint 41 has five issues that stay in Development; sprint 42 has no issue at all.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            StillInDevelopment(pipeline.Jira, 41, $"FAKE-{number}", person);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 0\n"
            + "\n## Cycle time by point bucket\n\nNo issue delivered in this sprint has a cycle time.\n"
            + TimeInStatusHeader
            + "| Fake New | 5 | 1.0 | 5.0 |\n"
            + "| Fake Development | 5 | 8.3 | 41.7 |\n"
            + MethodHeading);
        await Assert.That(await pipeline.ReadReportAsync(42)).StartsWith(
            "# Fake Sprint 42 (2026-01-19 to 2026-01-30)\n\n"
            + "Small-group suppression is off: a figure in this report may rest on the work of one or two people.\n\n"
            + "Delivered issues: 0\n"
            + "\n## Cycle time by point bucket\n\nNo issue delivered in this sprint has a cycle time.\n"
            + "\n## Time in status\n\nNo issue of this sprint spent time in a status that is not done.\n"
            + MethodHeading);
        await Assert.That(await QueryAsync(pipeline, CycleTimeSql)).IsEqualTo(
            """
            | sprint_id | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- | --- | --- |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, "SELECT sprint_id, status_name, issue_count FROM time_in_status_per_sprint ORDER BY sprint_id, board_position;")).IsEqualTo(
            """
            | sprint_id | status_name | issue_count |
            | --- | --- | --- |
            | 41 | Fake New | 5 |
            | 41 | Fake Development | 5 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_row_the_baseline_sprints_have_no_issue_in_still_reads_none()
    {
        // "none" is no data, not a withheld figure. The baseline sprint has one 3-point issue; the reported
        // sprint also has an 8-point issue that spent a day in Review.
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-2", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-3", points: 8, People.Two, days: 1, reviewDays: 1);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeaderWithBaseline
            + "| 3 | 1 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + "| 8 | 1 | 2.0 | 2.0 | 2.0 | 2.0 | none | none |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 2 | 1.0 | 2.0 | 1.0 |\n"
            + "| Fake Development | 2 | 1.0 | 2.0 | 1.0 |\n"
            + "| Fake Review | 1 | 1.0 | 1.0 | none |\n"
            + MethodHeading);

        // And through query: the baseline views have no row for bucket 8 or for Review, and withhold nothing.
        await Assert.That(await QueryAsync(pipeline, BaselineSql)).IsEqualTo(
            """
            | baseline_row | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | bucket 3 | 1 | 1 | 1 | 1 | 0 |
            | Fake New | 1 | NULL | NULL | NULL | 0 |
            | Fake Development | 1 | NULL | NULL | NULL | 0 |
            | sprint 41 | NULL | NULL | NULL | NULL | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task A_sprint_with_no_window_is_still_named_and_skipped_and_can_be_neither_reported_nor_a_baseline_sprint()
    {
        // Owner decision D51, whatever the numbers. Sprint 41 is closed and Jira gives it no end. This is also
        // why no baseline sprint can lack a row in sprint_team, which baseline_sprint would read as withheld
        // whatever the numbers are: derive refuses the list before it builds anything.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddClosedSprintWithoutCompletion(41, "Fake Sprint 41", Local(2026, 1, 5, 9));
        pipeline.Jira.AddSprint42(start: Local(2026, 1, 19, 9), plannedEnd: Local(2026, 1, 30, 17), completed: Local(2026, 1, 30, 17));
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-2", points: 3, People.Two, days: 2);

        var run = await pipeline.RunAsync("run", "--sprint", "42");
        var consoleOfRun = pipeline.Error.ToString().ReplaceLineEndings("\n");
        pipeline.Error.GetStringBuilder().Clear();
        var reportOfTheSprint = await pipeline.RunAsync("report", "--sprint", "41");
        var consoleOfReport = pipeline.Error.ToString().ReplaceLineEndings("\n");
        var sprints = await QueryAsync(pipeline, DeliveredSql);
        pipeline.Error.GetStringBuilder().Clear();
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        var runWithItAsBaseline = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(run).IsEqualTo(0);
        await Assert.That(consoleOfRun).IsEqualTo(
            "Sprint 41 is closed, but Jira gives it neither a completion time nor a planned end, so it has no sprint window: no delivery is counted in it, and it cannot be reported or be a baseline sprint.\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n" + CycleTimeHeader + "| 3 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n");
        await Assert.That(sprints).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 42 | 1 | 0 | 0 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(reportOfTheSprint).IsEqualTo(1);
        await Assert.That(consoleOfReport).IsEqualTo(
            "Sprint 41 is closed, but Jira gives it neither a completion time nor a planned end, so it has no sprint window and no figures. No report was written.\n");
        await Assert.That(runWithItAsBaseline).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "baselineSprintIds in the configuration file lists 41, which has no sprint window: Jira gives it no start date, or no completion time and no planned end, so nothing says when it ran. "
            + "A sprint without a window has no figures and cannot be a baseline sprint: take it out of the list and run again.\n");
    }

    [Test]
    [Arguments("99", "a sprint the store does not hold")]
    [Arguments("43", "a sprint that has not started")]
    [Arguments("44", "a sprint that is still running")]
    public async Task A_baseline_sprint_that_is_not_a_completed_sprint_of_the_store_is_refused_as_it_is_with_the_default_numbers(string listed, string what)
    {
        // With the test above, every way a baseline sprint could lack a row in sprint_team (which
        // baseline_sprint would read as withheld, whatever the numbers): derive refuses each list before it
        // builds anything, so the views never hold such a sprint. The message is the default numbers'
        // (BaselineTests).
        await using var pipeline = SuppressionOff();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddFutureSprint(43, "Fake Sprint 43", plannedStart: Local(2026, 2, 16, 9), plannedEnd: Local(2026, 2, 27, 17));
        pipeline.Jira.AddSprint(44, "Fake Sprint 44", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17));
        Delivered(pipeline.Jira, 41, "FAKE-1", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-2", points: 3, People.Two, days: 2);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", listed);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1).Because(what);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"baselineSprintIds in the configuration file lists {listed}, and the store holds no completed sprint with such an ID. "
            + "The baseline is drawn from completed sprints only. Correct the list, or run backfill if the sprint has completed since, and run again.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();

        // Nothing was derived, so query has no baseline to answer from and says what to run.
        pipeline.Error.GetStringBuilder().Clear();
        await Assert.That(await pipeline.RunAsync("query", "SELECT sprint_id FROM baseline_sprint;")).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("The store has no derived data yet. Run derive first.\n");
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("sprint_id");
    }

    [Test]
    public async Task No_account_id_name_email_or_summary_reaches_the_database_the_report_or_the_console_with_suppression_off()
    {
        // Owner decision D68. PrivacyTests' scenario and PrivacyTests' scan, with suppression off: two people
        // delivered, so the default numbers withhold every figure of the sprint, and here each is in the report
        // and in what query prints of every metric view. The scan reads standard output, where query prints.
        await using var pipeline = SuppressionOff();
        AddScenario(pipeline.Jira);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        foreach (var view in MetricViews)
        {
            await pipeline.RunAllAsync(["query", $"SELECT * FROM {view};"]);
        }

        // The cycle times are 2.25 and 4.08 business days (6.08 calendar days: a weekend lies in the second).
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 2\n" + CycleTimeHeader + "| unestimated | 2 | 3.2 | 4.1 | 4.2 | 6.1 |\n");
        var printed = pipeline.Out.ToString().ReplaceLineEndings("\n");
        await Assert.That(printed).Contains("| sprint_id | sprint_name | state | started | ended | planned_end | delivered_count | suppressed | team_suppressed |\n");
        await Assert.That(printed).Contains("| 41 | Fake Sprint 41 | closed | 2026-01-05T15:00:00.000Z | 2026-01-19T16:00:00.000Z | 2026-01-19T15:00:00.000Z | 2 | 0 | 0 |\n");
        await Assert.That(printed).Contains("| sprint_id | team_suppressed | delivered_suppressed |\n| --- | --- | --- |\n| 41 | 0 | 0 |\n");
        await AssertNothingForbiddenWasWrittenAsync(pipeline);
    }

    [Test]
    public async Task The_metric_views_are_these_nine_with_suppression_off_and_none_holds_a_person()
    {
        // Owner decision D68: turning suppression off adds no view, and no view is about a person. (That each
        // view has the same columns in either state is OptionalSuppressionTests'.)
        await using var pipeline = SuppressionOff(Local(2026, 2, 17, 12));
        AddStoreThatTripsEveryRule(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42", "43");
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        var views = await pipeline.QueryAsync<string>("SELECT name FROM derived_object WHERE kind = 'view' ORDER BY name;");
        await Assert.That(views).IsEquivalentTo(MetricViews);

        // No column is named for a person. This shows only that no column says it is one: the one name with
        // "assignee" in it is exempted by hand (it is a number of the setting), and a person column under
        // another name would pass.
        var columns = await pipeline.QueryAsync<string>(
            "SELECT o.name || '.' || c.name FROM derived_object o JOIN pragma_table_info(o.name) c WHERE o.kind = 'view' ORDER BY o.name, c.cid;");
        string[] personWords = ["assignee", "person", "people", "account", "author", "reporter", "hash", "email", "user"];
        await Assert.That(columns).Contains("delivered_per_sprint.delivered_count");
        await Assert.That(columns.Where(column => column != "minimum_group_size.assignees_behind_a_figure" && personWords.Any(word => column.Contains(word, StringComparison.OrdinalIgnoreCase)))).IsEmpty();

        // And no row of a view holds a person's stored hash, whatever its column is called. This shows that
        // no view gives a hash out for this store; it does not show that no view is grouped by person (a view
        // could group by the hash and leave it out). The store's content is one block per table or view,
        // headed by its name.
        var people = await pipeline.QueryAsync<string>("SELECT DISTINCT assignee_hash FROM issue WHERE assignee_hash IS NOT NULL;");
        await Assert.That(people.Count).IsEqualTo(6);
        var blocks = ("\n" + await pipeline.ReadStoreContentAsync()).Split("\n## ", StringSplitOptions.RemoveEmptyEntries).ToDictionary(block => block[..block.IndexOf('\n')], StringComparer.Ordinal);
        await Assert.That(blocks["issue"]).Contains(people[0]);
        await Assert.That(blocks["cycle_time_per_sprint_and_bucket"]).Contains("41 | 3 | 3 | ");
        var held = (from view in views
                    from person in people
                    where blocks[view].Contains(person, StringComparison.OrdinalIgnoreCase)
                    select view).ToList();
        await Assert.That(held).IsEmpty();
    }

    /// <summary>A report without its method note: from the heading to the end of the tables, and the footer.</summary>
    private static string TablesAndFooter(string report)
    {
        var method = report.IndexOf(MethodHeading, StringComparison.Ordinal);
        var footer = report.LastIndexOf("\n---\n", StringComparison.Ordinal);
        if (method < 0 || footer < method)
        {
            throw new InvalidOperationException("The report has no method note followed by a footer.");
        }

        return report[..method] + report[footer..];
    }

    private static string Footer(string anomalies, string betweenSprints) =>
        "\n---\n\n"
        + "- As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)\n"
        + $"- Issues with changelog anomalies, in the whole store: {anomalies}\n"
        + $"- Issues delivered between sprints, in the whole store: {betweenSprints}\n";

    private static string DataQuality(string anomalies, string betweenSprints) =>
        "| anomaly_issue_count | delivered_between_sprints_count | suppressed |\n| --- | --- | --- |\n"
        + $"| {anomalies} | {betweenSprints} | 0 |\n";

    /// <summary>
    /// Three sprints, all baseline sprints, in a store of six assignees. What the default numbers withhold of it:
    /// <list type="bullet">
    /// <item>Sprint 41 has four people: short of a team, so its delivered count and both tables are withheld
    /// (owner decisions D41, D45), and as a baseline sprint it withholds the whole baseline (D50). Three have
    /// a 3-point issue each; the fourth has the only 5-point issue and the only one in Review; an 8-point
    /// issue is unassigned.</item>
    /// <item>Sprint 42 has a team of six, two of whom delivered: its delivered count and its cycle time table
    /// are withheld (D49, D54), and it too withholds the whole baseline (D56).</item>
    /// <item>Sprint 43 has a team of six, five of whom delivered: bucket 5 has two people behind it and
    /// Review one, so those rows keep their counts and lose their figures (D5).</item>
    /// <item>One issue in the store has an anomaly, and none was delivered between sprints: both footer counts
    /// are withheld, one for its single assignee and one for having nobody behind it.</item>
    /// </list>
    /// Two rules it cannot trip beside those, and the tests that have them:
    /// <list type="bullet">
    /// <item>A pooled baseline row withheld for one baseline sprint's row (D44) shows only when no baseline
    /// sprint withholds the whole baseline:
    /// <see cref="A_pooled_baseline_row_whose_row_one_baseline_sprint_had_suppressed_is_shown"/>.</item>
    /// <item>The footer's team check needs a store of fewer than five assignees, and sprints 42 and 43 need
    /// six: <see cref="The_footers_two_counts_are_shown_when_the_whole_store_has_four_assignees"/>.</item>
    /// </list>
    /// </summary>
    private static void AddStoreThatTripsEveryRule(FakeJira jira)
    {
        AddTwoSprintsOfTwoWorkingWeeks(jira);
        jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 9), plannedEnd: Local(2026, 2, 13, 17), completed: Local(2026, 2, 13, 17));

        Delivered(jira, 41, "FAKE-11", points: 3, People.One, days: 1);
        Delivered(jira, 41, "FAKE-12", points: 3, People.Two, days: 2);
        Delivered(jira, 41, "FAKE-13", points: 3, People.Three, days: 3);
        Delivered(jira, 41, "FAKE-14", points: 5, People.Four, days: 1, reviewDays: 1);
        Delivered(jira, 41, "FAKE-15", points: 8, assignee: null, days: 2);

        foreach (var (person, number) in People.Team.Append(People.Six).Select((person, index) => (person, index + 1)))
        {
            StillInDevelopment(jira, 42, $"FAKE-2{number}", person);
        }

        Delivered(jira, 42, "FAKE-27", points: 3, People.One, days: 1);
        Delivered(jira, 42, "FAKE-28", points: 5, People.Two, days: 2);

        Delivered(jira, 43, "FAKE-31", points: 3, People.One, days: 2);
        Delivered(jira, 43, "FAKE-32", points: 3, People.Two, days: 2);
        Delivered(jira, 43, "FAKE-33", points: 3, People.Three, days: 2);
        Delivered(jira, 43, "FAKE-34", points: 5, People.Four, days: 1);
        Delivered(jira, 43, "FAKE-35", points: 5, People.Five, days: 1, reviewDays: 1);
        StillInDevelopment(jira, 43, "FAKE-36", People.Six);

        jira.AddIssue("FAKE-90", Local(2026, 1, 8, 9), createdIn: Workflow.Done).AssignedTo(People.One);
    }
}
