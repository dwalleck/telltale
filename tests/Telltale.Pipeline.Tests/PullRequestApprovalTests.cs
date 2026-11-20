using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The first code approval of a GitHub pull request, told apart from its first code review (PR timing,
/// ticket 04). The synthetic host is <see cref="FakeGitHub"/>. Sprint 41 runs from Monday 5 January 2026 to
/// Monday the 19th, sprint 42 from then to Monday 2 February, and sprint 43, still running, from then on.
/// </summary>
public class PullRequestApprovalTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 ORDER BY sprint_id, measure_position";

    /// <summary>A harness on Tuesday 10 February 2026 with the checks off, sprints 41 to 43 and a synthetic GitHub. No Jira issue is in it.</summary>
    private static (PipelineHarness Pipeline, FakeGitHub GitHub) ThreeSprints()
    {
        var pipeline = SuppressionOff(Local(2026, 2, 10, 12));
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 10), Local(2026, 2, 16, 9));
        return (pipeline, FakeGitHub.For(pipeline));
    }

    private static async Task<(string, string, long, double)[]> FiguresAsync(PipelineHarness pipeline) =>
        [.. await pipeline.QueryAsync<(string, string, long, double)>(Figures)];

    [Test]
    public async Task An_approval_is_counted_in_the_sprint_it_was_given_in_with_the_whole_wait_from_opening_without_a_merge_or_a_jira_issue()
    {
        // Opened in sprint 41. A comment-only review and a request for changes in sprint 42 are reviews and
        // not approvals. The approval comes in sprint 43, 18 days after the opening. Never merged.
        var (pipeline, github) = ThreeSprints();
        await using var _ = pipeline;
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 16, 12))
            .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Reviewer, "CHANGES_REQUESTED", Local(2026, 1, 22, 12))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 2, 3, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "43"], ["report", "--sprint", "42"]);

        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 4d), ("43", "first_code_approval", 1L, 18d) });
        await Assert.That(await pipeline.ReadReportAsync(43)).Contains("| First code review | 0 | none |\n| First code approval | 1 | 18.0 |\n| Merge | 0 | none |\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| First code review | 1 | 4.0 |\n| First code approval | 0 | none |\n| Merge | 0 | none |\n");
    }

    [Test]
    public async Task The_report_says_what_a_code_approval_is_and_that_the_ops_group_is_the_one_read_at_collection()
    {
        var (pipeline, github) = ThreeSprints();
        await using var _ = pipeline;
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 22, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "- A first code approval is the first approving review by someone other than the author, made before the pull request closed. When it is also the first review, it ends both waits. "
            + "A comment-only review and a request for changes are code reviews and not approvals. An approval by the Ops group is kept apart and is never a code approval, and neither is one by the author or by automation. "
            + "A review that had been dismissed before it was first collected does not say what it was: it is a code review and no approval, and since it may have been one, a pull request with such a review before its first known approval has no first code approval here. "
            + "An approval that was collected and dismissed afterwards stays an approval. "
            + "As for code reviews, the Ops group is the one read when a review is first collected: its members today, not those at the time of an older review.\n");
    }

    [Test]
    public async Task An_approval_that_is_the_first_review_ends_both_waits_at_once()
    {
        var (pipeline, github) = ThreeSprints();
        await using var _ = pipeline;
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 22, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 2d), ("42", "first_code_approval", 1L, 2d) });
    }

    [Test]
    public async Task Approvals_by_the_author_a_bot_and_the_ops_team_and_one_given_after_the_close_are_no_code_approval()
    {
        var (pipeline, github) = ThreeSprints();
        await using var _ = pipeline;

        // Approved by the Ops team and by a bot only, then merged: a merge, and no code approval.
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Bot, "APPROVED", Local(2026, 1, 20, 13))
            .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 21, 12))
            .Merged(Local(2026, 1, 22, 12));

        // The Ops team and the author come first: the code approval is the reviewer's, six days after the opening.
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 21, 12))
            .Reviewed(Accounts.AuthorTwo, "APPROVED", Local(2026, 1, 22, 12))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 26, 12));

        // Closed without a merge, and approved only afterwards: reviewed before the close, never approved.
        github.AddPullRequest(3, Accounts.AuthorThree, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 12))
            .Closed(Local(2026, 1, 23, 12))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 27, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // First code review: 6 days (pull request 2) and 1 day (pull request 3), median 3.5.
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[]
        {
            ("42", "first_code_review", 2L, 3.5d), ("42", "first_code_approval", 1L, 6d), ("42", "merge", 1L, 2d),
        });
    }

    [Test]
    public async Task An_approval_dismissed_after_it_was_collected_stays_the_review_and_the_approval_it_was()
    {
        var (pipeline, github) = ThreeSprints();
        await using var _ = pipeline;
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 22, 12));
        await pipeline.RunAllAsync(["sync"]);

        // The approval is dismissed a week later: the next sync reads the review again, now as dismissed.
        pullRequest.LastReviewDismissed(Local(2026, 1, 29, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1 });
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 2d), ("42", "first_code_approval", 1L, 2d) });
    }

    [Test]
    public async Task A_review_already_dismissed_when_first_collected_is_a_review_and_leaves_the_pull_request_without_a_first_code_approval()
    {
        var (pipeline, github) = ThreeSprints();
        await using var _ = pipeline;

        // Dismissed before the first sync: what the review had said is not known. It may have been an approval,
        // so the approval five days later cannot be called the first, and the pull request has none to count.
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 21, 12))
            .LastReviewDismissed(Local(2026, 1, 22, 12))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 26, 12))
            .Merged(Local(2026, 1, 27, 12));

        // A dismissed review after the first known approval changes nothing: the first approval is known.
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 23, 12))
            .Reviewed(Accounts.Reviewer, "CHANGES_REQUESTED", Local(2026, 1, 24, 12))
            .LastReviewDismissed(Local(2026, 1, 25, 12));

        // Reviews dismissed before the first sync that came from the Ops team or a bot were never a person's
        // code approval, whatever they said: the reviewer's approval after them is the first.
        github.AddPullRequest(3, Accounts.AuthorThree, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 21, 12))
            .LastReviewDismissed(Local(2026, 1, 21, 13))
            .Reviewed(Accounts.Bot, "APPROVED", Local(2026, 1, 21, 14))
            .LastReviewDismissed(Local(2026, 1, 21, 15))
            .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 25, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // First code review: 1, 3 and 5 days. First code approval: 3 days (pull request 2) and 5 (pull request 3).
        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[]
        {
            ("42", "first_code_review", 3L, 3d), ("42", "first_code_approval", 2L, 4d), ("42", "merge", 1L, 7d),
        });
    }

    [Test]
    public async Task A_draft_marked_ready_and_a_pull_request_reopened_are_still_timed_from_the_first_opening()
    {
        var (pipeline, github) = ThreeSprints();
        await using var _ = pipeline;

        // Opened as a draft in sprint 41, marked ready and then closed in sprint 42, each seen by a sync. (The syncs all run on 10 February: what differs is what the host says.)
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 16, 12)).AsDraft();
        await pipeline.RunAllAsync(["sync"]);
        pullRequest.MarkedReady(Local(2026, 1, 20, 12)).Closed(Local(2026, 1, 21, 12));
        await pipeline.RunAllAsync(["sync"]);

        // Reopened in sprint 43 and approved there: 19 days after the first opening, not one day after the reopening.
        pullRequest.Reopened(Local(2026, 2, 3, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 2, 4, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "43"]);

        await Assert.That(await FiguresAsync(pipeline)).IsEquivalentTo(new[] { ("43", "first_code_review", 1L, 19d), ("43", "first_code_approval", 1L, 19d) });
    }
}
