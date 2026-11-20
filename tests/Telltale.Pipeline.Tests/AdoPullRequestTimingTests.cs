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
}
