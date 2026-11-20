using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The pull requests that give no first code review to a sprint's figures, counted apart from the waits (PR
/// timing, ticket 07): still waiting when the sprint closed, or closed in it without one, and what had been
/// submitted by then. The counts are of GitHub's pull requests here (<see cref="FakeGitHub"/>); sprint 41 runs
/// from Monday 5 January to Monday 19 January 2026, sprint 42 from then to Monday 2 February.
/// </summary>
public class PullRequestsWithoutReviewTests
{
    internal const string Counts =
        "SELECT sprint_id, host_scope, waiting, closed_without_review, no_activity, automation_only, ops_only, unclassifiable, history_not_read FROM pr_without_review_per_sprint";

    internal const string TableHead =
        "\n### Without a first code review\n\n"
        + "Pull requests of the configured repositories that had no first code review when the sprint closed, or at the as-of time for a sprint that is still running. "
        + "These are numbers of pull requests, not waits: none of them is in the first code review row above, and none is counted as a wait of zero.\n\n"
        + "| Host | Still waiting | Closed without one | No activity | Automation only | Ops only | Cannot be classified | History not read |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n";

    internal static async Task<(string Sprint, string Scope, long Waiting, long Closed, long NoActivity, long AutomationOnly, long? OpsOnly, long Unclassifiable, long NotRead)[]> CountsAsync(PipelineHarness pipeline, string where) =>
        [.. await pipeline.QueryAsync<(string, string, long, long, long, long, long?, long, long)>(
            $"{Counts} WHERE {where} ORDER BY sprint_id, CASE host_scope WHEN 'github' THEN 1 WHEN 'ado' THEN 2 ELSE 3 END")];

    /// <summary>
    /// Sprint 41 with four Jira assignees, which the default numbers withhold whole, and three pull requests by
    /// one author: one reviewed only after the sprint closed, one closed in the sprint with no review, and one
    /// reviewed in the sprint.
    /// </summary>
    private static void AddAFourPersonSprintAndThreePullRequests(PipelineHarness pipeline)
    {
        AddSprint41WithFourAssignees(pipeline.Jira);
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 20, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 7, 12)).Closed(Local(2026, 1, 9, 12));
        github.AddPullRequest(3, Accounts.AuthorOne, Local(2026, 1, 8, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 9, 12));
    }

    [Test]
    public async Task A_review_after_the_sprint_closed_leaves_the_pull_request_waiting_at_its_close_and_the_report_shows_the_counts_by_the_default_numbers()
    {
        await using var pipeline = new PipelineHarness();
        AddAFourPersonSprintAndThreePullRequests(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        // The Jira figures of the same run are withheld; the pull request counts are not (owner decision D114).
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).Contains(TableHead + "| GitHub | 1 | 1 | 2 | 0 | 0 | 0 | 0 |\n\n- Still waiting");

        // Only the pull request reviewed in the sprint has a wait: neither of the other two is a wait of zero.
        await Assert.That(report).Contains("| First code review | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n");
    }

    [Test]
    public async Task By_the_default_numbers_the_owner_query_gives_the_same_counts_and_still_withholds_the_jira_figures()
    {
        await using var pipeline = new PipelineHarness();
        AddAFourPersonSprintAndThreePullRequests(pipeline);
        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, "SELECT * FROM pr_without_review_per_sprint;", "SELECT sprint_id, delivered_count, suppressed FROM delivered_per_sprint;")).IsEqualTo(
            """
            | sprint_id | host_scope | waiting | closed_without_review | no_activity | automation_only | ops_only | unclassifiable | history_not_read |
            | --- | --- | --- | --- | --- | --- | --- | --- | --- |
            | 41 | github | 1 | 1 | 2 | 0 | 0 | 0 | 0 |
            | 41 | combined | 1 | 1 | 2 | 0 | 0 | 0 | 0 |
            | sprint_id | delivered_count | suppressed |
            | --- | --- | --- |
            | 41 | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }
}
