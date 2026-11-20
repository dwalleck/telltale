using System.Globalization;
using System.Net;
using System.Text;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Whose rule a throttled answer is read by (PR timing, review 2, finding 2). GitHub says a used-up limit with
/// <c>x-ratelimit-remaining: 0</c> and the time requests are taken again in <c>x-ratelimit-reset</c>. Azure
/// DevOps sends headers of the same names with another meaning (the reset is when its tracked usage is back to
/// nothing) and asks its clients to wait what <c>Retry-After</c> says. So the reset is read only of GitHub's
/// answers, and a readable <c>Retry-After</c> is what is waited on either host. The synthetic hosts are
/// <see cref="FakeAdo"/> and <see cref="FakeGitHub"/>; the fixed clock records every wait.
/// </summary>
public class PullRequestThrottlingByHostTests
{
    private const string ThreadsCall = "GET Fake%20Project/_apis/git/repositories/fake-ado-sample/pullRequests/1/threads?api-version=7.1";

    /// <summary>A throttled answer: the status, a wait in <c>Retry-After</c> when given, no requests remaining and a reset so many seconds from the clock's now.</summary>
    private static Func<StubRequest, HttpResponseMessage> Throttled(PipelineHarness pipeline, HttpStatusCode status, int? retryAfterSeconds, int resetInSeconds) => _ =>
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"message":"Synthetic throttling message naming Fake Ado Reviewer One"}""", Encoding.UTF8, "application/json"),
        };
        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", seconds.ToString(CultureInfo.InvariantCulture));
        }

        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", pipeline.Clock.GetUtcNow().AddSeconds(resetInSeconds).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return response;
    };

    private static (PipelineHarness Pipeline, FakeAdo Ado) AdoOnly()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        return (pipeline, FakeAdo.For(pipeline));
    }

    [Test]
    [Arguments(290)]
    [Arguments(400)]
    public async Task Azure_DevOps_throttling_is_waited_for_as_long_as_Retry_After_says_whatever_its_reset_header_says(int resetInSeconds)
    {
        // A reset 290 seconds away would be waited out by GitHub's rule, and one 400 seconds away would stop
        // the command at once: neither is what Azure DevOps asked for, which is 30 seconds.
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 21, 12));
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, FakeAdo.ThreadsPath(1), Throttled(pipeline, HttpStatusCode.TooManyRequests, 30, resetInSeconds));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(30) });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Azure DevOps is limiting how often it can be asked. Waiting 30 seconds before asking again ({ThreadsCall}).\n");
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1, 1 });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'ado'")).ToArray()).IsEquivalentTo(new[] { "read" });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT history FROM pull_request")).ToArray()).IsEquivalentTo(new[] { "read" });
    }

    [Test]
    public async Task An_Azure_DevOps_refusal_with_no_requests_remaining_is_a_refusal_of_that_pull_request_and_is_not_waited_out()
    {
        // 403 is not how Azure DevOps throttles. With the headers GitHub's rule looks for it is still a refusal.
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(1), _ => true, Throttled(pipeline, HttpStatusCode.Forbidden, null, 90));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(pipeline.Clock.Waits).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString()).Contains($"Azure DevOps answered 403 (Forbidden) to {ThreadsCall}.");
        await Assert.That((await pipeline.QueryAsync<string>("SELECT history FROM pull_request")).ToArray()).IsEquivalentTo(new[] { "failed" });
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.TooManyRequests)]
    public async Task GitHub_too_waits_what_a_readable_Retry_After_says_on_an_answer_that_says_its_limit_is_used_up(HttpStatusCode status)
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, "/api/v3/repos/" + FakeGitHub.Repository + "/pulls/1/reviews", Throttled(pipeline, status, 30, 290));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(30) });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "GitHub's rate limit is used up. Waiting 30 seconds before asking again (GET repos/fake-org/fake-sample/pulls/1/reviews?per_page=100).\n");
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1 });
    }
}
