using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A GitHub repository that is listed again after a read of it stopped part-way (PR timing, review 2, finding
/// 17). A sync stops reading a repository's list at the first page of stored, unchanged pull requests, which
/// is right only for a repository the last finished read of the host went through: of any other the store may
/// hold some pull requests and not others. So a repository that is not among those the store records for the
/// host (<c>pull_request_host.repositories</c>) has its whole list read, at a sync too. The synthetic host
/// is <see cref="FakeGitHub"/>, with a second repository that has no pull request.
/// </summary>
public class PullRequestRepositoryReaddedTests
{
    private const string OtherRepository = "fake-org/fake-other";
    private const string PullsPath = "/api/v3/repos/" + FakeGitHub.Repository + "/pulls";
    private const string Recorded = "SELECT state || ': ' || COALESCE(repositories, 'none') FROM pull_request_host WHERE host = 'github'";
    private const string StoredNumbers = "SELECT pr_number FROM pull_request ORDER BY pr_number";

    private static void Configure(PipelineHarness pipeline, params string[] repositories) =>
        pipeline.Config["github"]!["repositories"] = new JsonArray([.. repositories.Select(repository => (JsonNode)repository)]);

    [Test]
    public async Task A_repository_listed_again_after_a_stopped_read_has_its_whole_list_read_by_the_next_sync()
    {
        // Five pull requests, two a page, the lowest number updated last: pages (1, 2), (3, 4), (5). The
        // reviews are read by number, and the limit is used up at pull request 3, for an hour: 1 and 2 are
        // stored, 3 to 5 are not, and the read failed.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        pipeline.Jira.Handler.On(HttpMethod.Get, "/api/v3/repos/" + OtherRepository + "/pulls", StubHttpHandler.Json("[]"));
        Configure(pipeline, OtherRepository, FakeGitHub.Repository);
        github.PageSize = 2;
        foreach (var (author, number) in Accounts.Authors.Select((author, index) => (author, index + 1)))
        {
            github.AddPullRequest(number, author, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 27 - number, 12));
        }

        var limited = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath + "/3/reviews", _ => limited, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
            response.Headers.TryAddWithoutValidation("x-ratelimit-reset", pipeline.Clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            return response;
        });

        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);

        // The stopped read does not record a repository as gone through: not the one it stopped in, and not
        // the one before it either, since the record is of the last read that finished.
        await Assert.That((await pipeline.QueryAsync<string>(Recorded)).ToArray()).IsEquivalentTo(new[] { "failed: none" });
        await Assert.That((await pipeline.QueryAsync<long>(StoredNumbers)).ToArray()).IsEquivalentTo(new[] { 1L, 2L });

        // The owner takes the repository out and syncs: the host is read, and only the other repository was gone through.
        limited = false;
        Configure(pipeline, OtherRepository);
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That((await pipeline.QueryAsync<string>(Recorded)).ToArray()).IsEquivalentTo(new[] { $"read: [\"{OtherRepository}\"]" });
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "3" });

        // The repository is listed again. Its first page, (1, 2), is as stored; the read goes on to the end
        // all the same, and asks for the reviews of the three the store lacks.
        Configure(pipeline, OtherRepository, FakeGitHub.Repository);
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "3", "1", "2", "3" });
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 3, 4, 5 });
        await Assert.That((await pipeline.QueryAsync<long>(StoredNumbers)).ToArray()).IsEquivalentTo(new[] { 1L, 2L, 3L, 4L, 5L });
        await Assert.That((await pipeline.QueryAsync<string>(Recorded)).ToArray()).IsEquivalentTo(new[] { $"read: [\"{OtherRepository}\",\"{FakeGitHub.Repository}\"]" });

        // Now that a finished read went through it, the next sync stops at its first page again.
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.PullRequestPagesAsked.Count).IsEqualTo(7);
        await Assert.That(github.ReviewsAskedFor.Count).IsEqualTo(6);
    }

    [Test]
    public async Task A_read_that_stops_in_a_repository_listed_for_the_first_time_does_not_record_it_and_the_next_sync_reads_it_whole()
    {
        // The other repository alone is read first, and the host is 'read'. Then the sample repository is
        // listed, and the read of it stops at pull request 3.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        pipeline.Jira.Handler.On(HttpMethod.Get, "/api/v3/repos/" + OtherRepository + "/pulls", StubHttpHandler.Json("[]"));
        Configure(pipeline, OtherRepository);
        await pipeline.RunAllAsync(["sync"]);

        github.PageSize = 2;
        foreach (var (author, number) in Accounts.Authors.Select((author, index) => (author, index + 1)))
        {
            github.AddPullRequest(number, author, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 27 - number, 12));
        }

        var limited = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath + "/3/reviews", _ => limited, StubHttpHandler.RateLimited(3600));
        Configure(pipeline, OtherRepository, FakeGitHub.Repository);

        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await Assert.That((await pipeline.QueryAsync<string>(Recorded)).ToArray()).IsEquivalentTo(new[] { $"failed: [\"{OtherRepository}\"]" });

        limited = false;
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That((await pipeline.QueryAsync<long>(StoredNumbers)).ToArray()).IsEquivalentTo(new[] { 1L, 2L, 3L, 4L, 5L });
        await Assert.That((await pipeline.QueryAsync<string>(Recorded)).ToArray()).IsEquivalentTo(new[] { $"read: [\"{OtherRepository}\",\"{FakeGitHub.Repository}\"]" });
    }
}
