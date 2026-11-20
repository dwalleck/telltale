using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The pull requests that give no first code review to a sprint's figures, counted apart from the waits (PR
/// timing, ticket 07): still waiting when the sprint closed, or closed in it without one, and what had been
/// submitted by then. The counts are of GitHub's pull requests here (<see cref="FakeGitHub"/>); sprint 41 runs
/// from Monday 5 January to Monday 19 January 2026, sprint 42 from then to Monday 2 February.
/// </summary>
public class PullRequestsWithoutReviewTests
{
    internal const string Counts =
        "SELECT sprint_id, host_scope, waiting, closed_without_review, no_activity, automation_only, ops_only, unclassifiable, history_not_read FROM pr_without_review_per_sprint";

    internal const string TableHead =
        "\n### Without a first code review\n\n"
        + "Pull requests of the configured repositories that had no first code review when the sprint closed, or at the as-of time for a sprint that is still running. "
        + "These are numbers of pull requests, not waits: none of them is in the first code review row above, and none is counted as a wait of zero.\n\n"
        + "| Pull requests of | Still waiting | Closed without one | No activity | Automation only | Ops only | Cannot be classified | History not read |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n";

    internal static async Task<(string Sprint, string Scope, long Waiting, long Closed, long NoActivity, long AutomationOnly, long? OpsOnly, long Unclassifiable, long NotRead)[]> CountsAsync(PipelineHarness pipeline, string where) =>
        [.. await pipeline.QueryAsync<(string, string, long, long, long, long, long?, long, long)>(
            "SELECT sprint_id, host_scope, COALESCE(waiting, -1), COALESCE(closed_without_review, -1), COALESCE(no_activity, -1), COALESCE(automation_only, -1), ops_only, COALESCE(unclassifiable, -1), COALESCE(history_not_read, -1) "
            + $"FROM pr_without_review_per_sprint WHERE {where} ORDER BY sprint_id, host_scope_position")];

