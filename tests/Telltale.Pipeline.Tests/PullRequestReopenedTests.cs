using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A pull request that was closed and is opened again (reopened on GitHub, reactivated on Azure DevOps): the
/// host then lists it with no closing time, and the sprints that had already closed must still say what it
/// was when they closed. Sprint 41 runs from Monday 5 January 2026 to Monday the 19th; sprint 42 is running.
/// The first collection is on Tuesday 27 January, the reopening on Wednesday the 28th, and the collection
/// that sees it on Thursday the 29th.
/// </summary>
public class PullRequestReopenedTests
{
    private const string WaitingAndClosed = "SELECT sprint_id, host_scope, waiting, closed_without_review FROM pr_without_review_per_sprint ORDER BY sprint_id;";

    /// <summary>Suppression off, sprint 41 closed and sprint 42 running, on Tuesday 27 January at noon.</summary>
    private static PipelineHarness InSprint42()
    {
        var pipeline = SuppressionOff(Local(2026, 1, 27, 12));
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddSprint42(stillRunning: true);
        return pipeline;
    }

    private static string Rows(string host, string sprint41, string sprint42) =>
        "| sprint_id | host_scope | waiting | closed_without_review |\n| --- | --- | --- | --- |\n"
        + $"| 41 | {host} | {sprint41} |\n| 42 | {host} | {sprint42} |\n";

    [Test]
    public async Task A_pull_request_closed_without_review_in_a_sprint_and_reopened_in_a_later_one_stays_closed_without_one_in_the_first()
    {
        await using var pipeline = InSprint42();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Closed(Local(2026, 1, 9, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var closedInSprint41 = PullRequestsWithoutReviewTests.TableHead + "| GitHub | 0 | 1 | 1 | 0 | 0 | 0 | 0 |\n";
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(closedInSprint41);

        pullRequest.Reopened(Local(2026, 1, 28, 12));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        // Sprint 41 is as it was: closed in it without a review. It waits in the running sprint, where it is open.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(closedInSprint41);
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("github", "0 | 1", "1 | 0"));

        // The closure the store saw is a raw fact: a backfill, which reads the host whole, does not lose it.
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("github", "0 | 1", "1 | 0"));
    }

    [Test]
    public async Task A_pull_request_closed_before_a_sprint_began_and_reopened_after_it_closed_is_not_added_to_that_sprints_waiting()
    {
        await using var pipeline = InSprint42();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2025, 12, 10, 12)).Closed(Local(2025, 12, 20, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var noneInSprint41 = PullRequestsWithoutReviewTests.TableHead + "| GitHub | 0 | 0 | 0 | 0 | 0 | 0 | 0 |\n";
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(noneInSprint41);

        pullRequest.Reopened(Local(2026, 1, 28, 12));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        // It was closed all through sprint 41, which has nothing of it to explain. It waits in the running sprint.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(noneInSprint41);
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("github", "0 | 0", "1 | 0"));
    }

    [Test]
    public async Task A_pull_request_closed_again_keeps_both_closures_each_in_its_own_sprint()
    {
        await using var pipeline = InSprint42();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Closed(Local(2026, 1, 9, 12));
        await pipeline.RunAllAsync(["sync"]);

        // Reopened and closed again between two collections: the second one sees only another closing time.
        pullRequest.Reopened(Local(2026, 1, 28, 12)).Closed(Local(2026, 1, 28, 15));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["sync"], ["derive"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("github", "0 | 1", "0 | 1"));
    }

    [Test]
    public async Task An_abandoned_Azure_DevOps_pull_request_that_is_reactivated_stays_closed_without_a_review_in_the_sprint_it_was_abandoned_in()
    {
        await using var pipeline = InSprint42();
        var ado = FakeAdo.For(pipeline);
        var pullRequest = ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 6, 12)).Abandoned(Local(2026, 1, 9, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        var closedInSprint41 = PullRequestsWithoutReviewTests.TableHead + "| Azure DevOps | 0 | 1 | 1 | 0 | no Ops group | 0 | 0 |\n";
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(closedInSprint41);

        pullRequest.Reactivated();
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(closedInSprint41);
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("ado", "0 | 1", "1 | 0"));
    }
}
