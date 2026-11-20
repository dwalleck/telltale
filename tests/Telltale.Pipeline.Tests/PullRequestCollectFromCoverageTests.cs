using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What the figures say of a sprint that began before a host's start date of collection (PR timing, review
/// finding 4). A pull request last updated before <c>collectFrom</c> (on Azure DevOps: closed before it) is
/// not collected, so what such a sprint shows of the host is only part of what the host has: its rows are
/// marked partial in both pull request views and in the report, and where the store holds nothing they say
/// "not known", never zero. A sprint that began on or after the date is not affected. The synthetic hosts are
/// <see cref="FakeGitHub"/> and <see cref="FakeAdo"/>; sprint 41 runs from Monday 5 January 2026 to Monday the
/// 19th and sprint 42 from then to Monday 2 February. The start date in every test is Thursday 15 January.
/// </summary>
public class PullRequestCollectFromCoverageTests
{
    private const string TimingRows =
        "SELECT sprint_id, measure, host_scope, coverage, sample_count, median_elapsed_days FROM pr_timing_per_sprint "
        + "WHERE sprint_id IS NOT NULL ORDER BY sprint_id, measure_position, host_scope_position;";

    private const string WithoutReviewRows =
        "SELECT sprint_id, host_scope, coverage, waiting, closed_without_review, no_activity, history_not_read FROM pr_without_review_per_sprint ORDER BY sprint_id, host_scope_position;";

    private const string GitHubCollectedInPart =
        "- GitHub's pull requests are collected from a start date (collectFrom), and this sprint began before it: a pull request last updated before that date is not in the store. "
        + "GitHub's rows hold only what was collected, so a count may be short of what GitHub has, and where nothing was collected a row says \"not known\": that is not a count of zero.";

