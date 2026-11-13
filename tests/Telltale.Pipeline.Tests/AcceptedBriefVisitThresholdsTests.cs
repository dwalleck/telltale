using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What <c>briefVisitThresholds</c> may say (owner decision D58 and the confirmed readings on fractions and done
/// statuses, in the brief status visits spec): a fraction of a minute, a status of any role but done, and
/// nothing at all, which is the same as leaving the setting out. What is refused is in
/// <see cref="RefusedBriefVisitThresholdsTests"/>. Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday
/// the 19th, 10:00; its team is five people, each with an issue that stays in New.
/// </summary>
public class AcceptedBriefVisitThresholdsTests
{
    private const string ViewSql =
        "SELECT status_name, threshold_minutes, brief_visit_issue_count, suppressed FROM brief_visits_per_sprint ORDER BY sprint_id, board_position;";

    private const string ViewHead = "| status_name | threshold_minutes | brief_visit_issue_count | suppressed |\n| --- | --- | --- | --- |\n";

    [Test]
    public async Task A_threshold_may_be_a_fraction_of_a_minute_and_is_applied_as_written()
    {
        // Four people's issues each spent twenty seconds in Development and Review. Half a minute is longer
        // than that, and a quarter of a minute is shorter: a fraction rounded to a whole minute would be neither.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourIssuesThatPassThroughInTwentySecondsEach(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = JsonNode.Parse($$"""{ "{{Workflow.Development.Id}}": 0.5, "{{Workflow.Review.Id}}": 0.25 }""");

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: suppressed
            - Fake Development: 4 issues had a visit shorter than 0.5 minutes
            - Fake Review: issues with a visit shorter than 0.25 minutes: suppressed

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead + "| Fake Development | 0.5 | 4 | 0 |\n| Fake Review | 0.25 | NULL | 1 |\n");
    }

    [Test]
    public async Task A_status_with_any_role_but_done_may_be_listed_and_has_its_line()
    {
        // New is a not started status, Development a work status and Blocked a blocked one.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourIssuesThatPassThroughInTwentySecondsEach(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5, [Workflow.Development.Id] = 5, [Workflow.Blocked.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: suppressed
            - Fake New: 4 issues had a visit shorter than 5 minutes
            - Fake Development: 4 issues had a visit shorter than 5 minutes
            - Fake Blocked: 4 issues had a visit shorter than 5 minutes

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task An_empty_setting_gives_the_report_and_the_store_that_no_setting_gives()
    {
        // The same Jira three times: no setting, an empty one, and Development listed, to show there was something to count.
        await using var absent = new PipelineHarness();
        await using var empty = new PipelineHarness();
        await using var listed = new PipelineHarness();
        foreach (var pipeline in new[] { absent, empty, listed })
        {
            AddSprint41WithItsTeam(pipeline.Jira);
            AddFourIssuesThatPassThroughInTwentySecondsEach(pipeline.Jira);
        }

        empty.Config["briefVisitThresholds"] = new JsonObject();
        listed.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        foreach (var pipeline in new[] { absent, empty, listed })
        {
            await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        }

        // No lines and an unchanged report: byte for byte, as both were written at the same fixed time.
        var report = await absent.ReadReportBytesAsync(41);
        await Assert.That(await empty.ReadReportBytesAsync(41)).IsEqualTo(report);
        await Assert.That(await absent.ReadReportAsync(41)).EndsWith("\n- Issues delivered between sprints, in the whole store: suppressed\n");
        await Assert.That(await listed.ReadReportAsync(41)).EndsWith("\n- Fake Development: 4 issues had a visit shorter than 5 minutes\n");

        // No rows: every table and view of the store holds the same, the record of the settings included.
        await Assert.That(await empty.ReadStoreContentAsync()).IsEqualTo(await absent.ReadStoreContentAsync());
        await Assert.That(await QueryAsync(empty, ViewSql, "SELECT COUNT(*) AS brief_visits FROM brief_visits_per_sprint;")).IsEqualTo(
            ViewHead + "| brief_visits |\n| --- |\n| 0 |\n");
    }

    [Test]
    public async Task Writing_an_empty_setting_into_a_file_that_had_none_or_taking_it_out_again_needs_no_derive()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourIssuesThatPassThroughInTwentySecondsEach(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var report = await pipeline.ReadReportBytesAsync(41);

        pipeline.Config["briefVisitThresholds"] = new JsonObject();
        var withAnEmptySetting = await pipeline.RunAsync("report", "--sprint", "41");
        var reportWithAnEmptySetting = await pipeline.ReadReportBytesAsync(41);
        pipeline.Config.Remove("briefVisitThresholds");
        var withoutItAgain = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(withAnEmptySetting).IsEqualTo(0);
        await Assert.That(withoutItAgain).IsEqualTo(0);
        await Assert.That(reportWithAnEmptySetting).IsEqualTo(report);
        await Assert.That(await pipeline.ReadReportBytesAsync(41)).IsEqualTo(report);
    }

    /// <summary>Sprint 41 as the harness has it, and a team of five whose issues stay in New through it.</summary>
    private static void AddSprint41WithItsTeam(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
    }

    /// <summary>
    /// FAKE-1 to FAKE-4, one for each of four people, in sprint 41: created (so in New) on Wednesday 7 January
    /// at 10:00, then moved every twenty seconds, to Development, to Blocked and to Review, and done from
    /// Review after twenty seconds more. So each had one visit of twenty seconds to each of the four statuses.
    /// </summary>
    private static void AddFourIssuesThatPassThroughInTwentySecondsEach(FakeJira jira)
    {
        var created = Local(2026, 1, 7, 10);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            jira.AddIssue($"FAKE-{number}", created).AssignedTo(person).InSprints(41)
                .Moved(created.AddSeconds(20), Workflow.Development)
                .Moved(created.AddSeconds(40), Workflow.Blocked)
                .Moved(created.AddSeconds(60), Workflow.Review)
                .Moved(created.AddSeconds(80), Workflow.Done);
        }
    }
}
