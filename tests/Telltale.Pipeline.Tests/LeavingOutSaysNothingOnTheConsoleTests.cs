using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Leaving brief visits out changes no changelog anomaly and nothing <c>derive</c> or <c>run</c> says on the
/// console, for time in status, for cycle time and for both (user story 39 of the brief visit treatment spec).
/// The console is not behind the metric views, so a line there about what was left out would print a number
/// the footer may withhold: the only recorded exception for the console is the anomaly totals (owner decision D37).
/// </summary>
public class LeavingOutSaysNothingOnTheConsoleTests
{
    [Test]
    [Arguments("exclude", "count", "time_in_status_per_sprint")]
    [Arguments("count", "exclude", "cycle_time_per_sprint_and_bucket")]
    [Arguments("exclude", "exclude", "cycle_time_per_sprint_and_bucket")]
    public async Task Leaving_out_changes_no_anomaly_and_nothing_derive_or_run_says_on_the_console(string timeInStatus, string cycleTime, string aViewThatDiffers)
    {
        // The same Jira twice, with Development listed: once counted, once left out. Four people's 3-point
        // issues were caught up and delivered, so each has a cycle that starts with a brief visit; three more
        // did four days in Development. One issue has a changelog anomaly, so the anomaly count has
        // something to count.
        await using var counted = new PipelineHarness();
        await using var leftOut = new PipelineHarness();
        foreach (var pipeline in new[] { counted, leftOut })
        {
            pipeline.Jira.AddSprint41();
            pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
            foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
            {
                Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person).Estimated(3);
            }

            foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 11)))
            {
                Scenarios.Delivered(pipeline.Jira, 41, $"FAKE-{number}", points: 3, person, days: 4);
            }

            pipeline.Jira.AddIssue("FAKE-9", Local(2026, 1, 5, 9)).AssignedTo(People.Five).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .NowIn(Workflow.Review);
            pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        }

        leftOut.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = timeInStatus, ["cycleTime"] = cycleTime };

        await counted.RunAllAsync(["backfill"], ["derive"], ["run", "--sprint", "41"]);
        await leftOut.RunAllAsync(["backfill"], ["derive"], ["run", "--sprint", "41"]);

        // The console says the same under either treatment, and the anomalies are the same.
        var storeCounted = BlocksOf(await counted.ReadStoreContentAsync());
        var storeLeftOut = BlocksOf(await leftOut.ReadStoreContentAsync());
        await Assert.That(leftOut.Error.ToString()).IsEqualTo(counted.Error.ToString());
        await Assert.That(leftOut.Out.ToString().Replace(leftOut.Directory, counted.Directory, StringComparison.Ordinal)).IsEqualTo(counted.Out.ToString());
        await Assert.That(counted.Error.ToString()).Contains("Changelog anomalies: 1 across 1 issue");
        await Assert.That(storeLeftOut["anomaly"]).IsEqualTo(storeCounted["anomaly"]);
        await Assert.That(storeLeftOut["anomaly"]).Contains(leftOut.Jira.IdOf("FAKE-9"));
        await Assert.That(storeLeftOut["data_quality"]).IsEqualTo(storeCounted["data_quality"]);

        // And the two did measure differently.
        await Assert.That(storeLeftOut[aViewThatDiffers]).IsNotEqualTo(storeCounted[aViewThatDiffers]);
    }

    /// <summary>The store's content (<see cref="PipelineHarness.ReadStoreContentAsync"/>) as one block per table or view, by its name; a block starts with the name.</summary>
    private static Dictionary<string, string> BlocksOf(string storeContent) =>
        ("\n" + storeContent).Split("\n## ", StringSplitOptions.RemoveEmptyEntries).ToDictionary(block => block[..block.IndexOf('\n')], block => block + "\n", StringComparer.Ordinal);
}
