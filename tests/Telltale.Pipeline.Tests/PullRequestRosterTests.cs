using System.Net;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The Ops group's members cannot be read (PR timing, ticket 04): the roster is not taken for an empty one, so
/// no review is classified, the figures that rest on reviews are not given, and the merge figure, which does
/// not, still is. The synthetic host is <see cref="FakeGitHub"/>; sprint 42 runs from Monday 19 January to
/// Monday 2 February 2026.
/// </summary>
public class PullRequestRosterTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 ORDER BY sprint_id, measure_position";
    private const string Measures = "SELECT DISTINCT measure FROM pr_timing_per_sprint ORDER BY measure_position";
    private const string HostState = "SELECT state || ', roster ' || roster FROM pull_request_host WHERE host = 'github'";
    private const string MembersPath = "/api/v3/orgs/fake-org/teams/fake-ops/members";

    private const string NotShown =
        "- Not shown: First code review, First code approval. The members of the Ops group could not be read when the pull requests were last collected, so reviews by the group could not be told apart from code reviews, and no review was collected. These waits are given for no sprint until a collection reads the group; nothing here says that no pull request was reviewed or approved. A merge does not depend on who reviewed, and is shown.\n";

    private static async Task<(string, string, long, double)[]> FiguresAsync(PipelineHarness pipeline) =>
        [.. await pipeline.QueryAsync<(string, string, long, double)>(Figures)];

    /// <summary>The roster's address answers 403 until the returned action is called.</summary>
    private static Action RefuseTheRoster(PipelineHarness pipeline)
    {
        var refused = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, MembersPath, _ => refused, StubHttpHandler.Json("""{"message":"Synthetic refusal naming fake-ops-one"}""", HttpStatusCode.Forbidden));
        return () => refused = false;
    }

    [Test]
    public async Task With_a_roster_that_cannot_be_read_the_run_goes_on_and_gives_the_merge_figure_and_neither_review_figure()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 20, 13))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 21, 12))
            .Merged(Local(2026, 1, 22, 12));
        var readable = RefuseTheRoster(pipeline);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        // The command goes on, says what it could not read, and the store records it.
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).Contains(
            "GitHub: the members of the Ops team could not be read (GitHub answered 403 (Forbidden) to GET orgs/fake-org/teams/fake-ops/members?per_page=100.). "
            + "No review was read, and no first code review or first code approval figure is given for GitHub until a sync reads the team.\n");
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "read, roster failed" });

        // No review is asked for: without the roster it could not be classified, and a class is never changed.
        await Assert.That(github.ReviewsAskedFor).IsEmpty();

        // The review measures have no row at all, not a row that counts nothing. The merge is known and counted.
        await Assert.That((await pipeline.QueryAsync<string>(Measures)).ToArray()).IsEquivalentTo(new[] { "merge" });
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "merge", 1L, 2d) });
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("| Wait from opening to | Pull requests | Median (elapsed days) |\n| --- | ---: | ---: |\n| Merge | 1 | 2.0 |\n\n" + NotShown + "- These are");
        await Assert.That(report).DoesNotContain("| First code");

        // Once the roster can be read, the reviews are asked for although the list entry is as it was, and
        // each is classified by the roster read then: the Ops approval is none, the reviewer's is.
        readable();
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1 });
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "read, roster read" });
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 1d), ("42", "first_code_approval", 1L, 1d), ("42", "merge", 1L, 2d) });
        await Assert.That(await pipeline.ReadReportAsync(42)).DoesNotContain("Not shown");
    }

    [Test]
    public async Task A_roster_that_fails_at_a_later_sync_withholds_the_review_figures_already_given_until_it_is_read_again()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 1d) });

        // A second pull request is reviewed and merged, and the roster cannot be read at the next sync: the
        // figure for first code review would count one pull request of two, so it is not given.
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 23, 12)).Merged(Local(2026, 1, 24, 12));
        var readable = RefuseTheRoster(pipeline);
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "merge", 1L, 4d) });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(NotShown);

        // A sync that fails as a whole after that leaves the reviews unread: still no review figure.
        pipeline.Jira.Handler.AnswerNext(HttpMethod.Get, "/api/v3/repos/fake-org/fake-sample/pulls", StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));
        readable();
        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await pipeline.RunAllAsync(["derive"]);
        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "failed, roster not read" });
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "merge", 1L, 4d) });

        // Read again: only the pull request whose reviews were not read is asked for.
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2 });
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 2L, 2d), ("42", "first_code_approval", 1L, 3d), ("42", "merge", 1L, 4d) });
    }

    [Test]
    public async Task Without_an_ops_team_configured_there_is_no_roster_to_read_and_every_figure_is_given()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        pipeline.Config["github"]!.AsObject().Remove("opsTeam");
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 21, 12));
        RefuseTheRoster(pipeline);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<string>(HostState)).ToArray()).IsEquivalentTo(new[] { "read, roster not configured" });
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 1d), ("42", "first_code_approval", 1L, 1d) });
    }
}
