using System.Globalization;
using System.Net;
using System.Text;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// GitHub's rate limit (PR timing, review finding 5). GitHub answers a used-up limit with 403 or 429 and the
/// headers <c>x-ratelimit-remaining: 0</c> and <c>x-ratelimit-reset</c>, the time requests are taken again. A
/// short wait is waited out. A long one stops the command with a message that names the time, and what was
/// read before it is stored, so that the next run goes on from there; the host is then recorded as not read
/// in full, never as read. The synthetic host is <see cref="FakeGitHub"/>; the run's clock reads 3 February
/// 2026, 18:00 UTC, and sprint 42 runs from Monday 19 January to Monday 2 February.
/// </summary>
public class PullRequestRateLimitTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 AND sprint_id IS NOT NULL ORDER BY sprint_id, measure_position";
    private const string HostState = "SELECT state FROM pull_request_host WHERE host = 'github'";
    private const string PullsPath = "/api/v3/repos/" + FakeGitHub.Repository + "/pulls";

    private static readonly DateTimeOffset Now = new(2026, 2, 3, 18, 0, 0, TimeSpan.Zero);

    /// <summary>GitHub's answer to a request over the limit: the status, the two headers, and a body a careless tool might quote.</summary>
    private static Func<StubRequest, HttpResponseMessage> LimitUsedUp(HttpStatusCode status, DateTimeOffset reset) => _ =>
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"message":"Synthetic rate limit message naming fake-author-one"}""", Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "5000");
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
        response.Headers.TryAddWithoutValidation("x-ratelimit-reset", reset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return response;
    };

    [Test]
    [Arguments(HttpStatusCode.Forbidden, "403 (Forbidden)")]
    [Arguments(HttpStatusCode.TooManyRequests, "429 (TooManyRequests)")]
    public async Task A_limit_used_up_part_way_stops_the_command_naming_the_reset_time_and_the_next_run_reads_only_what_is_left(HttpStatusCode status, string answered)
    {
        // Five pull requests, two a page, the lowest number updated last: pages (1, 2), (3, 4), (5). The
        // reviews are read by number, and the limit is used up at pull request 3, for an hour.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.PageSize = 2;
        foreach (var (author, number) in Accounts.Authors.Select((author, index) => (author, index + 1)))
        {
            github.AddPullRequest(number, author, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 27 - number, 12));
        }

        var limited = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath + "/3/reviews", _ => limited, LimitUsedUp(status, Now.AddHours(1)));

        var exit = await pipeline.RunAsync("sync");

        // The command stops without waiting an hour, says when to come back and what it kept, and quotes nothing.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Clock.Waits).IsEmpty();
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"GitHub's rate limit is used up: it answered {answered} to GET repos/fake-org/fake-sample/pulls/3/reviews?per_page=100 and takes requests again at 2026-02-03 19:00:00 UTC. Run the command again after that. "
            + "The reviews of 2 of 5 pull requests were read before that and are stored, and the next sync reads the rest. Until a read of GitHub finishes, a report says that GitHub's figures rest on a read that failed.\n");

        // What was read whole is stored: two pull requests with their reviews. The others are not there at
        // all, and the host is not recorded as read.
        await Assert.That((await pipeline.QueryAsync<string>("SELECT pr_number || ': ' || history FROM pull_request ORDER BY pr_number")).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read" });
        await Assert.That((await pipeline.QueryAsync<long>("SELECT DISTINCT pr_number FROM pull_request_event ORDER BY pr_number")).ToArray()).IsEquivalentTo(new[] { 1L, 2L });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "failed" });

        // A report of the store as it is shows the two it holds and says the read failed: not a host read in full.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT DISTINCT coverage FROM pr_timing_per_sprint")).ToArray()).IsEquivalentTo(new[] { "failed read" });
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("| First code review | 2 | 5.5 |");
        await Assert.That(report).Contains("- GitHub could not be read when pull requests were last collected.");

        // The limit is lifted. The next run asks for the reviews of the three that are left and of no other,
        // although the newest page of the list, pull requests 1 and 2, is as stored: after a read that
        // failed, the list is read to its end.
        limited = false;
        pipeline.Error.GetStringBuilder().Clear();
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 3, 4, 5 });
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "3", "1", "2", "3" });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "read" });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 5L, 4d) });
        await Assert.That(await pipeline.ReadReportAsync(42)).DoesNotContain("GitHub could not be read");

        // And the sync after that one is an ordinary one again: one page, nothing new, no review.
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.PullRequestPagesAsked.Count).IsEqualTo(7);
        await Assert.That(github.ReviewsAskedFor.Count).IsEqualTo(6);
    }

    [Test]
    public async Task A_limit_that_is_lifted_within_minutes_is_waited_out_and_the_command_goes_on()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, PullsPath + "/1/reviews", LimitUsedUp(HttpStatusCode.Forbidden, Now.AddSeconds(90)));

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Clock.Waits).IsEquivalentTo(new[] { TimeSpan.FromSeconds(90) });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "GitHub's rate limit is used up. Waiting 90 seconds before asking again (GET repos/fake-org/fake-sample/pulls/1/reviews?per_page=100).\n");
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1 });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "read" });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 2d) });
    }

    [Test]
    public async Task A_refusal_that_is_no_used_up_limit_is_still_a_refusal_and_is_not_waited_out()
    {
        // 403 with requests left is about the token or the repository: waiting changes nothing.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath, _ => true, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"message":"Synthetic refusal"}""", Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "4999");
            response.Headers.TryAddWithoutValidation("x-ratelimit-reset", Now.AddSeconds(90).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            return response;
        });

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Clock.Waits).IsEmpty();
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "GitHub answered 403 (Forbidden) to GET repos/fake-org/fake-sample/pulls?state=all&sort=updated&direction=desc&per_page=100.\n");
    }

    [Test]
    public async Task Any_failure_part_way_through_the_reviews_keeps_what_was_read_whole_and_one_at_the_first_review_stores_nothing()
    {
        // A wait of an hour asked for with Retry-After ends the run as it always did; what was read is kept too.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12));
        var failing = 1;
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath + "/1/reviews", _ => failing == 1, StubHttpHandler.RateLimited(3600));
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath + "/2/reviews", _ => failing == 2, StubHttpHandler.RateLimited(3600));

        // At the first pull request: nothing was read whole, nothing is stored, and the message says so as before.
        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("are stored");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 0L });

        // At the second: the first is stored, and the message says what was kept.
        failing = 2;
        pipeline.Error.GetStringBuilder().Clear();
        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "GitHub is limiting how often it can be asked: it answered 429 (too many requests) to GET repos/fake-org/fake-sample/pulls/2/reviews?per_page=100 and asked for a wait of more than 5 minutes, or one this tool cannot read. Run the command again later. "
            + "The reviews of 1 of 2 pull requests were read before that and are stored, and the next sync reads the rest. Until a read of GitHub finishes, a report says that GitHub's figures rest on a read that failed.\n");
        await Assert.That((await pipeline.QueryAsync<long>("SELECT pr_number FROM pull_request")).ToArray()).IsEquivalentTo(new[] { 1L });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "failed" });
    }
}
