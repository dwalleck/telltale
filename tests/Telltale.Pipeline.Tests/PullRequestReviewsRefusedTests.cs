using System.Net;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// One GitHub pull request whose own reviews cannot be read (PR timing, review 2, finding 1): GitHub refuses
/// them, or answers in a shape the tool does not know. The pull request is stored as one whose history was not
/// read and the read goes on, as on Azure DevOps; only several such in a row stop the read. The synthetic host
/// is <see cref="FakeGitHub"/>; sprint 42 runs from Monday 19 January to Monday 2 February 2026.
/// </summary>
public class PullRequestReviewsRefusedTests
{
    private const string Histories = "SELECT pr_number || ': ' || history FROM pull_request ORDER BY pr_number";
    private const string HostState = "SELECT state FROM pull_request_host WHERE host = 'github'";
    private const string PullsPath = "/api/v3/repos/" + FakeGitHub.Repository + "/pulls";

    private static Func<StubRequest, HttpResponseMessage> Answer(string how) => how switch
    {
        "a refusal" => StubHttpHandler.Json("""{"message":"Synthetic refusal naming fake-author-one"}""", HttpStatusCode.NotFound),
        "an unknown shape" => StubHttpHandler.Json("""{"reviews":"Synthetic text of another shape naming fake-author-one"}"""),
        _ => throw new ArgumentOutOfRangeException(nameof(how)),
    };

    private static string MessageOf(string how, int number) => how == "a refusal"
        ? $"GitHub answered 404 (NotFound) to GET repos/fake-org/fake-sample/pulls/{number}/reviews?per_page=100."
        : "GitHub's pull request reviews answered in a shape this tool does not understand (InvalidOperationException).";

    [Test]
    [Arguments("a refusal")]
    [Arguments("an unknown shape")]
    public async Task A_pull_request_whose_reviews_cannot_be_read_is_stored_without_them_and_the_others_are_read(string how)
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        foreach (var (author, number) in Accounts.Authors.Select((author, index) => (author, index + 1)))
        {
            github.AddPullRequest(number, author, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 12));
        }

        var unreadable = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath + "/3/reviews", _ => unreadable, Answer(how));

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        // The read goes on past pull request 3 and ends: the host is read, and the console says what was not.
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"GitHub: the reviews of a pull request could not be read ({MessageOf(how, 3)}). "
            + "It is stored without them and counted as a pull request whose history was not read; its reviews are asked for again at the next sync.\n");
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: failed", "4: read", "5: read" });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "read" });

        // It has no review wait and is counted as a history that was not read; the host's coverage is still a full read.
        await Assert.That((await pipeline.QueryAsync<(string, long)>("SELECT coverage, sample_count FROM pr_timing_per_sprint WHERE sprint_id = '42' AND measure = 'first_code_review'")).ToArray())
            .IsEquivalentTo(new[] { ("read", 4L) });
        await Assert.That((await pipeline.QueryAsync<(string, long, long)>("SELECT coverage, waiting, history_not_read FROM pr_without_review_per_sprint WHERE sprint_id = '42'")).ToArray())
            .IsEquivalentTo(new[] { ("read", 0L, 1L) });

        // The next sync asks for its reviews again, and for no other pull request's.
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 4, 5, 3 });

        // Once they can be read they are, and the pull request has its wait.
        unreadable = false;
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: read", "4: read", "5: read" });
        await Assert.That((await pipeline.QueryAsync<long>("SELECT sample_count FROM pr_timing_per_sprint WHERE sprint_id = '42' AND measure = 'first_code_review'")).ToArray()).IsEquivalentTo(new[] { 5L });
    }

    [Test]
    public async Task Three_such_pull_requests_in_a_row_stop_the_read_and_those_already_stored_as_unread_do_not_count_at_the_next()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        foreach (var number in Enumerable.Range(1, 6))
        {
            github.AddPullRequest(number, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 12));
        }

        foreach (var number in new[] { 3, 4, 5 })
        {
            pipeline.Jira.Handler.Override(HttpMethod.Get, $"{PullsPath}/{number}/reviews", _ => true, Answer("a refusal"));
        }

        // The third refusal in a row is the host failing, not a pull request: the read stops, and what was
        // read before it is kept, the two stored without their reviews among it.
        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith(
            "GitHub answered 404 (NotFound) to GET repos/fake-org/fake-sample/pulls/5/reviews?per_page=100. "
            + "It was the third pull request in a row whose reviews could not be read, so the read was stopped. "
            + "The reviews of 2 of 6 pull requests were read before that and are stored, and the next sync reads the rest. Until a read of GitHub finishes, a report says that GitHub's figures rest on a read that failed.\n");
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: failed", "4: failed" });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "failed" });

        // At the next sync pull requests 3 and 4 fail again, which says nothing new about the host: only 5
        // counts, and the read comes to its end.
        pipeline.Error.GetStringBuilder().Clear();
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 4, 5, 3, 4, 5, 6 });
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: failed", "4: failed", "5: failed", "6: read" });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "read" });
    }
}
