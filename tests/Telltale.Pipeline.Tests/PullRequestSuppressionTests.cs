using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The minimum group size on pull request figures (PR timing, ticket 03; ADR-0008): both checks count
/// distinct pull request authors, and the sprint report and the owner's <c>query</c> read the same withheld
/// figures. The store has no Jira issue at all, so no Jira assignee can be what passes a check.
/// </summary>
public class PullRequestSuppressionTests
{
    private const string EveryRow = "SELECT sprint_id, measure, sample_count, median_elapsed_days, suppressed FROM pr_timing_per_sprint ORDER BY sprint_id, measure_position;";

    /// <summary>
    /// Sprint 41: two authors, one pull request each, reviewed after one day. Sprint 42: five authors, one
    /// pull request each, opened Tuesday 20 January at noon and reviewed after 1, 2, 3, 6 and 7 days (median
    /// 3); the first two merged after 8 days. So by the default numbers (five and three) sprint 41 is short of
    /// a team, and in sprint 42 the review figure has five authors behind it and the merge figure two.
    /// </summary>
    private static void AddPullRequests(PipelineHarness pipeline)
    {
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 7, 12));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 7, 12));
        int[] reviewedOnDay = [21, 22, 23, 26, 27];
        foreach (var (author, index) in Accounts.Authors.Select((author, index) => (author, index)))
        {
            var pullRequest = github.AddPullRequest(10 + index, author, Local(2026, 1, 20, 12))
                .Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, reviewedOnDay[index], 12));
            if (index < 2)
            {
                pullRequest.Merged(Local(2026, 1, 28, 12));
            }
        }
    }

    [Test]
    public async Task By_the_default_numbers_the_report_and_the_query_withhold_the_same_pull_request_figures()
    {
        await using var pipeline = new PipelineHarness();
        AddPullRequests(pipeline);

        await pipeline.RunAllAsync(["run", "--sprint", "42"], ["report", "--sprint", "41"]);

        // A figure with too few authors behind it keeps its count; a sprint short of a team shows no row.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "\n## Pull request timing\n\n| Wait from opening to | Pull requests | Median (elapsed days) |\n| --- | ---: | ---: |\n| First code review | 5 | 3.0 |\n| Merge | 2 | suppressed |\n\n- These are");
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n## Pull request timing\n\nsuppressed\n\n- These are");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "- For these figures the minimum group size counts distinct pull request authors, not Jira assignees. When the pull requests with a wait counted in the sprint have fewer than five distinct authors, the table reads \"suppressed\", with no rows. A row with fewer than three distinct authors behind it keeps its count and reads \"suppressed\" for its figure.\n");

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, EveryRow)).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | first_code_review | NULL | NULL | 1 |
            | 41 | merge | NULL | NULL | 1 |
            | 42 | first_code_review | 5 | 3 | 0 |
            | 42 | merge | 2 | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task With_both_numbers_zero_every_pull_request_figure_is_shown_and_a_measure_without_a_wait_has_none()
    {
        await using var pipeline = SuppressionOff();
        AddPullRequests(pipeline);

        await pipeline.RunAllAsync(["run", "--sprint", "42"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| First code review | 5 | 3.0 |\n| Merge | 2 | 8.0 |\n\n- These are");

        // No pull request merged in sprint 41: a count of zero and no figure, which is not a withheld one.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("| First code review | 2 | 1.0 |\n| Merge | 0 | none |\n\n- These are");
        await Assert.That(await pipeline.ReadReportAsync(41)).DoesNotContain("For these figures the minimum group size");
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, EveryRow)).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | first_code_review | 2 | 1 | 0 |
            | 41 | merge | 0 | NULL | 0 |
            | 42 | first_code_review | 5 | 3 | 0 |
            | 42 | merge | 2 | 8 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task Each_number_is_applied_to_its_own_check_of_the_pull_request_figures()
    {
        // A team of two is enough for sprint 41, and a figure needs three authors: the review figure of
        // sprint 41 has two behind it, so it keeps its count and its figure is withheld.
        await using var pipeline = new PipelineHarness();
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 2, assigneesBehindAFigure: 3);
        AddPullRequests(pipeline);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("| First code review | 2 | suppressed |\n| Merge | 0 | none |\n\n- These are");
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, EveryRow.Replace("ORDER BY", "WHERE sprint_id = '41' ORDER BY", StringComparison.Ordinal))).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | suppressed |
            | --- | --- | --- | --- | --- |
            | 41 | first_code_review | 2 | NULL | 1 |
            | 41 | merge | 0 | NULL | 0 |

            """.ReplaceLineEndings("\n"));
    }
}
