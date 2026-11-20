using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The three waits of Azure DevOps pull requests (PR timing, ticket 05), in an installation with no GitHub
/// setting, no GitHub token and no Jira issue: what ends the wait for a first code review and for a first
/// code approval, and that only completion is a merge. The synthetic host is <see cref="FakeAdo"/>. Sprint 41
/// runs from Monday 5 January 2026, sprint 42 from Monday 19 January to Monday 2 February, and sprint 43 from
/// Monday 2 February.
/// </summary>
public class AdoPullRequestTimingTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 ORDER BY sprint_id, measure_position";

    /// <summary>A harness with suppression off, sprints 41 and 42 closed and 43 running, no GitHub token, and a synthetic Azure DevOps.</summary>
    private static (PipelineHarness Pipeline, FakeAdo Ado) AdoOnly(DateTimeOffset? now = null)
    {
        var pipeline = SuppressionOff(now ?? Local(2026, 2, 4, 12));
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 10), Local(2026, 2, 16, 9));
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        return (pipeline, FakeAdo.For(pipeline));
    }

    [Test]
    public async Task A_scanner_comment_ends_no_wait_a_later_file_comment_ends_the_review_wait_and_a_later_positive_vote_ends_the_approval_wait_in_its_own_sprint()
    {
        // Opened Friday 16 January at noon, in sprint 41. The designated scanner comments on a file an hour
        // later. A person comments on a file on Tuesday 20 January at noon, in sprint 42: four days. The
        // same person approves with suggestions on Tuesday 3 February at noon, in sprint 43: eighteen days.
        var (pipeline, ado) = AdoOnly(Local(2026, 2, 2, 12));
        await using var _ = pipeline;
        var pullRequest = ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 16, 12))
            .FileComment(AdoIdentities.Scanner, Local(2026, 1, 16, 13))
            .FileComment(AdoIdentities.Reviewer, Local(2026, 1, 20, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 4d) });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "\n## Pull request timing\n\n| Wait from opening to | Pull requests | Median (elapsed days) |\n| --- | ---: | ---: |\n| First code review | 1 | 4.0 |\n| First code approval | 0 | none |\n| Merge | 0 | none |\n");

        pullRequest.Voted(AdoIdentities.Reviewer, 5, Local(2026, 2, 3, 12));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "43"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 4d), ("43", "first_code_approval", 1L, 18d) });
        await Assert.That(await pipeline.ReadReportAsync(43)).Contains("| First code review | 0 | none |\n| First code approval | 1 | 18.0 |\n| Merge | 0 | none |\n");
    }

    [Test]
    public async Task A_vote_counts_for_the_reviewer_who_cast_it_whoever_wrote_its_thread()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;

        // Cast by a reviewer, in a thread written under the author's name: a review and an approval, after two days.
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 22, 12), threadWrittenBy: AdoIdentities.AuthorOne);

        // Cast by the author and by the scanner, in threads written under a reviewer's name: neither is a review.
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12)).Voted(AdoIdentities.AuthorTwo, 10, Local(2026, 1, 21, 12), threadWrittenBy: AdoIdentities.Reviewer);
        ado.AddPullRequest(3, AdoIdentities.AuthorThree, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Scanner, 10, Local(2026, 1, 21, 12), threadWrittenBy: AdoIdentities.Reviewer);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 2d), ("42", "first_code_approval", 1L, 2d) });
    }

    [Test]
    public async Task The_authors_own_activity_general_conversation_a_vote_reset_and_a_current_vote_without_a_record_end_no_wait()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12))
            .FileComment(AdoIdentities.AuthorOne, Local(2026, 1, 20, 13))
            .Comment(AdoIdentities.Reviewer, Local(2026, 1, 21, 12))
            .Voted(AdoIdentities.Reviewer, 0, Local(2026, 1, 22, 12))
            .FileComment(AdoIdentities.Reviewer, Local(2026, 1, 22, 13), deleted: true)
            .CurrentVote(AdoIdentities.Reviewer, 10);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // The reviewer's current vote says approved, and has no time: it is no approval the tool can date.
        await Assert.That(await pipeline.QueryAsync<(string, string, long, double)>(Figures)).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| First code review | 0 | none |\n| First code approval | 0 | none |\n| Merge | 0 | none |\n");
    }

    [Test]
    public async Task A_vote_against_is_a_code_review_and_no_approval_and_approval_with_suggestions_is_an_approval()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;

        // Waiting for the author after one day, approved with suggestions after three.
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12))
            .Voted(AdoIdentities.Reviewer, -5, Local(2026, 1, 21, 12))
            .Voted(AdoIdentities.Reviewer, 5, Local(2026, 1, 23, 12));

        // Rejected after two days, and nothing since.
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, -10, Local(2026, 1, 22, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 2L, 1.5d), ("42", "first_code_approval", 1L, 3d) });
    }

    [Test]
    public async Task Only_completion_is_a_merge_and_an_abandoned_pull_request_keeps_the_review_it_got_before_it_closed()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;

        // Reviewed after one day, abandoned after three, and approved a day after that: the approval came too late to be one.
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12))
            .FileComment(AdoIdentities.Reviewer, Local(2026, 1, 21, 12))
            .Abandoned(Local(2026, 1, 23, 12))
            .Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 24, 12));

        // Approved after two days, completed after six.
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12))
            .Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 22, 12))
            .Completed(Local(2026, 1, 26, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // The abandoned one is in the review row with its one day, and in no merge row: closing is not merging.
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 2L, 1.5d), ("42", "first_code_approval", 1L, 2d), ("42", "merge", 1L, 6d) });
    }

    [Test]
    public async Task An_earlier_vote_whose_voter_or_value_cannot_be_told_leaves_no_figure_in_place_of_a_later_one()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;

        // The first vote names no voter: it may have been the first review and the first approval.
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12))
            .Voted(null, 10, Local(2026, 1, 21, 12))
            .Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 23, 12));

        // The first vote's value is not a number: it may or may not have been a review, or an approval.
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12))
            .Voted(AdoIdentities.Reviewer, 0, Local(2026, 1, 22, 12), result: "synthetic")
            .Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 23, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await pipeline.QueryAsync<(string, string, long, double)>(Figures)).IsEmpty();
    }

    [Test]
    public async Task With_both_hosts_configured_each_is_read_and_the_report_words_the_rules_of_both()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        pipeline.Secrets[SecretName.GitHubToken] = PipelineHarness.GitHubToken;
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).FileComment(AdoIdentities.Reviewer, Local(2026, 1, 24, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // The same number on two hosts is two pull requests: waits of two and four days.
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 2L, 3d) });
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("\n- A first code review is the first submitted review by someone other than the author");
        await Assert.That(report).Contains("\n- On Azure DevOps, a first code review is the first vote other than a reset, or the first text comment on a file, by someone other than the author");
    }

    [Test]
    public async Task With_azure_devops_alone_the_report_words_its_rules_and_not_those_of_a_host_that_is_not_read()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).FileComment(AdoIdentities.Reviewer, Local(2026, 1, 24, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("\n- On Azure DevOps, a first code review is the first vote other than a reset");
        await Assert.That(report).Contains("No Ops group is set apart on Azure DevOps.");
        await Assert.That(report).DoesNotContain("submitted review");
        await Assert.That(report).Contains("- Pull request figures are not subject to the minimum group size");
    }

    [Test]
    public async Task By_the_default_numbers_a_single_authors_figures_are_shown_in_the_report_and_by_the_owner_query()
    {
        // Owner decision D114: pull request figures are shown whatever the minimum group size, here five and three.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourAssignees(pipeline.Jira);
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        FakeAdo.For(pipeline).AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 6, 12))
            .Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 8, 12))
            .Completed(Local(2026, 1, 9, 12));

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).Contains("| First code review | 1 | 2.0 |\n| First code approval | 1 | 2.0 |\n| Merge | 1 | 3.0 |\n");
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint ORDER BY measure_position;")).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days |
            | --- | --- | --- | --- |
            | 41 | first_code_review | 1 | 2 |
            | 41 | first_code_approval | 1 | 2 |
            | 41 | merge | 1 | 3 |

            """.ReplaceLineEndings("\n"));
    }
}
