using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A stay whose start or end comes from a changelog anomaly is not counted as a brief visit, however short it
/// looks (GLOSSARY: Brief visit; the owner's decision of 2026-11-13, bv-D15 of the brief-status-visits run),
/// through <c>derive</c> and then <c>query</c>. Every test is about one issue of one person, with both numbers
/// of the minimum group size at zero, so that the count is shown whatever it is. Each expected row also gives
/// the issue count of the status's time in status row: the issue is in the row, and only the brief visit count
/// says whether its stay was counted. Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00.
/// </summary>
public class AnomalyBoundedStaysTests
{
    private const string CountsSql =
        """
        SELECT b.sprint_id, b.status_name, t.issue_count AS issues_in_the_row, b.brief_visit_issue_count
        FROM brief_visits_per_sprint b
        JOIN time_in_status_per_sprint t ON t.sprint_id = b.sprint_id AND t.status_id = b.status_id
        ORDER BY b.sprint_id, b.board_position;
        """;

    private const string CountsHead =
        "| sprint_id | status_name | issues_in_the_row | brief_visit_issue_count |\n| --- | --- | --- | --- |\n";

    [Test]
    public async Task A_stay_that_begins_with_a_change_dated_before_the_issue_was_created_is_not_counted_though_it_ended_two_minutes_after_the_creation()
    {
        // Created on Monday 5 January at 10:00, inside the sprint. The move into Development is dated the day
        // before, and the move out of it two minutes after the creation: the timeline has two minutes of
        // Development, which put the issue in Development's row, and the stay as recorded began a day earlier.
        await using var pipeline = SprintFortyOne();
        var created = Local(2026, 1, 5, 10);
        pipeline.Jira.AddIssue("FAKE-1", created).AssignedTo(People.One).InSprints(41)
            .Moved(created.AddDays(-1), Workflow.Development)
            .Moved(created.AddMinutes(2), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + "| 41 | Fake Development | 1 | 0 |\n");
    }

    [Test]
    public async Task A_stay_ended_by_a_change_that_names_another_from_status_is_not_counted()
    {
        // In Development from Tuesday 09:00. Five seconds later the changelog has a move from Review to QE: the
        // move from Development to Review is missing, so nothing says when the issue left Development.
        await using var pipeline = SprintFortyOne();
        var tuesday = Local(2026, 1, 6, 9);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).AssignedTo(People.One).InSprints(41)
            .Moved(tuesday, Workflow.Development)
            .AddHistory(tuesday.AddSeconds(5), "status", "status", Workflow.Review.Id, "Fake Review", Workflow.QE.Id, "Fake QE")
            .NowIn(Workflow.QE)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await Assert.That(await CountsAfterDeriveAsync(pipeline)).IsEqualTo(CountsHead + "| 41 | Fake Development | 1 | 0 |\n");
    }

    /// <summary>Runs <c>backfill</c> and <c>derive</c>, and reads the counts with <c>query</c>.</summary>
    private static async Task<string> CountsAfterDeriveAsync(PipelineHarness pipeline)
    {
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        return await QueryAsync(pipeline, CountsSql);
    }

    /// <summary>A store of sprint 41 alone, with small-group suppression off.</summary>
    private static PipelineHarness SprintFortyOne()
    {
        var pipeline = Scenarios.SuppressionOff();
        pipeline.Jira.AddSprint41();
        return pipeline;
    }
}
