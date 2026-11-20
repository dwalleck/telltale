using System.Globalization;
using System.Net;
using System.Text;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// GitHub's secondary rate limit (PR timing, review 2, finding 10). GitHub answers it with 403 or 429 while
/// <c>x-ratelimit-remaining</c> need not be 0, often with <c>Retry-After</c>, and asks for a wait of at least
/// a minute when it names none. So on GitHub's client a 403 or 429 that carries a readable <c>Retry-After</c>
/// is waited out whatever the remaining count says, and a 429 that names no wait at all waits a minute,
/// doubling. A 403 with requests left and no <c>Retry-After</c> stays a refusal
/// (<see cref="PullRequestRateLimitTests"/>), and the answers of the other hosts are read as they were. The
/// synthetic hosts are <see cref="FakeGitHub"/> and <see cref="FakeAdo"/>; the fixed clock records every wait.
/// </summary>
public class PullRequestSecondaryRateLimitTests
{
    private const string ReviewsPath = "/api/v3/repos/" + FakeGitHub.Repository + "/pulls/1/reviews";
    private const string ReviewsCall = "GET repos/fake-org/fake-sample/pulls/1/reviews?per_page=100";

    /// <summary>An answer of a secondary limit: the status, requests left in the primary limit, and a wait in <c>Retry-After</c> when given.</summary>
    private static Func<StubRequest, HttpResponseMessage> SecondaryLimit(HttpStatusCode status, int? retryAfterSeconds) => _ =>
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"message":"Synthetic secondary rate limit message naming fake-author-one"}""", Encoding.UTF8, "application/json"),
        };
        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", seconds.ToString(CultureInfo.InvariantCulture));
        }

        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "4120");
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset", "1770145200");
        return response;
    };

    private static (PipelineHarness Pipeline, FakeGitHub GitHub) OnePullRequest()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        return (pipeline, github);
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.TooManyRequests)]
    public async Task A_GitHub_answer_that_asks_for_a_wait_with_requests_left_is_waited_out_and_the_command_goes_on(HttpStatusCode status)
    {
        var (pipeline, github) = OnePullRequest();
        await using var _ = pipeline;
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, ReviewsPath, SecondaryLimit(status, 60));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(60) });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"GitHub is limiting how often it can be asked. Waiting 60 seconds before asking again ({ReviewsCall}).\n");
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1 });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'github'")).ToArray()).IsEquivalentTo(new[] { "read" });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT history FROM pull_request")).ToArray()).IsEquivalentTo(new[] { "read" });
    }

    [Test]
    public async Task A_GitHub_403_that_asks_for_a_wait_longer_than_the_longest_ends_the_run_as_a_limit_and_not_as_a_refusal()
    {
        var (pipeline, _) = OnePullRequest();
        await using var disposed = pipeline;
        pipeline.Jira.Handler.Override(HttpMethod.Get, ReviewsPath, _ => true, SecondaryLimit(HttpStatusCode.Forbidden, 3600));

        var exit = await pipeline.RunAsync("sync");

        // The client gave up: that is never about one pull request, so the read stops and is run again later.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Clock.Waits).IsEmpty();
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"GitHub is limiting how often it can be asked: it answered 403 (Forbidden) to {ReviewsCall} and asked for a wait of more than 5 minutes, or one this tool cannot read. Run the command again later.\n");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task A_GitHub_429_that_names_no_wait_and_no_used_up_limit_waits_a_minute_and_then_twice_as_long()
    {
        var (pipeline, github) = OnePullRequest();
        await using var _ = pipeline;
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, ReviewsPath, SecondaryLimit(HttpStatusCode.TooManyRequests, null), SecondaryLimit(HttpStatusCode.TooManyRequests, null));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120) });
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1, 1 });
    }

    [Test]
    public async Task An_Azure_DevOps_429_that_names_no_wait_still_waits_five_seconds()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        var ado = FakeAdo.For(pipeline);
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 21, 12));
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, FakeAdo.ThreadsPath(1), SecondaryLimit(HttpStatusCode.TooManyRequests, null));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(5) });
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1, 1 });
    }

    [Test]
    public async Task An_Azure_DevOps_403_that_carries_a_Retry_After_is_still_a_refusal_of_that_pull_request()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        var ado = FakeAdo.For(pipeline);
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(1), _ => true, SecondaryLimit(HttpStatusCode.Forbidden, 60));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(pipeline.Clock.Waits).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT history FROM pull_request")).ToArray()).IsEquivalentTo(new[] { "failed" });
    }
}
