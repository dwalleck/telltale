using System.Globalization;
using System.Net;
using System.Text;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Whose clock a wait for GitHub's reset is measured by (PR timing, review 2, finding 14). The reset in
/// <c>x-ratelimit-reset</c> is a time on GitHub's clock, and this machine's clock can run ahead of it. So the
/// wait is counted from the time the answer itself gives in its <c>Date</c> header, with a few seconds added,
/// and from this machine's clock only when the answer has no such header. And when GitHub repeats a used-up
/// limit after the wait, the next waits grow: the retries that are left are not spent a second apart. The
/// synthetic host is <see cref="FakeGitHub"/>; the fixed clock records every wait and moves by each.
/// </summary>
public class PullRequestRateLimitClockTests
{
    private const string ReviewsPath = "/api/v3/repos/" + FakeGitHub.Repository + "/pulls/1/reviews";
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 AND sprint_id IS NOT NULL ORDER BY sprint_id, measure_position";

    /// <summary>GitHub's answer to a request over the limit, with the time on GitHub's own clock in <c>Date</c> when given.</summary>
    private static HttpResponseMessage LimitUsedUp(DateTimeOffset reset, DateTimeOffset? hostTime)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"message":"Synthetic rate limit message naming fake-author-one"}""", Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        response.Headers.Date = hostTime;
        return response;
    }

    /// <summary>
    /// A GitHub whose clock is <paramref name="behind"/> this machine's and whose limit is used up until
    /// <paramref name="reset"/> on its own clock: the reviews of pull request 1 are refused until then.
    /// </summary>
    private static (PipelineHarness Pipeline, FakeGitHub GitHub) LimitedUntil(Func<PipelineHarness, DateTimeOffset> reset, TimeSpan behind, bool saysItsTime)
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        var liftedAt = reset(pipeline);
        pipeline.Jira.Handler.Override(
            HttpMethod.Get, ReviewsPath,
            _ => pipeline.Clock.GetUtcNow() - behind < liftedAt,
            _ => LimitUsedUp(liftedAt, saysItsTime ? pipeline.Clock.GetUtcNow() - behind : null));
        return (pipeline, github);
    }

    [Test]
    public async Task A_wait_for_the_reset_is_counted_from_the_time_the_answer_gives_when_this_machines_clock_runs_ahead()
    {
        // This machine's clock is 10 seconds ahead of GitHub's, and the limit is lifted 90 seconds from now
        // on GitHub's clock: 80 seconds from now on this machine's, which is 10 seconds too early.
        var (pipeline, github) = LimitedUntil(pipeline => pipeline.Clock.GetUtcNow().AddSeconds(80), behind: TimeSpan.FromSeconds(10), saysItsTime: true);
        await using var _ = pipeline;

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        // One wait: the 90 seconds GitHub's own time leaves, and 5 more.
        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(95) });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "GitHub's rate limit is used up. Waiting 95 seconds before asking again (GET repos/fake-org/fake-sample/pulls/1/reviews?per_page=100).\n");
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1 });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 2d) });
    }

    [Test]
    public async Task A_reset_that_is_more_than_the_longest_wait_away_on_the_hosts_clock_ends_the_run_though_this_machines_clock_is_nearer_to_it()
    {
        // 295 seconds away on this machine's clock, 305 on GitHub's.
        var (pipeline, _) = LimitedUntil(pipeline => pipeline.Clock.GetUtcNow().AddSeconds(295), behind: TimeSpan.FromSeconds(10), saysItsTime: true);
        await using var disposed = pipeline;

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Clock.Waits).IsEmpty();
        await Assert.That(pipeline.Error.ToString()).Contains("GitHub's rate limit is used up: it answered 403 (Forbidden) to GET repos/fake-org/fake-sample/pulls/1/reviews?per_page=100 and takes requests again at 2026-02-03 18:04:55 UTC. Run the command again after that.");
    }

    [Test]
    public async Task A_used_up_limit_repeated_after_the_wait_is_waited_on_longer_each_time_and_not_a_second_apart()
    {
        // The answer names no time of its own, so this machine's clock is all there is, and it is 12 seconds
        // ahead: after the wait to the reset GitHub still refuses, for 12 seconds more.
        var (pipeline, github) = LimitedUntil(pipeline => pipeline.Clock.GetUtcNow().AddSeconds(90), behind: TimeSpan.FromSeconds(12), saysItsTime: false);
        await using var _ = pipeline;

        var exit = await pipeline.RunAsync("sync");

        // 90 seconds to the reset as this machine reads it, then 5 and 10: the third wait ends past the 12 seconds.
        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) });
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1, 1, 1 });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'github'")).ToArray()).IsEquivalentTo(new[] { "read" });
    }
}
