using System.Net;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A review made while a pull request was closed is not a review of it, and must not become its first code
/// review once the pull request is opened again. The list of pull requests does not say when one was
/// reopened, so when a read finds a stored GitHub pull request open again it asks once for that pull request's
/// issue events, which give the closing and the reopening to the second. Sprint 41 runs from Monday 5 January
/// 2026 to Monday the 19th; sprint 42 is running. The first collection is on Tuesday 27 January at noon.
/// </summary>
public class PullRequestReviewedWhileClosedTests
{
    private const string WaitingAndClosed = "SELECT sprint_id, host_scope, waiting, closed_without_review FROM pr_without_review_per_sprint ORDER BY sprint_id;";
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 AND sprint_id IS NOT NULL ORDER BY sprint_id, measure_position";

    private static PipelineHarness InSprint42()
    {
        var pipeline = SuppressionOff(Local(2026, 1, 27, 12));
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddSprint42(stillRunning: true);
        return pipeline;
    }

    private static string Rows(string sprint41, string sprint42) =>
        "| sprint_id | host_scope | waiting | closed_without_review |\n| --- | --- | --- | --- |\n"
        + $"| 41 | github | {sprint41} |\n| 42 | github | {sprint42} |\n";

    private static async Task<(string, string, long, double)[]> FiguresAsync(PipelineHarness pipeline) =>
        [.. await pipeline.QueryAsync<(string, string, long, double)>(Figures)];

    [Test]
    public async Task A_review_made_while_the_pull_request_was_closed_does_not_become_its_first_code_review_when_it_is_reopened()
    {
        // Opened on the 6th, closed on the 9th, and reviewed by a person on the 12th, while closed: all in sprint 41.
        await using var pipeline = InSprint42();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Closed(Local(2026, 1, 9, 12))
            .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 12, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("0 | 1", "0 | 0"));
        await Assert.That(await FiguresAsync(pipeline)).IsEmpty();
        await Assert.That(github.IssueEventsAskedFor).IsEmpty();

        // Reopened on the 28th, in sprint 42, and seen open by the collection of the 29th.
        pullRequest.Reopened(Local(2026, 1, 28, 12));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        // Sprint 41 is as it was: the pull request closed in it without a review, and is in no first code
        // review row, there or anywhere. In the running sprint it is open and waiting.
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("0 | 1", "1 | 0"));
        await Assert.That(await FiguresAsync(pipeline)).IsEmpty();
        await Assert.That(github.IssueEventsAskedFor).IsEquivalentTo(new[] { 1 });

        // A review on the 30th, after the reopening, is its first code review: in sprint 42, 24 days after the first opening.
        pullRequest.Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 30, 12));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["backfill"], ["derive"]);

        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 24d), ("42", "first_code_approval", 1L, 24d) });
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("0 | 1", "0 | 0"));

        // The issue events were asked for once, by the read that saw the reopening, and by none since: not by a backfill either.
        await Assert.That(github.IssueEventsAskedFor).IsEquivalentTo(new[] { 1 });

        // Nothing of who closed or reopened it is kept.
        var files = await pipeline.ReadAllFilesAsync();
        foreach (var forbidden in github.ForbiddenText)
        {
            await Assert.That(files.Values.Any(content => content.Contains(forbidden, StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task A_review_made_after_the_reopening_and_before_the_collection_that_sees_it_counts()
    {
        // The common case: closed on the 9th and seen closed, reopened on the 28th at nine and reviewed at
        // three, both before the collection of the 29th.
        await using var pipeline = InSprint42();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Closed(Local(2026, 1, 9, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        pullRequest.Reopened(Local(2026, 1, 28, 9)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 28, 15));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 22.125d) });
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("0 | 1", "0 | 0"));
    }

    [Test]
    public async Task With_the_exact_reopening_a_sprint_that_closed_after_it_has_the_pull_request_as_open()
    {
        // Closed on the 9th and seen closed by a collection on the 12th. Reopened on the 14th, still in
        // sprint 41, which closes on the 19th; the next collection is on the 27th. By the bound alone the
        // pull request would count as closed until the 27th, and sprint 41 would have it closed without a review.
        await using var pipeline = SuppressionOff(Local(2026, 1, 12, 12));
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddSprint42(stillRunning: true);
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Closed(Local(2026, 1, 9, 12));
        await pipeline.RunAllAsync(["sync"]);

        pullRequest.Reopened(Local(2026, 1, 14, 12));
        pipeline.Clock.Advance(TimeSpan.FromDays(15));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("1 | 0", "1 | 0"));
    }

    [Test]
    public async Task When_the_issue_events_are_refused_the_read_goes_on_with_the_closure_as_the_collections_saw_it()
    {
        await using var pipeline = InSprint42();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Closed(Local(2026, 1, 9, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeGitHub.IssueEventsPath(1), _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal naming fake-author-one"}""", HttpStatusCode.Forbidden));

        pullRequest.Reopened(Local(2026, 1, 28, 9)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 28, 15));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        // The command goes on and says so, quoting nothing. The closure is kept as before, and the review
        // made between the reopening and this collection counts: it is most likely one made after the reopening.
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "GitHub: when a reopened pull request was closed and reopened could not be read (GitHub answered 403 (Forbidden) to GET repos/fake-org/fake-sample/issues/1/events?per_page=100.). "
            + "Its closing is kept as the collections saw it: closed until this one, and a review made in that time is counted.\n");
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(Rows("0 | 1", "0 | 0"));
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 22.125d) });

        // It is not asked for again: the next read no longer finds a closing gone.
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.IssueEventsAskedFor).IsEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task An_ordinary_sync_asks_for_no_issue_events()
    {
        // Opened, reviewed, closed, merged, changed between two reads: nothing here was reopened.
        await using var pipeline = InSprint42();
        var github = FakeGitHub.For(pipeline);
        var open = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12));
        var reviewed = github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 7, 12));
        github.AddPullRequest(3, Accounts.AuthorThree, Local(2026, 1, 6, 12)).Closed(Local(2026, 1, 8, 12));
        github.AddPullRequest(4, Accounts.AuthorFour, Local(2026, 1, 6, 12)).Merged(Local(2026, 1, 8, 12));
        await pipeline.RunAllAsync(["sync"]);

        open.Closed(Local(2026, 1, 27, 15));
        reviewed.Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 27, 16)).Merged(Local(2026, 1, 27, 17));
        pipeline.Clock.Advance(TimeSpan.FromDays(1));
        await pipeline.RunAllAsync(["sync"], ["backfill"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 4, 1, 2 });
        await Assert.That(github.IssueEventsAskedFor).IsEmpty();
    }
}
