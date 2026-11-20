using System.Net;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A start date of collection (<c>collectFrom</c>) that is moved forward past pull requests the store already
/// holds (PR timing, review 2, finding 4). The date says which pull requests are brought into the store; one
/// that is in it is kept up to date whatever the date, since derive counts every stored pull request: a
/// stored open one that closed before the new date would otherwise be counted as still waiting for good. A
/// pull request the store never held and that is older than the date is still never asked for. The synthetic
/// hosts are <see cref="FakeGitHub"/> and <see cref="FakeAdo"/>; sprint 42 runs from Monday 19 January to
/// Monday 2 February 2026.
/// </summary>
public class PullRequestStartDateMovedForwardTests
{
    private const string Stored = "SELECT pr_number || ': ' || CASE WHEN closed IS NULL THEN 'open' ELSE 'closed' END || ', ' || history FROM pull_request ORDER BY pr_number";

    [Test]
    public async Task A_stored_open_pull_request_that_closed_before_the_new_start_date_is_stored_as_closed_and_no_longer_counted_as_waiting()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        pipeline.Config["github"]!["collectFrom"] = "2026-01-10";
        var five = github.AddPullRequest(5, Accounts.AuthorOne, Local(2026, 1, 12, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That(await PullRequestsWithoutReviewTests.CountsAsync(pipeline, "sprint_id = '42'")).IsEquivalentTo(new[] { ("42", "github", 1L, 0L, 1L, 0L, (long?)0L, 0L, 0L) });

        // It is closed without a merge the next day, and the start date is then moved past that.
        five.Closed(Local(2026, 1, 13, 12));
        pipeline.Config["github"]!["collectFrom"] = "2026-01-15";
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "5: closed, read" });
        await Assert.That((await PullRequestsWithoutReviewTests.CountsAsync(pipeline, "sprint_id = '42'")).Select(row => row.Waiting).ToArray()).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task A_page_of_pull_requests_too_old_to_collect_does_not_end_the_list_before_a_stored_one_further_down_and_none_of_them_is_asked_for()
    {
        // Two a page. 1 to 4 were last updated before the first start date and are never stored; 5 is stored, open.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.PageSize = 2;
        pipeline.Config["github"]!["collectFrom"] = "2026-01-10";
        var old = new[] { 1, 2, 3, 4 }.Select(number => github.AddPullRequest(number, Accounts.AuthorTwo, Local(2026, 1, 5, 12)).AsDraft()).ToList();
        var five = github.AddPullRequest(5, Accounts.AuthorOne, Local(2026, 1, 12, 12));

        // The first read goes (5, 4), then (3, 2): a page of nothing but pull requests too old to collect,
        // with no stored one left to come to, ends the list before its last page.
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2" });
        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "5: open, read" });

        // 5 closes on the 13th. The four old ones are touched on the 14th, and 6 is opened on the 20th. The
        // start date moves to the 15th, so the list now goes (6, 4), (3, 2), (1, 5): the second page holds
        // only pull requests that are too old and were never stored, and the stored one comes after it.
        five.Closed(Local(2026, 1, 13, 12));
        old.ForEach(pullRequest => pullRequest.MarkedReady(Local(2026, 1, 14, 12)));
        github.AddPullRequest(6, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Config["github"]!["collectFrom"] = "2026-01-15";
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "5: closed, read", "6: open, read" });
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "1", "2", "3" });

        // Never stored and older than the date: never asked for, at either read.
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 5, 5, 6 });
    }

    [Test]
    public async Task On_Azure_DevOps_a_stored_pull_request_closed_before_the_new_start_date_is_brought_up_to_date_and_one_never_stored_is_never_asked_for()
    {
        var pipeline = SuppressionOff();
        await using var _ = pipeline;
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        var ado = FakeAdo.For(pipeline);
        pipeline.Config["ado"]!["collectFrom"] = "2026-01-10";

        // 1 is open. 2 was completed on the 13th and its threads are refused at the first read. 3 was completed before the first date.
        var one = ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 12, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorOne, Local(2026, 1, 12, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 13, 9)).Completed(Local(2026, 1, 13, 12));
        ado.AddPullRequest(3, AdoIdentities.AuthorTwo, Local(2026, 1, 6, 12)).Completed(Local(2026, 1, 8, 12));
        var refused = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(2), _ => refused, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));

        await pipeline.RunAllAsync(["sync"]);
        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "1: open, read", "2: closed, failed" });

        // 1 is abandoned on the 13th, 2's threads can be read, and the start date moves to the 15th.
        one.Abandoned(Local(2026, 1, 13, 15));
        refused = false;
        pipeline.Config["ado"]!["collectFrom"] = "2026-01-15";
        await pipeline.RunAllAsync(["run", "--sprint", "42"], ["sync"]);

        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "1: closed, read", "2: closed, read" });
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1, 2, 1, 2 });
    }
}
