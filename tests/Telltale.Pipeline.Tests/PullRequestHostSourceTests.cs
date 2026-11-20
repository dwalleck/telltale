using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The store records where each pull request host's rows were read from (review 2, finding 15): the address of
/// GitHub's API, and on Azure DevOps the organization's address. A pull request is one by host name, repository
/// and number, so rows read from another server or organization must not be continued: every command refuses
/// until a backfill has removed the host's rows and read it whole. Sprint 41 runs from Monday 5 January 2026
/// to Monday the 19th and sprint 42 from then to Monday 2 February; the run is on Tuesday 3 February.
/// </summary>
public class PullRequestHostSourceTests
{
    private const string OtherGitHubApiUrl = "https://github-other.example.invalid/api/v3/";
    private const string OtherAdoApiUrl = "https://ado-other.example.invalid/";
    private const string OtherAdoOrganization = "fake-other-ado-org";

    private const string AdoSourceChanged =
        "ado.organization or ado.apiUrl in the configuration file changed since the store's Azure DevOps pull requests were last read, "
        + "so the store holds the pull requests of another organization or server. Run backfill.\n";

    private const string GitHubSourceChanged =
        "github.apiUrl in the configuration file changed since the store's GitHub pull requests were last read, "
        + "so the store holds the pull requests of another server. Run backfill.\n";

    private const string WaitingAndClosed = "SELECT sprint_id, host_scope, waiting, closed_without_review FROM pr_without_review_per_sprint ORDER BY sprint_id;";

