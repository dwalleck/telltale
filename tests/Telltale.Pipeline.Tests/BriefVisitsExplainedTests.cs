using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What a report says about brief visits besides the counts (GLOSSARY: Brief visit; owner decisions D58 to
/// D63): a paragraph of the method note, printed only when a status is listed under
/// <c>briefVisitThresholds</c>, and worded by the minimum group size's numbers in force (D67). It also covers
/// what listing a status must not change: the console of <c>derive</c> and <c>run</c>, the footer's changelog
/// anomaly count, and the baseline columns. Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the
/// 19th, 10:00. Its team is five people, each with an issue that stays in New; four of them also have an
/// issue that passed through Development in twenty seconds, and the fifth one that stayed there two days.
/// </summary>
public class BriefVisitsExplainedTests
{
    private const string LastParagraphBefore = "- Work that has since been removed from Jira stays in the figures until the next backfill.\n";

    // The paragraph's sentences that hold no number.
    private const string WhatABriefVisitIs =
        "- A brief visit is one unbroken stay in a status that ended and was shorter than the status's threshold."
        + " It is measured in clock time over the issue's whole history: weekends and holidays count, and the stay is not clipped to the sprint."
        + " For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues in the status's time in status row had a brief visit during the time the row counts."
        + " Brief visits are counted and nothing more: no duration was excluded or corrected because of one, and every other figure is what it would be without the count."
        + " A short median in the time in status table does not show how many issues had a brief visit.";

    private const string AZeroAndAWithheldCountReadAlike =
        " A count of nothing has nobody behind it, so a zero and a withheld count both read \"suppressed\".";

    private const string WithheldUnderTwo =
        " A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than two distinct assignees are among the issues counted.";

    private const string WithheldUnderOne =
        " A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than one distinct assignee is among the issues counted.";

    private const string AStatusWithNoRowHasNoLine = " A listed status with no row in the time in status table has no line";

    private const string WhenTheRuleWasAdded =
        " This rule was added on 2026-11-12, after the first run on real data: the thresholds were chosen with knowledge of results, and the counts describe data quality and are not a success criterion.\n";

    private const string Footer =
        """

        ---

        - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
        - Issues with changelog anomalies, in the whole store: suppressed
        - Issues delivered between sprints, in the whole store: suppressed

        """;

    [Test]
    public async Task With_a_status_listed_the_method_note_ends_with_a_paragraph_on_brief_visits_and_the_footer_follows_it()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            LastParagraphBefore
            + WhatABriefVisitIs
            + " A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than three distinct assignees are among the issues counted."
            + AZeroAndAWithheldCountReadAlike
            + AStatusWithNoRowHasNoLine + ", and a sprint whose team is smaller than five has none at all."
            + WhenTheRuleWasAdded
            + Footer.ReplaceLineEndings("\n")
            + "- Fake Development: 4 issues had a visit shorter than 5 minutes\n");
    }

    [Test]
    public async Task With_a_status_listed_the_method_note_calls_the_whole_store_counts_the_footers_first_two_counts()
    {
        // The footer then has more counts than two, and the brief visit counts are withheld by another rule
        // than the one this sentence gives (review finding F6 of ticket 01).
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains(
            " The footer's first two counts read \"suppressed\" when the issues in the whole store have fewer than five distinct assignees, or fewer than three are among the issues counted.\n");
        await Assert.That(report).DoesNotContain("The footer's two counts");
    }

    [Test]
    [Arguments(4, 2, WithheldUnderTwo + AZeroAndAWithheldCountReadAlike + AStatusWithNoRowHasNoLine + ", and a sprint whose team is smaller than four has none at all.", "4 issues had")]
    [Arguments(1, 1, WithheldUnderOne + AZeroAndAWithheldCountReadAlike + AStatusWithNoRowHasNoLine + ", and a sprint whose team is smaller than one has none at all.", "4 issues had")]
    [Arguments(0, 2, WithheldUnderTwo + AZeroAndAWithheldCountReadAlike + AStatusWithNoRowHasNoLine + ".", "4 issues had")]
    [Arguments(4, 0, " No count is withheld for the number of assignees behind it, and a count of nothing reads 0." + AStatusWithNoRowHasNoLine + ", and a sprint whose team is smaller than four has none at all.", "4 issues had")]
    [Arguments(0, 0, " No count is withheld for group size, and a count of nothing reads 0." + AStatusWithNoRowHasNoLine + ".", "4 issues had")]
    [Arguments(5, 5, " A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than five distinct assignees are among the issues counted." + AZeroAndAWithheldCountReadAlike + AStatusWithNoRowHasNoLine + ", and a sprint whose team is smaller than five has none at all.", "issues with")]
    public async Task The_paragraph_says_what_a_count_reads_in_place_of_a_number_by_the_numbers_in_force(int team, int assigneesBehindAFigure, string whatACountReads, string developmentLineHas)
    {
        // Four people are behind the four issues counted: the count is shown under every pair but five and five.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["minimumGroupSize"] = Scenarios.MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains(LastParagraphBefore + WhatABriefVisitIs + whatACountReads + WhenTheRuleWasAdded + "\n---\n");
        await Assert.That(report).Contains($"\n- Fake Development: {developmentLineHas} ");
    }

    [Test]
    [Arguments(5, 0, "- Fake Review: 0 issues had a visit shorter than 2 minutes\n", " reads 0.")]
    [Arguments(5, 3, "- Fake Review: issues with a visit shorter than 2 minutes: suppressed\n", " so a zero and a withheld count both read \"suppressed\".")]
    public async Task What_the_paragraph_says_of_a_count_of_nothing_is_what_the_footer_line_of_one_reads(int team, int assigneesBehindAFigure, string reviewLine, string paragraphSays)
    {
        // Nobody had a brief visit to Review: the four issues stayed there two days.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Review.Id] = 2 };
        pipeline.Config["minimumGroupSize"] = Scenarios.MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).EndsWith("- Fake Development: 4 issues had a visit shorter than 5 minutes\n" + reviewLine);
        await Assert.That(report).Contains(paragraphSays);
    }

    [Test]
    public async Task A_sprint_short_of_a_team_has_the_paragraph_and_no_brief_visit_lines()
    {
        // Four people, and no fifth. The status is listed, so the note explains the lines the footer would have.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            CaughtUp(pipeline.Jira, $"FAKE-{number}", person, 41, Local(2026, 1, 7, 10));
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            ", and a sprint whose team is smaller than five has none at all."
            + WhenTheRuleWasAdded
            + Footer.ReplaceLineEndings("\n"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task With_no_status_listed_the_report_says_nothing_of_brief_visits_and_the_method_note_is_what_it_was(bool emptySetting)
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        if (emptySetting)
        {
            pipeline.Config["briefVisitThresholds"] = new JsonObject();
        }

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).EndsWith(LastParagraphBefore + Footer.ReplaceLineEndings("\n"));
        await Assert.That(report).Contains(" The footer's two counts read \"suppressed\" when the issues in the whole store have fewer than five distinct assignees, or fewer than three are among the issues counted.\n");
        await Assert.That(report).DoesNotContain("brief visit");
        await Assert.That(report).DoesNotContain("briefVisitThresholds");
    }

    [Test]
    public async Task A_baseline_sprints_counts_are_in_its_own_report_and_the_reported_sprints_baseline_columns_gain_nothing()
    {
        // Sprint 41 is the baseline: four of its issues had a brief visit to Development. In sprint 42 three had.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        pipeline.Jira.AddSprint42();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 19, 11), 42);
        foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 21)))
        {
            CaughtUp(pipeline.Jira, $"FAKE-{number}", person, 42, Local(2026, 1, 21, 10));
        }

        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"], ["report", "--sprint", "41"]);

        // The reported sprint: the tables have the baseline columns they had, and the footer has its own count
        // and no other. There is no baseline count (owner decision D63).
        var reported = await pipeline.ReadReportAsync(42);
        await Assert.That(reported).Contains(ReportText.TimeInStatusHeaderWithBaseline);
        await Assert.That(reported).Contains("\n| Fake Development | 3 | 0.0 | 0.0 | 0.0 |\n");
        await Assert.That(reported).EndsWith(
            "- Issues delivered between sprints, in the whole store: suppressed\n"
            + "- Fake Development: 3 issues had a visit shorter than 5 minutes\n");
        await Assert.That(reported).DoesNotContain("4 issues had");
        await Assert.That(reported.Split('\n').Count(line => line.Contains("a visit shorter than", StringComparison.Ordinal))).IsEqualTo(1);

        // The baseline sprint's count is read by reporting on that sprint.
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            "- Issues delivered between sprints, in the whole store: suppressed\n"
            + "- Fake Development: 4 issues had a visit shorter than 5 minutes\n");
    }

    [Test]
    public async Task The_console_of_derive_and_run_and_the_anomaly_count_are_the_same_with_and_without_brief_visits_in_the_data()
    {
        // Three stores. In the first, four issues passed through Development in twenty seconds and Development
        // is listed. In the second, the same issues stayed there two hours: no brief visit. The third is the
        // first without the setting. Each has three people's issues with a changelog anomaly.
        await using var withBriefVisits = new PipelineHarness();
        await using var withNone = new PipelineHarness();
        await using var withoutTheSetting = new PipelineHarness();
        foreach (var (pipeline, secondsInDevelopment, listed) in new[] { (withBriefVisits, 20, true), (withNone, 7200, true), (withoutTheSetting, 20, false) })
        {
            pipeline.Jira.AddSprint41();
            pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
            foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
            {
                CaughtUp(pipeline.Jira, $"FAKE-{number}", person, 41, Local(2026, 1, 7, 10), secondsInDevelopment);
            }

            foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 11)))
            {
                pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                    .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                    .NowIn(Workflow.Review);
            }

            if (listed)
            {
                pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            }

            await pipeline.RunAllAsync(["backfill"], ["derive"], ["run", "--sprint", "41"]);
        }

        // The fixture is what it claims: the first store has brief visits and the second has none.
        await Assert.That(await withBriefVisits.ReadReportAsync(41)).EndsWith("\n- Fake Development: 4 issues had a visit shorter than 5 minutes\n");
        await Assert.That(await withNone.ReadReportAsync(41)).EndsWith("\n- Fake Development: issues with a visit shorter than 5 minutes: suppressed\n");

        // Standard error is where derive logs the anomalies; standard output names the report's path.
        var expectedError = ConsoleOf(withoutTheSetting.Error, withoutTheSetting);
        var expectedOut = ConsoleOf(withoutTheSetting.Out, withoutTheSetting);
        await Assert.That(expectedError).Contains("Changelog anomalies: 3 across 3 issues");
        await Assert.That(expectedOut).Contains("Wrote ");
        foreach (var pipeline in new[] { withBriefVisits, withNone })
        {
            await Assert.That(ConsoleOf(pipeline.Error, pipeline)).IsEqualTo(expectedError);
            await Assert.That(ConsoleOf(pipeline.Out, pipeline)).IsEqualTo(expectedOut);
        }

        foreach (var pipeline in new[] { withBriefVisits, withNone, withoutTheSetting })
        {
            await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n- Issues with changelog anomalies, in the whole store: 3\n");
        }
    }

    /// <summary>What a harness's console writer holds, with the harness's own temporary directory taken out so that two harnesses can be compared.</summary>
    private static string ConsoleOf(StringWriter console, PipelineHarness pipeline) =>
        console.ToString().ReplaceLineEndings("\n").Replace(pipeline.Directory, "<directory>", StringComparison.Ordinal);

    /// <summary>
    /// Sprint 41 with its team of five idle in New, four issues <see cref="CaughtUp"/> of four people, and a
    /// fifth person's issue that did two days in Development: a Development row of five issues, four of them
    /// with a brief visit.
    /// </summary>
    private static void AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            CaughtUp(jira, $"FAKE-{number}", person, 41, Local(2026, 1, 7, 10));
        }

        jira.AddIssue("FAKE-5", Local(2026, 1, 5, 9)).AssignedTo(People.Five).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
    }

    /// <summary>
    /// An issue that was caught up in one go at <paramref name="at"/>: in New until then, moved to Development
    /// and, after <paramref name="secondsInDevelopment"/>, to Review, where it stayed two days before it was done.
    /// </summary>
    private static FakeIssue CaughtUp(FakeJira jira, string key, FakePerson assignee, long sprintId, DateTimeOffset at, int secondsInDevelopment = 20) =>
        jira.AddIssue(key, at.AddDays(-2)).AssignedTo(assignee).InSprints(sprintId)
            .Moved(at, Workflow.Development)
            .Moved(at.AddSeconds(secondsInDevelopment), Workflow.Review)
            .Moved(at.AddDays(2), Workflow.Done);
}