    /// <summary>
    /// Sprint 41 with four Jira assignees, which the default numbers withhold whole, and three pull requests by
    /// one author: one reviewed only after the sprint closed, one closed in the sprint with no review, and one
    /// reviewed in the sprint.
    /// </summary>
    private static void AddAFourPersonSprintAndThreePullRequests(PipelineHarness pipeline)
    {
        AddSprint41WithFourAssignees(pipeline.Jira);
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 20, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 7, 12)).Closed(Local(2026, 1, 9, 12));
        github.AddPullRequest(3, Accounts.AuthorOne, Local(2026, 1, 8, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 9, 12));
    }

    [Test]
    public async Task A_review_after_the_sprint_closed_leaves_the_pull_request_waiting_at_its_close_and_the_report_shows_the_counts_by_the_default_numbers()
    {
        await using var pipeline = new PipelineHarness();
        AddAFourPersonSprintAndThreePullRequests(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        // The Jira figures of the same run are withheld; the pull request counts are not (owner decision D114).
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).Contains(TableHead + "| GitHub | 1 | 1 | 2 | 0 | 0 | 0 | 0 |\n\n- Still waiting");

        // Only the pull request reviewed in the sprint has a wait: neither of the other two is a wait of zero.
        await Assert.That(report).Contains("| First code review | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n");
    }

    [Test]
    public async Task By_the_default_numbers_the_owner_query_gives_the_same_counts_and_still_withholds_the_jira_figures()
    {
        await using var pipeline = new PipelineHarness();
        AddAFourPersonSprintAndThreePullRequests(pipeline);
        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, "SELECT * FROM pr_without_review_per_sprint;", "SELECT sprint_id, delivered_count, suppressed FROM delivered_per_sprint;")).IsEqualTo(
            """
            | sprint_id | host_scope | host_scope_position | coverage | waiting | closed_without_review | no_activity | automation_only | ops_only | unclassifiable | history_not_read |
            | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
            | 41 | github | 1 | read | 1 | 1 | 2 | 0 | 0 | 0 | 0 |
            | sprint_id | delivered_count | suppressed |
            | --- | --- | --- |
            | 41 | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task What_was_submitted_splits_the_waiting_and_the_closed_into_ops_only_automation_only_and_no_activity_and_adds_no_pull_request()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);

        // Open at the close of sprint 42: a bot's review only; an Ops approval and a bot's review; the
        // author's own review only; and one opened in sprint 41 on which nothing was ever submitted.
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Bot, "COMMENTED", Local(2026, 1, 21, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 21, 12)).Reviewed(Accounts.Bot, "COMMENTED", Local(2026, 1, 22, 12));
        github.AddPullRequest(3, Accounts.AuthorTwo, Local(2026, 1, 20, 12)).Reviewed(Accounts.AuthorTwo, "COMMENTED", Local(2026, 1, 21, 12));
        github.AddPullRequest(4, Accounts.AuthorTwo, Local(2026, 1, 6, 12));

        // Closed in sprint 42: merged on an Ops approval alone; and merged with nothing, a person's review
        // coming only after the merge, which is no review of it.
        github.AddPullRequest(5, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 21, 12)).Merged(Local(2026, 1, 22, 12));
        github.AddPullRequest(6, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 22, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 23, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // The pull request opened in sprint 41 was waiting at its close and is still waiting at sprint 42's.
        await Assert.That(await CountsAsync(pipeline, "host_scope = 'github'")).IsEquivalentTo(new[]
        {
            ("41", "github", 1L, 0L, 1L, 0L, (long?)0L, 0L, 0L),
            ("42", "github", 4L, 2L, 3L, 1L, (long?)2L, 0L, 0L),
        });

        // No pull request has a first code review, none is a wait of zero, and both merges are counted.
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("| First code review | 0 | none | none | none | none |\n| First code approval | 0 | none | none | none | none |\n| Merge | 2 | 2.0 | 2.0 | 2.0 | 2.0 |\n");
        await Assert.That(report).Contains(TableHead + "| GitHub | 4 | 2 | 3 | 1 | 2 | 0 | 0 |\n\n- Still waiting");
        await Assert.That(report).Contains(
            "- No activity, Automation only and Ops only split the pull requests of the first two columns by what had been submitted on each before it closed. "
            + "Each of those pull requests is in exactly one of the three, so the three add up to the first two columns and are not further pull requests. "
            + "Ops only: something by the Ops group, whatever automation did besides. Automation only: something by automation and nothing by the Ops group. "
            + "No activity: nothing, or only the author's own; the pull request's history was read and holds nothing else.\n");
    }

    [Test]
    public async Task A_review_that_cannot_be_classified_gives_no_wait_and_is_counted_apart_from_the_waiting()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);

        // A review that names no reviewer, before a person's: it could have been the first code review. And a
        // person's review in a state this tool does not know.
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(null, "COMMENTED", Local(2026, 1, 21, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "SYNTHETIC_NEW_STATE", Local(2026, 1, 21, 12)).Merged(Local(2026, 1, 23, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await CountsAsync(pipeline, "sprint_id = '42' AND host_scope = 'github'")).IsEquivalentTo(new[] { ("42", "github", 0L, 0L, 0L, 0L, (long?)0L, 2L, 0L) });
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("| First code review | 0 | none | none | none | none |\n");
        await Assert.That(report).Contains("| Merge | 1 | 3.0 | 3.0 | 3.0 | 3.0 |\n");
        await Assert.That(report).Contains("| GitHub | 0 | 0 | 0 | 0 | 0 | 2 | 0 |\n");
    }

    [Test]
    public async Task While_the_ops_roster_cannot_be_read_the_pull_requests_are_counted_as_not_read_and_not_as_having_no_activity()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 21, 12)).Merged(Local(2026, 1, 22, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        var refused = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/orgs/fake-org/teams/fake-ops/members", _ => refused, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", System.Net.HttpStatusCode.Forbidden));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Neither history was read: the one that will turn out empty is not said to be empty yet. The merge is known.
        await Assert.That(await CountsAsync(pipeline, "sprint_id = '42' AND host_scope = 'github'")).IsEquivalentTo(new[] { ("42", "github", -1L, -1L, -1L, -1L, (long?)null, -1L, 2L) });
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("| Merge | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n");
        await Assert.That(report).Contains(
            "| GitHub | not known | not known | not known | not known | not known | not known | 2 |\n\n"
            + "- \"not known\" stands where a row does not rest on a full read of its reviews (the lines under the table above say which and why) and the store holds no such pull request: that is not a count of zero.\n- Still waiting");

        // Read: one was reviewed and is in no column, and the other's history is empty, which is waiting with no activity.
        refused = false;
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await CountsAsync(pipeline, "sprint_id = '42' AND host_scope = 'github'")).IsEquivalentTo(new[] { ("42", "github", 1L, 0L, 1L, 0L, (long?)0L, 0L, 0L) });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| GitHub | 1 | 0 | 1 | 0 | 0 | 0 | 0 |\n");
    }

    [Test]
    public async Task A_sprint_that_is_still_running_is_counted_as_of_the_run_and_a_closed_one_stays_as_it_was_at_its_close()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddSprint42(stillRunning: true);
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await CountsAsync(pipeline, "host_scope = 'github'")).IsEquivalentTo(new[]
        {
            ("41", "github", 1L, 0L, 1L, 0L, (long?)0L, 0L, 0L),
            ("42", "github", 1L, 0L, 1L, 0L, (long?)0L, 0L, 0L),
        });

        // Reviewed on Wednesday 4 February, in the running sprint: it no longer waits there, and it was
        // still waiting when sprint 41 closed.
        pullRequest.Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 2, 4, 9));
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await CountsAsync(pipeline, "host_scope = 'github'")).IsEquivalentTo(new[]
        {
            ("41", "github", 1L, 0L, 1L, 0L, (long?)0L, 0L, 0L),
            ("42", "github", 0L, 0L, 0L, 0L, (long?)0L, 0L, 0L),
        });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| First code review | 1 | 28.9 |");
    }
}
