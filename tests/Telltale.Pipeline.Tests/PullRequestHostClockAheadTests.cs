using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A pull request host whose clock runs ahead of this machine's (PR timing, review 2, finding 13). The as-of
/// time of a derive is never earlier than the newest thing a host reported, a pull request host like Jira:
/// a merge or a review made moments ago would otherwise be dated after the run that collected it, and be
/// missing from its report. The synthetic hosts are <see cref="FakeGitHub"/> and <see cref="FakeAdo"/>. The
/// run is on Wednesday 21 January 2026 at noon, in sprint 42, which began Monday the 19th at 10:30 and is
/// still running.
/// </summary>
public class PullRequestHostClockAheadTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 AND sprint_id IS NOT NULL ORDER BY sprint_id, measure_position";

    private static PipelineHarness InTheRunningSprint()
    {
        var pipeline = SuppressionOff(Local(2026, 1, 21, 12));
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddSprint42(stillRunning: true);
        return pipeline;
    }

    [Test]
    public async Task A_merge_dated_a_minute_after_this_machines_clock_is_in_the_report_of_the_run_that_collected_it()
    {
        // This machine is two minutes behind GitHub. The pull request was opened a day ago and merged, with
        // no review, a minute before the run by GitHub's clock: a minute after it by this machine's.
        await using var pipeline = InTheRunningSprint();
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12, 1)).Merged(Local(2026, 1, 21, 12, 1));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // The merge is counted, a day after the opening, and the pull request is not counted as still waiting.
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "merge", 1L, 1d) });
        await Assert.That((await PullRequestsWithoutReviewTests.CountsAsync(pipeline, "sprint_id = '42'")).Select(row => (row.Waiting, row.Closed)).ToArray()).IsEquivalentTo(new[] { (0L, 1L) });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT as_of FROM run")).Single()).IsEqualTo("2026-01-21T18:01:00.001Z");
    }

    [Test]
    public async Task A_pull_request_opened_and_voted_on_after_this_machines_clock_on_Azure_DevOps_is_counted_by_the_run_that_collected_it()
    {
        // Opened a minute after the run by this machine's clock, and voted on a minute after that.
        await using var pipeline = InTheRunningSprint();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        FakeAdo.For(pipeline).AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 21, 12, 1)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 21, 12, 2));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // One minute is 1/1440 of a day.
        var waits = (await pipeline.QueryAsync<(string Sprint, string Measure, long Count, double Days)>(Figures)).ToArray();
        await Assert.That(waits.Select(wait => (wait.Sprint, wait.Measure, wait.Count)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L), ("42", "first_code_approval", 1L) });
        await Assert.That(waits.Select(wait => Math.Round(wait.Days * 1440, 6)).ToArray()).IsEquivalentTo(new[] { 1d, 1d });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT as_of FROM run")).Single()).IsEqualTo("2026-01-21T18:02:00.001Z");
    }
}