    [Test]
    public async Task A_sprint_that_began_before_githubs_start_date_is_marked_partial_and_shows_no_zero_and_a_later_sprint_is_as_ever()
    {
        // Pull request 1 was merged on Wednesday 7 January and never touched again: before the start date, so
        // it is not collected. Pull request 2 was reviewed on Thursday the 8th and merged on Tuesday the
        // 20th, after the date: it is collected, with its review in sprint 41 and its merge in sprint 42.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        pipeline.Config["github"]!["collectFrom"] = "2026-01-15";
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Merged(Local(2026, 1, 7, 12));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 8, 12)).Merged(Local(2026, 1, 20, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"]);

        // Sprint 41 holds what was collected and says it is partial: the one review is counted, and the merge
        // row has no count, because a merge that was not collected is not a count of zero. Sprint 42 began
        // after the date and is read in full: its zeros are zeros.
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, TimingRows)).IsEqualTo(
            """
            | sprint_id | measure | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- | --- |
            | 41 | first_code_review | github | partial | 1 | 2 |
            | 41 | first_code_approval | github | partial | NULL | NULL |
            | 41 | merge | github | partial | NULL | NULL |
            | 42 | first_code_review | github | read | 0 | NULL |
            | 42 | first_code_approval | github | read | 0 | NULL |
            | 42 | merge | github | read | 1 | 14 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WithoutReviewRows)).IsEqualTo(
            """
            | sprint_id | host_scope | coverage | waiting | closed_without_review | no_activity | history_not_read |
            | --- | --- | --- | --- | --- | --- | --- |
            | 41 | github | partial | NULL | NULL | NULL | NULL |
            | 42 | github | read | 0 | 0 | 0 | 0 |

            """.ReplaceLineEndings("\n"));

        // The waits in no sprint are of everything collected, which a start date makes a part of what the host has.
        await Assert.That((await pipeline.QueryAsync<string>("SELECT DISTINCT coverage FROM pr_timing_per_sprint WHERE sprint_id IS NULL")).ToArray()).IsEquivalentTo(new[] { "partial" });

        var sprint41 = await pipeline.ReadReportAsync(41);
        await Assert.That(sprint41).Contains(
            "| First code review | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| First code approval | not known | not known | not known | not known | not known |\n"
            + "| Merge | not known | not known | not known | not known | not known |\n"
            + "\n" + GitHubCollectedInPart + "\n"
            + "- These are");
        await Assert.That(sprint41).Contains(PullRequestsWithoutReviewTests.TableHead + "| GitHub (partial) | not known | not known | not known | not known | not known | not known | not known |\n\n- \"not known\" stands where");

        var sprint42 = await pipeline.ReadReportAsync(42);
        await Assert.That(sprint42).Contains("| First code approval | 0 | none | none | none | none |\n| Merge | 1 | 14.0 | 14.0 | 10.0 | 10.0 |\n\n- These are");
        await Assert.That(sprint42).DoesNotContain("collectFrom");
        await Assert.That(sprint42).DoesNotContain("not known");
    }

    [Test]
    public async Task Moving_the_start_date_back_covers_the_earlier_sprint_only_once_a_backfill_has_read_the_whole_list()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.PageSize = 1;
        pipeline.Config["github"]!["collectFrom"] = "2026-01-15";
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Merged(Local(2026, 1, 7, 12));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        const string mergeOfSprint41 = "SELECT coverage || ', ' || COALESCE(sample_count, 'no count') FROM pr_timing_per_sprint WHERE sprint_id = '41' AND measure = 'merge'";
        await Assert.That((await pipeline.QueryAsync<string>(mergeOfSprint41)).ToArray()).IsEquivalentTo(new[] { "partial, no count" });

        // The date is taken out. A sync stops at the first page of the list on which nothing is new, so it
        // does not come to the older pull request: the store is still what the date made it, and says so.
        pipeline.Config["github"]!.AsObject().Remove("collectFrom");
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        await Assert.That((await pipeline.QueryAsync<string>(mergeOfSprint41)).ToArray()).IsEquivalentTo(new[] { "partial, no count" });

        // A backfill reads the whole list: now the sprint is covered, and its merge is counted.
        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);
        await Assert.That((await pipeline.QueryAsync<string>(mergeOfSprint41)).ToArray()).IsEquivalentTo(new[] { "read, 1" });
        await Assert.That(await pipeline.ReadReportAsync(41)).DoesNotContain("collectFrom");

        // A later date leaves out what is updated before it from then on, and the next sync records that.
        pipeline.Config["github"]!["collectFrom"] = "2026-01-15";
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        await Assert.That((await pipeline.QueryAsync<string>(mergeOfSprint41)).ToArray()).IsEquivalentTo(new[] { "partial, 1" });
    }

    [Test]
    public async Task With_two_hosts_the_host_with_a_start_date_and_the_combined_rows_are_partial_and_the_other_host_is_not()
    {
        // Azure DevOps has the start date: its pull request 1, completed on Wednesday 7 January, is not
        // collected. GitHub has none, and one merge in sprint 41 after one day.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        var ado = FakeAdo.For(pipeline);
        pipeline.Config["ado"]!["collectFrom"] = "2026-01-15";
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Merged(Local(2026, 1, 7, 12));
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 6, 12)).Completed(Local(2026, 1, 7, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["report", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, TimingRows.Replace("sprint_id IS NOT NULL", "measure = 'merge' AND sprint_id IS NOT NULL", StringComparison.Ordinal))).IsEqualTo(
            """
            | sprint_id | measure | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- | --- |
            | 41 | merge | github | read | 1 | 1 |
            | 41 | merge | ado | partial | NULL | NULL |
            | 41 | merge | combined | partial | 1 | 1 |
            | 42 | merge | github | read | 0 | NULL |
            | 42 | merge | ado | read | 0 | NULL |
            | 42 | merge | combined | complete | 0 | NULL |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WithoutReviewRows.Replace(" ORDER BY", " WHERE sprint_id = '41' ORDER BY", StringComparison.Ordinal))).IsEqualTo(
            """
            | sprint_id | host_scope | coverage | waiting | closed_without_review | no_activity | history_not_read |
            | --- | --- | --- | --- | --- | --- | --- |
            | 41 | github | read | 0 | 1 | 1 | 0 |
            | 41 | ado | partial | NULL | NULL | NULL | NULL |
            | 41 | combined | partial | NULL | 1 | 1 | NULL |

            """.ReplaceLineEndings("\n"));

        var sprint41 = await pipeline.ReadReportAsync(41);
        await Assert.That(sprint41).Contains(
            "| Merge | GitHub | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + "| Merge | Azure DevOps (partial) | not known | not known | not known | not known | not known |\n"
            + "| Merge | Combined (partial) | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + "\n- Azure DevOps's pull requests are collected from a start date (collectFrom), and this sprint began before it: a pull request closed before that date is not in the store. "
            + "Azure DevOps's rows hold only what was collected, so a count may be short of what Azure DevOps has, and where nothing was collected a row says \"not known\": that is not a count of zero."
            + " The combined rows are marked partial: they leave out whatever Azure DevOps has from before that date.\n"
            + "- Each host has its own rows");
        await Assert.That(await pipeline.ReadReportAsync(42)).DoesNotContain("partial");
    }

    [Test]
    public async Task A_host_whose_read_failed_says_that_and_not_that_it_was_collected_in_part()
    {
        // The stronger reason wins: a failed read is said as a failed read, with its start date or without.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        pipeline.Config["github"]!["collectFrom"] = "2026-01-15";
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/repos/" + FakeGitHub.Repository + "/pulls", _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", System.Net.HttpStatusCode.Forbidden));

        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        await Assert.That((await pipeline.QueryAsync<string>("SELECT DISTINCT coverage FROM pr_timing_per_sprint")).ToArray()).IsEquivalentTo(new[] { "failed read" });
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("- GitHub could not be read when pull requests were last collected.");
        await Assert.That(report).DoesNotContain("collectFrom");
    }
}
