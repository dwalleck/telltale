using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The distribution of pull request waits in a sprint: for each measure the number of pull requests, the
/// median and the nearest-rank P85, in elapsed days and in business days, each clock ranked on its own from
/// the unrounded waits. Every expected figure is worked out by hand beside its fixture. The sprint report and
/// the owner's <c>query</c> give the same figures from the same view.
/// </summary>
public class PullRequestTimingDistributionTests
{
    private const string Columns = "sprint_id, measure, sample_count, median_elapsed_days, p85_elapsed_days, median_business_days, p85_business_days";

    private const string TableHead =
        "\n## Pull request timing\n\n"
        + "| Wait from opening to | Pull requests | Median (elapsed days) | P85 (elapsed days) | Median (business days) | P85 (business days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: |\n";

    private static string RowsOf(string sprint) =>
        $"SELECT {Columns} FROM pr_timing_per_sprint WHERE sprint_id IS {sprint} ORDER BY measure_position;";

    [Test]
    public async Task An_even_number_of_waits_across_a_weekend_and_a_holiday_has_each_clock_ranked_on_its_own()
    {
        // Sprint 42 is Monday 19 January 09:00 to Friday the 30th 17:00, and Monday the 19th is a company
        // holiday. Times are the team's (America/Chicago). Four first code reviews in sprint 42:
        //
        //   PR  opened        reviewed       elapsed  business
        //   1   Tue 20 12:00  Wed 21 00:00   0.5      0.5
        //   2   Fri 16 12:00  Wed 21 00:00   4.5      1.5   (Friday 0.5, weekend 0, holiday 0, Tuesday 1)
        //   3   Wed 21 12:00  Sat 24 00:00   2.5      2.5
        //   4   Thu 22 12:00  Mon 26 06:00   3.75     1.75  (Thursday 0.5, Friday 1, weekend 0, Monday 0.25)
        //
        // Elapsed, in order:  0.5, 2.5, 3.75, 4.5 (PR 1, 3, 4, 2): median (2.5 + 3.75) / 2 = 3.125; P85 is
        //                     rank ceil(0.85 * 4) = 4, so 4.5.
        // Business, in order: 0.5, 1.5, 1.75, 2.5 (PR 1, 2, 4, 3): median (1.5 + 1.75) / 2 = 1.625; P85 2.5.
        // Taking the business figures at the elapsed ranks would give 2.125 and 1.5 instead.
        //
        // One merge: opened Friday 23 at 20:00, merged Monday 26 at 08:00. Elapsed 2.5 days. Business: 4 hours
        // of Friday and 8 of Monday, 0.5 days. (In UTC the opening is already Saturday 02:00 and the merge
        // Monday 14:00: 14 hours, which is not what the team's days give.)
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Config["holidays"] = new JsonArray("2026-01-19");
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 0));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 16, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 0));
        github.AddPullRequest(3, Accounts.AuthorOne, Local(2026, 1, 21, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 24, 0));
        github.AddPullRequest(4, Accounts.AuthorTwo, Local(2026, 1, 22, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 26, 6));
        github.AddPullRequest(5, Accounts.AuthorOne, Local(2026, 1, 23, 20)).Merged(Local(2026, 1, 26, 8));

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, RowsOf("'42'"))).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | 42 | first_code_review | 4 | 3.125 | 4.5 | 1.625 | 2.5 |
            | 42 | merge | 1 | 2.5 | 2.5 | 0.5 | 0.5 |

            """.ReplaceLineEndings("\n"));

        // The report rounds for display only: 3.125 and 1.625 are shown to one decimal.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            TableHead
            + "| First code review | 4 | 3.1 | 4.5 | 1.6 | 2.5 |\n"
            + "| Merge | 1 | 2.5 | 2.5 | 0.5 | 0.5 |\n");
    }

    [Test]
    public async Task The_p85_of_seven_waits_is_the_sixth_by_nearest_rank_and_the_median_the_fourth()
    {
        // Sprint 41 is Monday 5 January 09:00 to Friday the 16th 17:00. Seven pull requests opened Monday at
        // noon and merged every half day from Tuesday 00:00 to Friday 00:00: waits of 0.5, 1, 1.5, 2, 2.5, 3
        // and 3.5 days, with no weekend in them, so the same on both clocks.
        // Median: the fourth of seven, 2. P85: rank ceil(0.85 * 7) = ceil(5.95) = 6, so 3: a wait that was
        // observed, not 3.05 (interpolated) and not 3.5 (the longest).
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        var github = FakeGitHub.For(pipeline);
        var opened = Local(2026, 1, 5, 12);
        for (var number = 1; number <= 7; number++)
        {
            github.AddPullRequest(number, Accounts.AuthorOne, opened).Merged(opened + TimeSpan.FromHours(12 * number));
        }

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, RowsOf("'41'"))).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | 41 | first_code_review | 0 | NULL | NULL | NULL | NULL |
            | 41 | merge | 7 | 2 | 3 | 2 | 3 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            TableHead
            + "| First code review | 0 | none | none | none | none |\n"
            + "| Merge | 7 | 2.0 | 3.0 | 2.0 | 3.0 |\n");
    }

    [Test]
    public async Task A_wait_is_counted_whole_in_the_one_window_that_holds_its_end_and_in_no_sprint_when_none_or_two_do()
    {
        // Sprint 41: Monday 5 January 09:00 to Friday the 16th 17:00. Sprint 42: Monday the 19th 09:00 to
        // Friday the 30th 17:00. Sprint 43 overlaps 42: Wednesday the 28th 09:00 to Monday 2 February 17:00.
        // No holidays. Five merges:
        //
        //   PR  opened              merged           where the merge falls            elapsed  business
        //   1   Mon 29 Dec 09:00    Mon 19 09:00     42's first instant: in 42        21       15 (three weeks of five days)
        //   2   Wed 14 17:00        Fri 16 17:00     41's end, which 41 excludes:     2        2
        //                                            the gap before 42
        //   3   Thu 15 16:59        Fri 16 16:59     41's last minute: in 41          1        1
        //   4   Wed 28 12:00        Thu 29 12:00     in both 42 and 43                1        1
        //   5   Fri 30 12:00        Sat 31 12:00     after 42's end: in 43 only       1        0.5 (Friday's half; Saturday 0)
        //
        // PR 1 was opened a week before sprint 41 began and is counted in 42 with all 21 days: not the zero
        // days of it that lie in 42's window. PR 2 and PR 4 are in no sprint: waits of 1 and 2 days on both
        // clocks, so a median of 1.5 and a P85 (rank ceil(1.7) = 2) of 2.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsOfTwoWorkingWeeks(pipeline.Jira);
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 1, 28, 9), Local(2026, 2, 2, 17), Local(2026, 2, 2, 17));
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2025, 12, 29, 9)).Merged(Local(2026, 1, 19, 9));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 14, 17)).Merged(Local(2026, 1, 16, 17));
        github.AddPullRequest(3, Accounts.AuthorOne, Local(2026, 1, 15, 16, 59)).Merged(Local(2026, 1, 16, 16, 59));
        github.AddPullRequest(4, Accounts.AuthorOne, Local(2026, 1, 28, 12)).Merged(Local(2026, 1, 29, 12));
        github.AddPullRequest(5, Accounts.AuthorOne, Local(2026, 1, 30, 12)).Merged(Local(2026, 1, 31, 12));

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "42"]);

        // Each merge is in exactly one row: the counts add up to the five merges.
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, $"SELECT {Columns} FROM pr_timing_per_sprint ORDER BY sprint_id IS NULL, sprint_id, measure_position;")).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | 41 | first_code_review | 0 | NULL | NULL | NULL | NULL |
            | 41 | merge | 1 | 1 | 1 | 1 | 1 |
            | 42 | first_code_review | 0 | NULL | NULL | NULL | NULL |
            | 42 | merge | 1 | 21 | 21 | 15 | 15 |
            | 43 | first_code_review | 0 | NULL | NULL | NULL | NULL |
            | 43 | merge | 1 | 1 | 1 | 0.5 | 0.5 |
            | NULL | first_code_review | 0 | NULL | NULL | NULL | NULL |
            | NULL | merge | 2 | 1.5 | 2 | 1.5 | 2 |

            """.ReplaceLineEndings("\n"));

        // The report of a sprint says how many waits are in no sprint's row, for the measures that have any.
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            TableHead
            + "| First code review | 0 | none | none | none | none |\n"
            + "| Merge | 1 | 21.0 | 21.0 | 15.0 | 15.0 |\n"
            + "\nIn no sprint's row, of everything collected, because the event fell between sprint windows or in more than one: Merge 2.\n"
            + "\n- These are");
    }

    [Test]
    public async Task What_is_dated_at_or_after_the_as_of_time_is_not_counted_until_a_later_run_and_no_wait_is_no_figure()
    {
        // The run is on Wednesday 21 January at noon, in sprint 42, which is still running (it began Monday
        // the 19th at 10:30). One pull request, opened Tuesday the 20th at noon. The host already gives a
        // review dated at the very instant of the run and a merge dated six hours after it.
        await using var pipeline = new PipelineHarness(Local(2026, 1, 21, 12));
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddSprint42(stillRunning: true);
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 12))
            .Merged(Local(2026, 1, 21, 18));

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "42"]);

        // Neither has happened as of this run: no pull request and no figure, which is not a figure of zero.
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, RowsOf("'42'"), RowsOf("NULL"))).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | 42 | first_code_review | 0 | NULL | NULL | NULL | NULL |
            | 42 | merge | 0 | NULL | NULL | NULL | NULL |
            | sprint_id | measure | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | NULL | first_code_review | 0 | NULL | NULL | NULL | NULL |
            | NULL | merge | 0 | NULL | NULL | NULL | NULL |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            TableHead
            + "| First code review | 0 | none | none | none | none |\n"
            + "| Merge | 0 | none | none | none | none |\n"
            + "\n- These are");

        // Twelve hours later, at midnight, both are in the past: the review after 1 day and the merge after
        // 30 hours, 1.25 days, all on working days.
        pipeline.Clock.Advance(TimeSpan.FromHours(12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, RowsOf("'42'"))).IsEqualTo(
            """
            | sprint_id | measure | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | 42 | first_code_review | 1 | 1 | 1 | 1 | 1 |
            | 42 | merge | 1 | 1.25 | 1.25 | 1.25 | 1.25 |

            """.ReplaceLineEndings("\n"));
    }
}
