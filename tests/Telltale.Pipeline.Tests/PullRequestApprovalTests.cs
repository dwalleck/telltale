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
}
