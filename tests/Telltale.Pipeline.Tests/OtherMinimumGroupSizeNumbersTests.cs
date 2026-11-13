using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Every suppression rule applies with the configured numbers in place of five and three (owner decision D66),
/// and a number of zero turns only its own check off (D65). <see cref="SuppressionTests"/>,
/// <see cref="BaselineTests"/> and <see cref="MethodNoteAndFooterTests"/> say what each rule withholds at the
/// default numbers; each test here sets <c>minimumGroupSize</c> to other numbers and shows a group that the
/// default numbers would treat the other way, in the report and through <c>query</c>.
/// Sprint 41 runs from Monday 5 to Friday 16 January 2026 and sprint 42 from Monday 19 to Friday 30 January,
/// except in the test of the footer.
/// </summary>
public class OtherMinimumGroupSizeNumbersTests
{
    private const string CycleTimeHeader =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: |\n";

    private const string TimeInStatusHeader =
        "\n## Time in status\n\n"
        + "| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n";

    private const string CycleTimeHeaderWithBaseline =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) | Baseline median (business days) | Baseline P85 (business days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n";

    private const string TimeInStatusHeaderWithBaseline =
        "\n## Time in status\n\n"
        + "| Status | Issues | Median (business days) | Total (business days) | Baseline median (business days) |\n| --- | ---: | ---: | ---: | ---: |\n";

    /// <summary>What follows the two tables.</summary>
    private const string MethodHeading = "\n## Method\n";

    /// <summary>A sprint that shows nothing: no delivered count, and one line in place of each table's rows.</summary>
    private const string WholeSprintWithheld =
        "\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n\n## Time in status\n\nsuppressed\n" + MethodHeading;

    private const string DeliveredSql = "SELECT sprint_id, delivered_count, suppressed, team_suppressed FROM delivered_per_sprint ORDER BY sprint_id;";

    private const string CycleTimeSql =
        "SELECT sprint_id, point_bucket, issue_count, median_business_days, suppressed FROM cycle_time_per_sprint_and_bucket ORDER BY sprint_id, point_bucket;";

    private const string TimeInStatusSql =
        "SELECT sprint_id, status_name, issue_count, median_business_days, total_business_days, suppressed FROM time_in_status_per_sprint ORDER BY sprint_id, board_position;";

    [Test]
    public async Task With_a_team_minimum_of_four_a_sprint_of_four_assignees_is_reported_and_a_sprint_of_three_is_withheld_whole()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 4, assigneesBehindAFigure: 3);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, number, points: 3, person, development: 2);
        }

        foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 11)))
        {
            Delivered(pipeline.Jira, 42, number, points: 3, person, development: 2);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"]);
        var queried = await QueryAsync(pipeline, DeliveredSql, CycleTimeSql, TimeInStatusSql);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 4\n"
            + CycleTimeHeader
            + "| 3 | 4 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 4 | 1.0 | 4.0 |\n"
            + "| Fake Development | 4 | 2.0 | 8.0 |\n"
            + MethodHeading);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(WholeSprintWithheld);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 41 | 4 | 0 | 0 |
            | 42 | NULL | 1 | 1 |
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | 3 | 4 | 2 | 0 |
            | 42 | NULL | NULL | NULL | 1 |
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | Fake New | 4 | 1 | 4 | 0 |
            | 41 | Fake Development | 4 | 2 | 8 | 0 |
            | 42 | NULL | NULL | NULL | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_two_assignees_behind_a_figure_a_row_with_two_assignees_shows_its_figures_and_a_row_with_one_is_withheld_and_keeps_its_issue_count()
    {
        // Buckets 3 and 8 and Review have two people behind them. Bucket 5 and QE have two issues of one person.
        await using var pipeline = new PipelineHarness();
        AddSprintFortyOneOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 5, assigneesBehindAFigure: 2);
        Delivered(pipeline.Jira, 41, 1, points: 3, People.One, development: 1, review: 1);
        Delivered(pipeline.Jira, 41, 2, points: 3, People.Two, development: 1, review: 1);
        Delivered(pipeline.Jira, 41, 3, points: 5, People.Three, development: 1, qe: 1);
        Delivered(pipeline.Jira, 41, 4, points: 5, People.Three, development: 1, qe: 1);
        Delivered(pipeline.Jira, 41, 5, points: 8, People.Four, development: 2);
        Delivered(pipeline.Jira, 41, 6, points: 8, People.Five, development: 2);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var queried = await QueryAsync(pipeline, DeliveredSql, CycleTimeSql, TimeInStatusSql);

        // Development is 1, 1, 1, 1, 2, 2: the median is the mean of 1 and 1.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 6\n"
            + CycleTimeHeader
            + "| 3 | 2 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 2 | suppressed | suppressed | suppressed | suppressed |\n"
            + "| 8 | 2 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 |\n"
            + "| Fake Development | 6 | 1.0 | 8.0 |\n"
            + "| Fake Review | 2 | 1.0 | 2.0 |\n"
            + "| Fake QE | 2 | suppressed | suppressed |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 41 | 6 | 0 | 0 |
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | 3 | 2 | 2 | 0 |
            | 41 | 5 | 2 | NULL | 1 |
            | 41 | 8 | 2 | 2 | 0 |
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | Fake New | 6 | 1 | 6 | 0 |
            | 41 | Fake Development | 6 | 1 | 8 | 0 |
            | 41 | Fake Review | 2 | 1 | 2 | 0 |
            | 41 | Fake QE | 2 | NULL | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_the_team_minimum_at_zero_and_three_behind_a_figure_a_sprint_of_four_is_reported_and_a_row_with_two_assignees_is_still_withheld()
    {
        // Four people. Bucket 3 has three of them behind it; bucket 5 and Review have two.
        await using var pipeline = new PipelineHarness();
        AddSprintFortyOneOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 0, assigneesBehindAFigure: 3);
        Delivered(pipeline.Jira, 41, 1, points: 3, People.One, development: 2);
        Delivered(pipeline.Jira, 41, 2, points: 3, People.Two, development: 2);
        Delivered(pipeline.Jira, 41, 3, points: 3, People.Three, development: 2);
        Delivered(pipeline.Jira, 41, 4, points: 5, People.Four, development: 1, review: 1);
        Delivered(pipeline.Jira, 41, 5, points: 5, People.One, development: 1, review: 1);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var queried = await QueryAsync(pipeline, DeliveredSql, CycleTimeSql, TimeInStatusSql);

        // Development is 1, 1, 2, 2, 2.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 5\n"
            + CycleTimeHeader
            + "| 3 | 3 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 2 | suppressed | suppressed | suppressed | suppressed |\n"
            + TimeInStatusHeader
            + "| Fake New | 5 | 1.0 | 5.0 |\n"
            + "| Fake Development | 5 | 2.0 | 8.0 |\n"
            + "| Fake Review | 2 | suppressed | suppressed |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 41 | 5 | 0 | 0 |
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | 3 | 3 | 2 | 0 |
            | 41 | 5 | 2 | NULL | 1 |
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | Fake New | 5 | 1 | 5 | 0 |
            | 41 | Fake Development | 5 | 2 | 8 | 0 |
            | 41 | Fake Review | 2 | NULL | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_nothing_needed_behind_a_figure_and_a_team_of_five_a_sprint_of_four_is_still_withheld_and_every_row_of_a_sprint_of_five_shows_its_figures()
    {
        // Sprint 41 has four people. Sprint 42 has five: bucket 5 and Review have one person behind them, and
        // the unestimated row only an issue nobody is assigned to.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 5, assigneesBehindAFigure: 0);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, number, points: 3, person, development: 2);
        }

        foreach (var (person, number) in new[] { (People.One, 11), (People.Two, 12), (People.Three, 13), (People.Five, 14) })
        {
            Delivered(pipeline.Jira, 42, number, points: 3, person, development: 2);
        }

        Delivered(pipeline.Jira, 42, 15, points: 5, People.Four, development: 1, review: 1);
        Delivered(pipeline.Jira, 42, 16, points: null, assignee: null, development: 3);

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"]);
        var queried = await QueryAsync(pipeline, DeliveredSql, CycleTimeSql, TimeInStatusSql);

        // Sprint 42's Development is 1, 2, 2, 2, 2, 3.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(WholeSprintWithheld);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "\nDelivered issues: 6\n"
            + CycleTimeHeader
            + "| 3 | 4 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| unestimated | 1 | 3.0 | 3.0 | 3.0 | 3.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 |\n"
            + "| Fake Development | 6 | 2.0 | 12.0 |\n"
            + "| Fake Review | 1 | 1.0 | 1.0 |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 41 | NULL | 1 | 1 |
            | 42 | 6 | 0 | 0 |
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | NULL | NULL | NULL | 1 |
            | 42 | NULL | 1 | 3 | 0 |
            | 42 | 3 | 4 | 2 | 0 |
            | 42 | 5 | 1 | 2 | 0 |
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | NULL | NULL | NULL | NULL | 1 |
            | 42 | Fake New | 6 | 1 | 6 | 0 |
            | 42 | Fake Development | 6 | 2 | 12 | 0 |
            | 42 | Fake Review | 1 | 1 | 1 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_numbers_above_the_defaults_a_sprint_of_five_assignees_and_a_row_with_three_are_withheld()
    {
        // Six for a team and four behind a figure. Sprint 41 has five people. Sprint 42 has six: four behind
        // bucket 3, three behind bucket 5 and Review.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 6, assigneesBehindAFigure: 4);
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, number, points: 3, person, development: 2);
        }

        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 11)))
        {
            Delivered(pipeline.Jira, 42, number, points: 3, person, development: 2);
        }

        foreach (var (person, number) in new[] { (People.Five, 21), (People.Six, 22), (People.One, 23) })
        {
            Delivered(pipeline.Jira, 42, number, points: 5, person, development: 1, review: 1);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"]);
        var queried = await QueryAsync(pipeline, DeliveredSql, CycleTimeSql, TimeInStatusSql);

        // Sprint 42's Development is 1, 1, 1, 2, 2, 2, 2.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(WholeSprintWithheld);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "\nDelivered issues: 7\n"
            + CycleTimeHeader
            + "| 3 | 4 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 3 | suppressed | suppressed | suppressed | suppressed |\n"
            + TimeInStatusHeader
            + "| Fake New | 7 | 1.0 | 7.0 |\n"
            + "| Fake Development | 7 | 2.0 | 11.0 |\n"
            + "| Fake Review | 3 | suppressed | suppressed |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 41 | NULL | 1 | 1 |
            | 42 | 7 | 0 | 0 |
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | NULL | NULL | NULL | 1 |
            | 42 | 3 | 4 | 2 | 0 |
            | 42 | 5 | 3 | NULL | 1 |
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | NULL | NULL | NULL | NULL | 1 |
            | 42 | Fake New | 7 | 1 | 7 | 0 |
            | 42 | Fake Development | 7 | 2 | 11 | 0 |
            | 42 | Fake Review | 3 | NULL | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_four_and_two_a_delivered_count_with_one_assignee_behind_it_is_withheld_with_its_cycle_time_rows_and_one_with_two_is_shown()
    {
        // Owner decisions D49 and D54 at four and two. Both sprints have a team of four, which the default
        // numbers withhold whole. In sprint 41 one person delivered, in two buckets; in sprint 42 two people
        // did. The other people's issues stay in New to the end of the sprint: nine business days and eight hours.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 4, assigneesBehindAFigure: 2);
        Delivered(pipeline.Jira, 41, 1, points: 3, People.One, development: 2);
        Delivered(pipeline.Jira, 41, 2, points: 5, People.One, development: 2);
        foreach (var (person, number) in new[] { (People.Two, 3), (People.Three, 4), (People.Four, 5) })
        {
            LeftInNew(pipeline.Jira, 41, number, person);
        }

        Delivered(pipeline.Jira, 42, 11, points: 3, People.One, development: 2);
        Delivered(pipeline.Jira, 42, 12, points: 3, People.Two, development: 2);
        LeftInNew(pipeline.Jira, 42, 13, People.Three);
        LeftInNew(pipeline.Jira, 42, 14, People.Four);

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"]);
        var queried = await QueryAsync(pipeline, DeliveredSql, CycleTimeSql);

        // Sprint 41: the time in status table keeps its rows (D55). New is 1, 1 and three times 9.33;
        // Development has one person behind it.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n"
            + TimeInStatusHeader
            + "| Fake New | 5 | 9.3 | 30.0 |\n"
            + "| Fake Development | 2 | suppressed | suppressed |\n"
            + MethodHeading);

        // Sprint 42: New is 1, 1, 9.33, 9.33, so its median is 5.17 and its total 20.67.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "\nDelivered issues: 2\n"
            + CycleTimeHeader
            + "| 3 | 2 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeader
            + "| Fake New | 4 | 5.2 | 20.7 |\n"
            + "| Fake Development | 2 | 2.0 | 4.0 |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- | --- |
            | 41 | NULL | 1 | 0 |
            | 42 | 2 | 0 | 0 |
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | NULL | NULL | NULL | 1 |
            | 42 | 3 | 2 | 2 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_four_and_two_a_baseline_sprint_of_four_assignees_with_two_among_its_delivered_issues_gives_the_baseline_figures()
    {
        // Owner decisions D50 and D56 at four and two: the baseline sprint passes both checks, and the default
        // numbers would withhold the whole baseline for it. Two of its four people delivered, two days each;
        // the other two issues stay in New, so the baseline's New is 1, 1, 9.33, 9.33.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 4, assigneesBehindAFigure: 2);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        Delivered(pipeline.Jira, 41, 1, points: 3, People.One, development: 2);
        Delivered(pipeline.Jira, 41, 2, points: 3, People.Two, development: 2);
        LeftInNew(pipeline.Jira, 41, 3, People.Three);
        LeftInNew(pipeline.Jira, 41, 4, People.Four);
        AddSprintFortyTwoWithFourAssigneesInBucketThree(pipeline.Jira);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var queried = await QueryAsync(
            pipeline,
            "SELECT sprint_id, suppressed FROM baseline_sprint;",
            "SELECT point_bucket, median_business_days, p85_business_days, suppressed FROM baseline_cycle_time_per_bucket;",
            "SELECT status_name, ROUND(median_business_days, 2) AS median_business_days, suppressed FROM baseline_time_in_status ORDER BY board_position;");

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeaderWithBaseline
            + "| 3 | 4 | 1.0 | 1.0 | 1.0 | 1.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 4 | 1.0 | 4.0 | 5.2 |\n"
            + "| Fake Development | 4 | 1.0 | 4.0 | 2.0 |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | suppressed |
            | --- | --- |
            | 41 | 0 |
            | point_bucket | median_business_days | p85_business_days | suppressed |
            | --- | --- | --- | --- |
            | 3 | 2 | 2 | 0 |
            | status_name | median_business_days | suppressed |
            | --- | --- | --- |
            | Fake New | 5.17 | 0 |
            | Fake Development | 2 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    [Arguments("three assignees, two of them delivered")]
    [Arguments("four assignees, one of them delivered")]
    public async Task With_four_and_two_a_baseline_sprint_short_of_either_number_withholds_every_baseline_figure(string baselineSprint)
    {
        // Owner decision D50 (a baseline sprint short of a team) and D56 (one whose delivered count is
        // withheld), at four and two. Sprint 42, the one reported, passes both checks.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 4, assigneesBehindAFigure: 2);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        Delivered(pipeline.Jira, 41, 1, points: 3, People.One, development: 2);
        if (baselineSprint == "three assignees, two of them delivered")
        {
            Delivered(pipeline.Jira, 41, 2, points: 3, People.Two, development: 2);
            LeftInNew(pipeline.Jira, 41, 3, People.Three);
        }
        else
        {
            LeftInNew(pipeline.Jira, 41, 2, People.Two);
            LeftInNew(pipeline.Jira, 41, 3, People.Three);
            LeftInNew(pipeline.Jira, 41, 4, People.Four);
        }

        AddSprintFortyTwoWithFourAssigneesInBucketThree(pipeline.Jira);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var queried = await QueryAsync(
            pipeline,
            "SELECT (SELECT COUNT(*) FROM baseline_cycle_time_per_bucket) AS cycle_time_rows, (SELECT COUNT(*) FROM baseline_time_in_status) AS status_rows, "
            + "(SELECT group_concat(sprint_id || '=' || suppressed, ' ') FROM baseline_sprint) AS baseline_sprints;");

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeaderWithBaseline
            + "| 3 | 4 | 1.0 | 1.0 | 1.0 | 1.0 | suppressed | suppressed |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 4 | 1.0 | 4.0 | suppressed |\n"
            + "| Fake Development | 4 | 1.0 | 4.0 | suppressed |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | cycle_time_rows | status_rows | baseline_sprints |
            | --- | --- | --- |
            | 0 | 0 | 41=1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_four_and_two_a_pooled_baseline_figure_is_withheld_when_one_baseline_sprint_has_one_assignee_behind_that_row()
    {
        // Owner decision D44 at four and two. Both baseline sprints have a team of four and four people among
        // their delivered issues. Sprint 41 has one person behind bucket 3 and Review, and three behind bucket
        // 5; sprint 42 has two behind each. Pooled, bucket 3 and Review have two people behind them, and are
        // withheld all the same: sprint 41's own report withholds them.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 4, assigneesBehindAFigure: 2);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        Delivered(pipeline.Jira, 41, 1, points: 3, People.One, development: 1, review: 2);
        Delivered(pipeline.Jira, 41, 2, points: 5, People.Two, development: 2);
        Delivered(pipeline.Jira, 41, 3, points: 5, People.Three, development: 2);
        Delivered(pipeline.Jira, 41, 4, points: 5, People.Four, development: 2);
        Delivered(pipeline.Jira, 42, 11, points: 3, People.One, development: 1, review: 1);
        Delivered(pipeline.Jira, 42, 12, points: 3, People.Two, development: 1, review: 1);
        Delivered(pipeline.Jira, 42, 13, points: 5, People.Three, development: 1);
        Delivered(pipeline.Jira, 42, 14, points: 5, People.Four, development: 1);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var queried = await QueryAsync(
            pipeline,
            "SELECT sprint_id, suppressed FROM baseline_sprint ORDER BY sprint_id;",
            "SELECT point_bucket, median_business_days, p85_business_days, suppressed FROM baseline_cycle_time_per_bucket ORDER BY point_bucket;",
            "SELECT status_name, median_business_days, suppressed FROM baseline_time_in_status ORDER BY board_position;");

        // Bucket 5 pooled is 1, 1, 2, 2, 2: the median is the third and the P85 the fifth. Development pooled
        // is 1, 1, 1, 1, 1, 2, 2, 2: the mean of 1 and 1.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            CycleTimeHeaderWithBaseline
            + "| 3 | 2 | 2.0 | 2.0 | 2.0 | 2.0 | suppressed | suppressed |\n"
            + "| 5 | 2 | 1.0 | 1.0 | 1.0 | 1.0 | 2.0 | 2.0 |\n"
            + TimeInStatusHeaderWithBaseline
            + "| Fake New | 4 | 1.0 | 4.0 | 1.0 |\n"
            + "| Fake Development | 4 | 1.0 | 4.0 | 1.0 |\n"
            + "| Fake Review | 2 | 1.0 | 2.0 | suppressed |\n"
            + MethodHeading);
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | suppressed |
            | --- | --- |
            | 41 | 0 |
            | 42 | 0 |
            | point_bucket | median_business_days | p85_business_days | suppressed |
            | --- | --- | --- | --- |
            | 3 | NULL | NULL | 1 |
            | 5 | 2 | 2 | 0 |
            | status_name | median_business_days | suppressed |
            | --- | --- | --- |
            | Fake New | 1 | 0 |
            | Fake Development | 1 | 0 |
            | Fake Review | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_one_assignee_behind_a_figure_a_row_made_only_of_unassigned_issues_and_a_data_quality_count_of_nothing_are_withheld()
    {
        // One is not zero: a figure needs somebody behind it. Bucket 5 has one person, and is shown. Bucket 8
        // and Review have only an issue nobody is assigned to. The store has no anomaly and no delivery
        // between sprints, so each of the footer's counts has nobody behind it.
        await using var pipeline = new PipelineHarness();
        AddSprintFortyOneOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 5, assigneesBehindAFigure: 1);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Delivered(pipeline.Jira, 41, number, points: 3, person, development: 2);
        }

        Delivered(pipeline.Jira, 41, 5, points: 5, People.Five, development: 2);
        Delivered(pipeline.Jira, 41, 6, points: 8, assignee: null, development: 1, review: 1);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var queried = await QueryAsync(
            pipeline,
            CycleTimeSql,
            TimeInStatusSql,
            "SELECT anomaly_issue_count, delivered_between_sprints_count, suppressed FROM data_quality;");

        // Development is 1, 2, 2, 2, 2, 2.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains(
            "\nDelivered issues: 6\n"
            + CycleTimeHeader
            + "| 3 | 4 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 8 | 1 | suppressed | suppressed | suppressed | suppressed |\n"
            + TimeInStatusHeader
            + "| Fake New | 6 | 1.0 | 6.0 |\n"
            + "| Fake Development | 6 | 2.0 | 11.0 |\n"
            + "| Fake Review | 1 | suppressed | suppressed |\n"
            + MethodHeading);
        await Assert.That(report).EndsWith(
            """

            ---

            - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
            - Issues with changelog anomalies, in the whole store: suppressed
            - Issues delivered between sprints, in the whole store: suppressed

            """.ReplaceLineEndings("\n"));
        await Assert.That(queried).IsEqualTo(
            """
            | sprint_id | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | 3 | 4 | 2 | 0 |
            | 41 | 5 | 1 | 2 | 0 |
            | 41 | 8 | 1 | NULL | 1 |
            | sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- | --- |
            | 41 | Fake New | 6 | 1 | 6 | 0 |
            | 41 | Fake Development | 6 | 2 | 11 | 0 |
            | 41 | Fake Review | 1 | NULL | NULL | 1 |
            | anomaly_issue_count | delivered_between_sprints_count | suppressed |
            | --- | --- | --- |
            | NULL | NULL | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    [Arguments(4, 2, 1, "2", "suppressed", "| 2 | NULL | 0 |")]
    [Arguments(4, 1, 2, "suppressed", "2", "| NULL | 2 | 0 |")]
    [Arguments(3, 2, 2, "suppressed", "suppressed", "| NULL | NULL | 1 |")]
    public async Task With_four_and_two_the_footers_counts_need_four_assignees_in_the_store_and_two_among_the_issues_counted(
        int peopleInTheStore, int peopleWithAnAnomaly, int peopleWhoDeliveredBetweenSprints, string anomalies, string betweenSprints, string row)
    {
        // The harness's own sprints: 41 is completed on 19 January at 10:00 and 42 starts at 10:30. Each
        // person of the store has an issue in New. Some of them also have an issue with an anomaly (created
        // in Done), and some an issue delivered between the sprints. The default numbers withhold both counts
        // in a store of four people, and a count with two people behind it in any store.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 4, assigneesBehindAFigure: 2);
        foreach (var (person, number) in People.Team.Take(peopleInTheStore).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 6, 9)).AssignedTo(person);
        }

        foreach (var (person, number) in People.Team.Take(peopleWithAnAnomaly).Select((person, index) => (person, index + 11)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 8, 9), createdIn: Workflow.Done).AssignedTo(person);
        }

        foreach (var (person, number) in People.Team.Take(peopleWhoDeliveredBetweenSprints).Select((person, index) => (person, index + 21)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 6, 9)).AssignedTo(person).Moved(Local(2026, 1, 19, 10, 15), Workflow.Done);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var queried = await QueryAsync(pipeline, "SELECT anomaly_issue_count, delivered_between_sprints_count, suppressed FROM data_quality;");

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            $"""

            ---

            - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
            - Issues with changelog anomalies, in the whole store: {anomalies}
            - Issues delivered between sprints, in the whole store: {betweenSprints}

            """.ReplaceLineEndings("\n"));
        await Assert.That(queried).IsEqualTo(
            $"""
            | anomaly_issue_count | delivered_between_sprints_count | suppressed |
            | --- | --- | --- |
            {row}

            """.ReplaceLineEndings("\n"));
    }

    private static JsonObject MinimumGroupSize(int team, int assigneesBehindAFigure) =>
        new() { ["team"] = team, ["assigneesBehindAFigure"] = assigneesBehindAFigure };

    /// <summary>What <c>query</c> prints for each statement in turn, with LF line endings.</summary>
    private static async Task<string> QueryAsync(PipelineHarness pipeline, params string[] statements)
    {
        pipeline.Out.GetStringBuilder().Clear();
        await pipeline.RunAllAsync([.. statements.Select(sql => new[] { "query", sql })]);
        return pipeline.Out.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>Sprint 41 alone, as two working weeks: Monday 09:00 to the second Friday 17:00, completed as planned.</summary>
    private static void AddSprintFortyOneOfTwoWorkingWeeks(FakeJira jira) =>
        jira.AddSprint41(plannedEnd: Local(2026, 1, 16, 17), completed: Local(2026, 1, 16, 17));

    /// <summary>Sprint 41 and 42 as two working weeks each: Monday 09:00 to the second Friday 17:00, completed as planned.</summary>
    private static void AddTwoSprintsOfTwoWorkingWeeks(FakeJira jira)
    {
        AddSprintFortyOneOfTwoWorkingWeeks(jira);
        jira.AddSprint42(start: Local(2026, 1, 19, 9), plannedEnd: Local(2026, 1, 30, 17), completed: Local(2026, 1, 30, 17));
    }

    /// <summary>Sprint 42 with four people, one delivered 3-point issue each, a day in Development.</summary>
    private static void AddSprintFortyTwoWithFourAssigneesInBucketThree(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 11)))
        {
            Delivered(jira, 42, number, points: 3, person, development: 1);
        }
    }

    /// <summary>
    /// An issue of sprint 41 or 42, created as the sprint starts (Monday 09:00), so a business day in New; in
    /// Development from Tuesday 09:00 for <paramref name="development"/> business days, then in Review and in
    /// QE for theirs, then done. Its cycle time is the three together: at most three days, so it is done by
    /// Friday 09:00 and its calendar days are its business days. Null <paramref name="points"/> leaves it
    /// unestimated and a null <paramref name="assignee"/> unassigned.
    /// </summary>
    private static void Delivered(FakeJira jira, long sprintId, int number, double? points, FakePerson? assignee, int development, int review = 0, int qe = 0)
    {
        var tuesday = MondayOf(sprintId).AddDays(1);
        var issue = jira.AddIssue($"FAKE-{number}", MondayOf(sprintId)).InSprints(sprintId).Moved(tuesday, Workflow.Development);
        if (points is { } estimate)
        {
            issue.Estimated(estimate);
        }

        if (assignee is not null)
        {
            issue.AssignedTo(assignee);
        }

        if (review > 0)
        {
            issue.Moved(tuesday.AddDays(development), Workflow.Review);
        }

        if (qe > 0)
        {
            issue.Moved(tuesday.AddDays(development + review), Workflow.QE);
        }

        issue.Moved(tuesday.AddDays(development + review + qe), Workflow.Done);
    }

    /// <summary>
    /// An issue of the sprint that is created as it starts and never moves: it is in New for the whole sprint,
    /// Monday 09:00 to the second Friday 17:00, which is nine business days and eight hours.
    /// </summary>
    private static void LeftInNew(FakeJira jira, long sprintId, int number, FakePerson assignee) =>
        jira.AddIssue($"FAKE-{number}", MondayOf(sprintId)).InSprints(sprintId).AssignedTo(assignee);

    private static DateTimeOffset MondayOf(long sprintId) => sprintId == 41 ? Local(2026, 1, 5, 9) : Local(2026, 1, 19, 9);
}
