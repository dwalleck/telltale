using System.Net;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What a failed thread request says on Azure DevOps (PR timing, review 2, finding 5). Only a failure that is
/// about the pull request asked for (the host refuses its threads, or gives them in a form the tool cannot
/// read) stores that pull request as one whose history was not read and lets the read go on. A client that
/// gave up (retries used up, the time allowed for failures spent, a throttle with a wait too long, a token
/// the host does not take) says nothing of one pull request: it stops the read, as on GitHub, so a host that
/// has stopped answering is not asked once more for every pull request left. The synthetic host is
/// <see cref="FakeAdo"/>.
/// </summary>
public class AdoClientGiveUpTests
{
    private const string HostState = "SELECT state FROM pull_request_host WHERE host = 'ado'";
    private const string Histories = "SELECT pr_number || ': ' || history FROM pull_request WHERE host = 'ado' ORDER BY pr_number";
    private const string ThreadsOfOne = "GET Fake%20Project/_apis/git/repositories/fake-ado-sample/pullRequests/1/threads?api-version=7.1";

    private static (PipelineHarness Pipeline, FakeAdo Ado) AdoOnly()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        return (pipeline, FakeAdo.For(pipeline));
    }

    [Test]
    public async Task A_host_that_stops_answering_thread_requests_stops_the_read_at_the_first_pull_request_although_the_store_holds_them_all_as_not_read()
    {
        // Four open pull requests whose threads are refused at the first sync: each is stored as not read,
        // and none of them counts toward the three failures in a row at a later sync.
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        var answer = StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden);
        foreach (var number in new[] { 1, 2, 4, 5 })
        {
            ado.AddPullRequest(number, AdoIdentities.AuthorOne, Local(2026, 1, 20 + number, 12));
            pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(number), _ => true, request => answer(request));
        }

        // A readable one lies between them, so that the refusals of the first read are not three in a row.
        ado.AddPullRequest(3, AdoIdentities.AuthorTwo, Local(2026, 1, 23, 13)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 24, 12)).Completed(Local(2026, 1, 25, 12));

        await pipeline.RunAllAsync(["sync"]);
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: failed", "2: failed", "3: read", "4: failed", "5: failed" });

        // Now the host answers every thread request with 503. The client sends the first one again five
        // times and gives up; that is the host, and the read stops there.
        answer = StubHttpHandler.Json("""{"message":"Synthetic outage"}""", HttpStatusCode.ServiceUnavailable);
        pipeline.Error.GetStringBuilder().Clear();
        var asked = ado.ThreadsAskedFor.Count;
        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ado.ThreadsAskedFor.Skip(asked).ToArray()).IsEquivalentTo(new[] { 1, 1, 1, 1, 1, 1 });
        await Assert.That(pipeline.Clock.Waits.Count).IsEqualTo(5);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith(
            $"Azure DevOps answered 503 (ServiceUnavailable) to {ThreadsOfOne} again after 5 retries. Run the command again later.\n");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("It is stored without them");
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "failed" });
    }

    [Test]
    public async Task A_throttle_that_asks_for_too_long_a_wait_on_a_thread_request_stops_the_read_and_stores_no_pull_request_as_not_read()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorOne, Local(2026, 1, 21, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(1), _ => true, StubHttpHandler.RateLimited(3600));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Azure DevOps is limiting how often it can be asked: it answered 429 (too many requests) to {ThreadsOfOne} and asked for a wait of more than 5 minutes, or one this tool cannot read. Run the command again later.\n");
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1 });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "failed" });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task A_token_the_host_does_not_take_on_a_thread_request_stops_the_read()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorOne, Local(2026, 1, 21, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(1), _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Unauthorized));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"Azure DevOps answered 401 (Unauthorized) to {ThreadsOfOne}.\n");
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1 });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 0L });
    }
}