    private static PipelineHarness TwoClosedSprints()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        return pipeline;
    }

    [Test]
    [Arguments("organization", OtherAdoOrganization)]
    [Arguments("apiUrl", OtherAdoApiUrl)]
    public async Task Every_command_but_backfill_refuses_when_the_Azure_DevOps_organization_or_address_changed_and_changes_nothing(string setting, string changedTo)
    {
        await using var pipeline = TwoClosedSprints();
        var ado = FakeAdo.For(pipeline);
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var before = await pipeline.ReadStoreContentAsync();
        var requestsBefore = ado.Requests.Count;

        pipeline.Config["ado"]![setting] = changedTo;
        var exits = new List<int>();
        foreach (var command in new string[][] { ["sync"], ["derive"], ["report", "--sprint", "42"], ["run", "--sprint", "42"], ["query", WaitingAndClosed] })
        {
            exits.Add(await pipeline.RunAsync(command));
        }

        // Each names the settings and the remedy, and neither the old nor the new organization or address.
        await Assert.That(exits).IsEquivalentTo(new[] { 1, 1, 1, 1, 1 });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(string.Concat(Enumerable.Repeat(AdoSourceChanged, 5)));
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(before);
        await Assert.That(ado.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Address)).DoesNotContain("https://ado-other.example.invalid");
    }

    [Test]
    public async Task Sync_and_run_refuse_when_the_GitHub_address_changed_and_ask_nothing_of_the_new_one()
    {
        await using var pipeline = TwoClosedSprints();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var before = await pipeline.ReadStoreContentAsync();

        pipeline.Config["github"]!["apiUrl"] = OtherGitHubApiUrl;
        var exits = new[] { await pipeline.RunAsync("sync"), await pipeline.RunAsync("run", "--sprint", "42"), await pipeline.RunAsync("derive") };

        await Assert.That(exits).IsEquivalentTo(new[] { 1, 1, 1 });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(string.Concat(Enumerable.Repeat(GitHubSourceChanged, 3)));
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(before);
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Address)).DoesNotContain("https://github-other.example.invalid");
    }

    /// <summary>What the other GitHub server has: pull requests 1 and 2, both open and never reviewed or closed.</summary>
    private static void AddTheOtherServersPullRequests(FakeGitHub github)
    {
        github.AddPullRequest(1, Accounts.AuthorTwo, Local(2026, 1, 5, 10));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 6, 10));
    }

    [Test]
    public async Task A_backfill_under_a_changed_GitHub_address_leaves_the_store_as_one_that_only_ever_read_the_new_address()
    {
        // The first server: pull request 1 was reviewed in sprint 41, and pull request 2 was closed in it,
        // seen closed by a sync, and reopened, so the store holds a review and a past closure of them.
        await using var changed = TwoClosedSprints();
        var github = FakeGitHub.For(changed);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 8, 12));
        var reopened = github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 7, 12)).Closed(Local(2026, 1, 9, 12));
        await changed.RunAllAsync(["sync"]);
        reopened.Reopened(Local(2026, 2, 3, 9));
        await changed.RunAllAsync(["sync"], ["derive"]);
        await Assert.That(await HarnessSteps.QueryAsync(changed, WaitingAndClosed)).IsEqualTo(
            "| sprint_id | host_scope | waiting | closed_without_review |\n| --- | --- | --- | --- |\n| 41 | github | 0 | 1 |\n| 42 | github | 0 | 0 |\n");

        // The setting is pointed at another server, which has pull requests of the same numbers.
        changed.Config["github"]!["apiUrl"] = OtherGitHubApiUrl;
        github.PullRequests.Clear();
        AddTheOtherServersPullRequests(github);
        changed.Out.GetStringBuilder().Clear();
        await changed.RunAllAsync(["backfill"]);
        await Assert.That(changed.Out.ToString().ReplaceLineEndings("\n")).EndsWith(
            "GitHub: github.apiUrl changed since its pull requests were last read. The 2 pull requests stored before the change were removed, with everything stored of them, and GitHub was read whole.\n");
        await changed.RunAllAsync(["derive"]);

        // Neither the first server's review nor its closure is left beside the second server's pull requests.
        await Assert.That(await HarnessSteps.QueryAsync(changed, WaitingAndClosed)).IsEqualTo(
            "| sprint_id | host_scope | waiting | closed_without_review |\n| --- | --- | --- | --- |\n| 41 | github | 2 | 0 |\n| 42 | github | 2 | 0 |\n");
        await Assert.That(await changed.QueryAsync<long>("SELECT COALESCE(SUM(sample_count), 0) FROM pr_timing_per_sprint;")).IsEquivalentTo(new[] { 0L });

        await using var fresh = TwoClosedSprints();
        var other = FakeGitHub.For(fresh);
        fresh.Config["github"]!["apiUrl"] = OtherGitHubApiUrl;
        AddTheOtherServersPullRequests(other);
        await fresh.RunAllAsync(["backfill"], ["derive"]);
        await Assert.That(await changed.ReadStoreContentAsync()).IsEqualTo(await fresh.ReadStoreContentAsync());

        // The new address is the recorded one from here on: a sync works, and the old address is now the changed one.
        await changed.RunAllAsync(["sync"], ["run", "--sprint", "42"]);
        changed.Config["github"]!["apiUrl"] = FakeGitHub.ApiUrl;
        await Assert.That(await changed.RunAsync("sync")).IsEqualTo(1);
        await Assert.That(changed.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(GitHubSourceChanged);
    }

    [Test]
    public async Task A_backfill_under_a_changed_Azure_DevOps_address_removes_what_was_read_from_the_old_one()
    {
        await using var pipeline = TwoClosedSprints();
        var ado = FakeAdo.For(pipeline);
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 6, 12)).FileComment(AdoIdentities.Reviewer, Local(2026, 1, 8, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorOne, Local(2026, 1, 7, 12)).Abandoned(Local(2026, 1, 9, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("| First code review | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n");

        // The other server has one pull request, of another number, still open.
        pipeline.Config["ado"]!["apiUrl"] = OtherAdoApiUrl;
        ado.PullRequests.Clear();
        ado.AddPullRequest(7, AdoIdentities.AuthorTwo, Local(2026, 1, 6, 12));
        pipeline.Out.GetStringBuilder().Clear();
        await pipeline.RunAllAsync(["backfill"]);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).EndsWith(
            "Azure DevOps: ado.organization or ado.apiUrl changed since its pull requests were last read. The 2 pull requests stored before the change were removed, with everything stored of them, and Azure DevOps was read whole.\n");
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("| First code review | 0 | none | none | none | none |\n");
        await Assert.That(report).Contains(PullRequestsWithoutReviewTests.TableHead + "| Azure DevOps | 1 | 0 | 1 | 0 | no Ops group | 0 | 0 |\n");
    }

    [Test]
    public async Task A_backfill_under_the_same_address_removes_no_pull_request_the_host_no_longer_lists()
    {
        await using var pipeline = TwoClosedSprints();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 7, 12));
        await pipeline.RunAllAsync(["sync"]);

        github.PullRequests.RemoveAt(1);
        pipeline.Out.GetStringBuilder().Clear();
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(pipeline.Out.ToString()).DoesNotContain("removed");
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, WaitingAndClosed)).IsEqualTo(
            "| sprint_id | host_scope | waiting | closed_without_review |\n| --- | --- | --- | --- |\n| 41 | github | 2 | 0 |\n| 42 | github | 2 | 0 |\n");
    }

    [Test]
    public async Task A_store_from_before_the_source_was_recorded_takes_the_configured_one_and_records_it_at_the_next_read()
    {
        await using var pipeline = TwoClosedSprints();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // As an earlier version of the tool left the host's row: without a source.
        await pipeline.ExecuteAsync("UPDATE pull_request_host SET source = NULL;");

        // No command refuses, and the read goes on from the rows the store holds: the review is one more event of pull request 1.
        pullRequest.Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        await pipeline.RunAllAsync(["report", "--sprint", "42"], ["run", "--sprint", "42"]);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| First code review | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n");

        // That read recorded the address, so a change of it is now seen.
        pipeline.Config["github"]!["apiUrl"] = OtherGitHubApiUrl;
        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(GitHubSourceChanged);
    }
}
