using System.Net;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The author of a stored pull request whose reviews are read without its list entry, after a new salt (PR
/// timing, review 2, finding 7). A backfill under a new salt clears every stored author hash, and the list
/// hashes again only the pull requests it gives. A stored pull request whose reviews were never read and
/// that the list does not give is read all the same, and there the stored hash was all that said who the
/// author is: without it the author's own comment would be stored as a code review, for good. So the pull
/// request itself is asked for then (<c>pulls/{number}</c>), which names its author. The list can leave one
/// out when it changes while the list is read and moves to a page already given. The synthetic host is
/// <see cref="FakeGitHub"/>; sprint 42 runs from Monday 19 January to Monday 2 February 2026.
/// </summary>
public class PullRequestAuthorAfterNewSaltTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 AND sprint_id IS NOT NULL ORDER BY sprint_id, measure_position";
    private const string Stored = "SELECT pr_number || ': ' || history || ', author ' || CASE WHEN author_hash IS NULL THEN 'not known' ELSE 'hashed' END FROM pull_request ORDER BY pr_number";
    private const string Events = "SELECT pr_number || ': ' || outcome || ', ' || actor FROM pull_request_event ORDER BY pr_number, at";
    private const string MembersPath = "/api/v3/orgs/fake-org/teams/fake-ops/members";

    /// <summary>
    /// Pull request 9, on which its author answered with a comment-only review and nobody else did anything,
    /// stored without its reviews because the Ops team could not be read; then a new salt, a readable team,
    /// and a list that does not give the pull request.
    /// </summary>
    private static async Task<(PipelineHarness Pipeline, FakeGitHub GitHub)> StoredWithoutReviewsThenANewSaltAsync()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(9, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.AuthorOne, "COMMENTED", Local(2026, 1, 21, 12));
        var refused = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, MembersPath, _ => refused, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "9: not read, author hashed" });

        refused = false;
        pipeline.Error.GetStringBuilder().Clear();
        pipeline.Secrets[SecretName.HashingSalt] = "synthetic-salt-two";
        github.NotGivenByTheList.Add(9);
        return (pipeline, github);
    }

    [Test]
    public async Task The_authors_own_comment_is_not_a_first_code_review_when_the_reviews_are_read_after_a_new_salt_cleared_the_stored_author()
    {
        var (pipeline, github) = await StoredWithoutReviewsThenANewSaltAsync();
        await using var _ = pipeline;
        var hashedBefore = (await pipeline.QueryAsync<string>("SELECT author_hash FROM pull_request")).Single();

        await pipeline.RunAllAsync(["backfill", "--allow-empty"], ["derive"]);

        // The author's review is the author's and ends no wait, and the pull request was asked for by itself, once.
        await Assert.That((await pipeline.QueryAsync<string>(Events)).ToArray()).IsEquivalentTo(new[] { "9: commented, author" });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEmpty();
        await Assert.That(github.AskedForSingly).IsEquivalentTo(new[] { 9 });

        // The row is stored as read, with its author hashed under the salt of now.
        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "9: read, author hashed" });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT author_hash FROM pull_request")).Single()).IsNotEqualTo(hashedBefore);

        // The same hash a read of the list gives: nothing differs at the next sync, which asks for nothing of the pull request.
        github.NotGivenByTheList.Clear();
        var hashed = (await pipeline.QueryAsync<string>("SELECT author_hash FROM pull_request")).Single();
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT author_hash FROM pull_request")).Single()).IsEqualTo(hashed);
        await Assert.That(github.AskedForSingly).IsEquivalentTo(new[] { 9 });
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 9 });
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden, """{"message":"Synthetic refusal"}""", "GitHub answered 403 (Forbidden) to GET repos/fake-org/fake-sample/pulls/9.")]
    [Arguments(HttpStatusCode.NotFound, """{"message":"Synthetic refusal"}""", "GitHub answered 404 (NotFound) to GET repos/fake-org/fake-sample/pulls/9.")]
    [Arguments(HttpStatusCode.OK, """{"number":9,"user":{"login":"fake-author-one"}}""", "GitHub's pull request answered in a shape this tool does not understand (KeyNotFoundException).")]
    public async Task A_pull_request_that_cannot_be_asked_for_by_itself_is_left_as_it_is_and_the_read_goes_on(HttpStatusCode status, string body, string said)
    {
        var (pipeline, github) = await StoredWithoutReviewsThenANewSaltAsync();
        await using var _ = pipeline;
        github.AddPullRequest(10, Accounts.AuthorTwo, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeGitHub.PullRequestPath(9), _ => true, StubHttpHandler.Json(body, status));

        var exit = await pipeline.RunAsync("backfill", "--allow-empty");

        // Its reviews are not asked for: they could not be told from the author's. It stays not read, and the other is read.
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"GitHub: the author of a stored pull request could not be read ({said}). "
            + "Its reviews were not read, since the author's own could not be told from a code review; it stays a pull request whose reviews were not read, and is asked for again at the next sync.\n");
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 10 });
        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "9: not read, author not known", "10: read, author hashed" });
        await Assert.That((await pipeline.QueryAsync<string>(Events)).ToArray()).IsEquivalentTo(new[] { "10: commented, human" });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'github'")).ToArray()).IsEquivalentTo(new[] { "read" });
    }

    [Test]
    public async Task A_client_that_gives_up_on_the_pull_request_asked_for_by_itself_stops_the_read()
    {
        var (pipeline, github) = await StoredWithoutReviewsThenANewSaltAsync();
        await using var _ = pipeline;
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeGitHub.PullRequestPath(9), _ => true, StubHttpHandler.RateLimited(3600));

        var exit = await pipeline.RunAsync("backfill", "--allow-empty");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("GitHub is limiting how often it can be asked: it answered 429 (too many requests) to GET repos/fake-org/fake-sample/pulls/9 and asked for a wait of more than 5 minutes");
        await Assert.That(github.ReviewsAskedFor).IsEmpty();
        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "9: not read, author not known" });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'github'")).ToArray()).IsEquivalentTo(new[] { "failed" });
    }

    [Test]
    public async Task An_ordinary_sync_asks_for_no_pull_request_by_itself()
    {
        // The first read, a read of a changed pull request, a read with nothing new, and the read of reviews
        // that an earlier sync could not read while the list has moved on past the pull request: none of them
        // asks for a single pull request, since each has the author from the list or from the stored hash.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.PageSize = 1;
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.AuthorOne, "COMMENTED", Local(2026, 1, 21, 12));
        var two = github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 22, 12));
        var refused = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/repos/" + FakeGitHub.Repository + "/pulls/1/reviews", _ => refused, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));

        await pipeline.RunAllAsync(["sync"]);
        two.Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 23, 12));
        await pipeline.RunAllAsync(["sync"], ["sync"]);
        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "1: failed, author hashed", "2: read, author hashed" });

        refused = false;
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That((await pipeline.QueryAsync<string>(Stored)).ToArray()).IsEquivalentTo(new[] { "1: read, author hashed", "2: read, author hashed" });
        await Assert.That((await pipeline.QueryAsync<string>(Events)).ToArray()).IsEquivalentTo(new[] { "1: commented, author", "2: commented, human" });
        await Assert.That(github.AskedForSingly).IsEmpty();
        await Assert.That(github.Requests.Count(request => request.Path.EndsWith("/reviews", StringComparison.Ordinal))).IsEqualTo(6);
    }
}
