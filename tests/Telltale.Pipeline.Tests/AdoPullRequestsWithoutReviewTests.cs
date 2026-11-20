using System.Net;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The pull requests without a first code review on Azure DevOps, and on both hosts together (PR timing,
/// ticket 07): a history that was read and is empty is told apart from one that could not be read, which
/// withholds that pull request's review waits and nothing else. The synthetic hosts are <see cref="FakeAdo"/>
/// and <see cref="FakeGitHub"/>; sprint 42 runs from Monday 19 January to Monday 2 February 2026.
/// </summary>
public class AdoPullRequestsWithoutReviewTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 AND sprint_id IS NOT NULL ORDER BY sprint_id, measure_position";
    private const string Histories = "SELECT pr_number || ': ' || history FROM pull_request WHERE host = 'ado' ORDER BY pr_number";

    private static readonly Func<StubRequest, HttpResponseMessage> Refusal = StubHttpHandler.Json("""{"message":"Synthetic refusal naming Fake Ado Reviewer One"}""", HttpStatusCode.Forbidden);

    private static async Task<(string, string, long, double)[]> FiguresAsync(PipelineHarness pipeline) =>
        [.. await pipeline.QueryAsync<(string, string, long, double)>(Figures)];

    /// <summary>A harness with suppression off, sprints 41 and 42 closed, no GitHub token, and a synthetic Azure DevOps.</summary>
    private static (PipelineHarness Pipeline, FakeAdo Ado) AdoOnly()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        return (pipeline, FakeAdo.For(pipeline));
    }

    [Test]
    public async Task Threads_that_cannot_be_read_leave_one_pull_request_not_read_while_an_empty_history_is_waiting_with_no_activity()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;

        // Four pull requests opened in sprint 42. The first has no thread at all. The second was reviewed and
        // completed, and its threads are refused. The third has only the scanner's comment. The fourth was reviewed.
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).FileComment(AdoIdentities.Reviewer, Local(2026, 1, 21, 12)).Completed(Local(2026, 1, 22, 12));
        ado.AddPullRequest(3, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12)).FileComment(AdoIdentities.Scanner, Local(2026, 1, 20, 13));
        ado.AddPullRequest(4, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 23, 12));
        var refused = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(2), _ => refused, Refusal);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        // The command goes on, says what it could not read without quoting the answer, and the row records it.
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).Contains(
            "Azure DevOps: the threads of a pull request could not be read (Azure DevOps answered 403 (Forbidden) to GET Fake%20Project/_apis/git/repositories/fake-ado-sample/pullRequests/2/threads?api-version=7.1.). "
            + "It is stored without them and counted as a pull request whose history was not read; its threads are asked for again at the next sync.\n");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("Synthetic refusal");
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: failed", "3: read", "4: read" });

        // The unread pull request has no review wait, and its completion is still a merge. The others' waits are given.
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 3d), ("42", "first_code_approval", 1L, 3d), ("42", "merge", 1L, 2d) });
        await Assert.That(await PullRequestsWithoutReviewTests.CountsAsync(pipeline, "sprint_id = '42'")).IsEquivalentTo(new[] { ("42", "ado", 2L, 0L, 1L, 1L, (long?)null, 0L, 1L) });
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains(PullRequestsWithoutReviewTests.TableHead + "| Azure DevOps | 2 | 0 | 1 | 1 | no Ops group | 0 | 1 |\n\n- Still waiting");
        await Assert.That(report).Contains("Ops only: something by the Ops group, whatever automation did besides; Azure DevOps has no Ops group set apart. Automation only:");
        await Assert.That(report).DoesNotContain("Not shown");

        // At the next sync the threads are asked for again although the pull request is closed and unchanged,
        // and its review counts: it leaves the count, and nothing else moves.
        refused = false;
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(ado.ThreadsAskedFor.Count(number => number == 2)).IsEqualTo(2);
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: read", "4: read" });
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 2L, 2d), ("42", "first_code_approval", 1L, 3d), ("42", "merge", 1L, 2d) });
        await Assert.That(await PullRequestsWithoutReviewTests.CountsAsync(pipeline, "sprint_id = '42' AND host_scope = 'ado'")).IsEquivalentTo(new[] { ("42", "ado", 2L, 0L, 1L, 1L, (long?)null, 0L, 0L) });
    }

    [Test]
    public async Task When_the_threads_of_one_pull_request_after_another_cannot_be_read_the_host_is_not_read_and_nothing_of_it_is_stored()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        foreach (var number in new[] { 1, 2, 3 })
        {
            ado.AddPullRequest(number, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
            pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(number), _ => true, Refusal);
        }

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'ado'")).ToArray()).IsEquivalentTo(new[] { "failed" });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task With_both_hosts_each_has_its_row_and_the_combined_row_counts_every_pull_request_once()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        var ado = FakeAdo.For(pipeline);

        // GitHub: one waiting on an Ops approval alone, one closed with nothing. Azure DevOps: one waiting with nothing.
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 21, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Closed(Local(2026, 1, 22, 12));
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, PullRequestsWithoutReviewTests.Counts + " WHERE sprint_id = '42';")).IsEqualTo(
            """
            | sprint_id | host_scope | waiting | closed_without_review | no_activity | automation_only | ops_only | unclassifiable | history_not_read |
            | --- | --- | --- | --- | --- | --- | --- | --- | --- |
            | 42 | github | 1 | 1 | 1 | 0 | 1 | 0 | 0 |
            | 42 | ado | 1 | 0 | 1 | 0 | NULL | 0 | 0 |
            | 42 | combined | 2 | 1 | 2 | 0 | 1 | 0 | 0 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            PullRequestsWithoutReviewTests.TableHead
            + "| GitHub | 1 | 1 | 1 | 0 | 1 | 0 | 0 |\n"
            + "| Azure DevOps | 1 | 0 | 1 | 0 | no Ops group | 0 | 0 |\n"
            + "| Both hosts | 2 | 1 | 2 | 0 | 1 | 0 | 0 |\n\n- Still waiting");
    }
}
