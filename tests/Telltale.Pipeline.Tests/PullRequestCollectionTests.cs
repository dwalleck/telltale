using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Collecting pull requests from GitHub (PR timing, ticket 03): only when the <c>github</c> setting is there,
/// every page of every list, and the same rows however often it runs. The synthetic host is
/// <see cref="FakeGitHub"/>; sprint 42 runs from Monday 19 January to Monday 2 February 2026.
/// </summary>
public class PullRequestCollectionTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 ORDER BY sprint_id, measure_position";

    [Test]
    public async Task Without_the_github_setting_nothing_is_asked_of_a_pull_request_host_and_no_token_is_needed()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourAssignees(pipeline.Jira);
        pipeline.Secrets.Remove(SecretName.GitHubToken);

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        await Assert.That(pipeline.Jira.Handler.Requests.Where(request => !request.Path.StartsWith("/rest/", StringComparison.Ordinal))).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(41)).DoesNotContain("Pull request");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pr_timing_per_sprint")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task With_the_github_setting_and_no_token_the_command_stops_before_anything_is_asked()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Secrets.Remove(SecretName.GitHubToken);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        // Missing access is a failure that names the secret, never a host without pull requests.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The secret 'GitHub token' is not set in the secret store. Run 'telltale secrets set github-token'.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task Every_page_of_every_list_is_read_and_no_page_is_asked_for_after_the_last()
    {
        // Two rows a page. Five pull requests are three pages. Each has three reviews, the human one on the
        // second page: after one by a bot and an approval by the Ops member, who is on the roster's second page.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.PageSize = 2;
        github.OpsMembers.InsertRange(0, [new FakeAccount(70000022, "fake-ops-two"), new FakeAccount(70000023, "fake-ops-three")]);
        foreach (var (author, number) in Accounts.Authors.Select((author, index) => (author, index + 1)))
        {
            github.AddPullRequest(number, author, Local(2026, 1, 20, 12))
                .Reviewed(Accounts.Bot, "COMMENTED", Local(2026, 1, 20, 13))
                .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 20, 14))
                .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        }

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 5L, 2d) });
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "3" });

        // Roster: two pages. Pull requests: three. Reviews: two for each of the five.
        await Assert.That(github.Requests.Count).IsEqualTo(2 + 3 + 10);
    }

    [Test]
    public async Task Every_request_to_the_host_is_a_read_with_the_token_from_the_secret_store_under_the_configured_address()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 21, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(github.Requests.Count).IsEqualTo(3);
        await Assert.That(github.Requests.All(request => request.Method == HttpMethod.Get)).IsTrue();
        await Assert.That(github.Requests.Select(request => request.Headers.Authorization?.ToString() ?? "none").Distinct()).IsEquivalentTo(new[] { $"Bearer {PipelineHarness.GitHubToken}" });

        // Closed and merged pull requests are asked for too, not only the open ones.
        await Assert.That(github.Requests.Single(request => request.Path.EndsWith("/pulls", StringComparison.Ordinal)).Query["state"]).IsEqualTo("all");
    }

    [Test]
    public async Task Running_sync_and_derive_again_adds_no_observation_and_changes_no_wait()
    {
        await using var pipeline = SuppressionOff(Local(2026, 2, 2, 12));
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 16, 12))
            .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 16, 13))
            .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 20, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var figures = await pipeline.QueryAsync<(string, string, long, double)>(Figures);
        var store = await pipeline.ReadStoreContentAsync(withoutAsOf: true);
        var report = await pipeline.ReadReportBytesAsync(42, withoutAsOf: true);
        await Assert.That(figures.ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 4d) });

        await pipeline.RunAllAsync(["sync"], ["derive"], ["sync"], ["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(figures.ToArray());
        await Assert.That(await pipeline.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(store);
        await Assert.That(await pipeline.ReadReportBytesAsync(42, withoutAsOf: true)).IsEqualTo(report);
    }

    [Test]
    public async Task A_review_keeps_the_class_it_was_first_stored_with_when_the_ops_roster_changes_later()
    {
        // The store keeps nothing of who reviewed, so a review is judged once, by the roster at the sync that
        // first stored it. The reviewer joining the Ops team afterwards does not take the review back.
        await using var pipeline = SuppressionOff(Local(2026, 2, 2, 12));
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        var pullRequest = github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 16, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 20, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // A later review moves the pull request's last update, so the next sync reads all its reviews again,
        // the first one too, now by a member of the Ops team.
        github.OpsMembers.Add(Accounts.Reviewer);
        pullRequest.Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 28, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 1 });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 4d) });
    }

    [Test]
    public async Task Reviews_by_the_author_a_bot_and_the_ops_team_and_a_pending_review_end_no_wait_and_a_close_without_a_merge_is_no_merge()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.AuthorOne, "COMMENTED", Local(2026, 1, 20, 13))
            .Reviewed(Accounts.Bot, "APPROVED", Local(2026, 1, 20, 14))
            .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 20, 15))
            .ReviewPending(Accounts.Reviewer)
            .Closed(Local(2026, 1, 23, 12));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Reviewer, "CHANGES_REQUESTED", Local(2026, 1, 21, 12))
            .Merged(Local(2026, 1, 26, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Pull request 1 has no code review and no merge: it is in neither row, not there with a wait of zero.
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 1d), ("42", "merge", 1L, 6d) });
    }

    [Test]
    public async Task A_later_sync_asks_for_the_reviews_of_a_pull_request_only_when_its_list_entry_changed()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 21, 12)).Merged(Local(2026, 1, 22, 12));
        var changing = github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12));
        github.AddPullRequest(3, Accounts.AuthorThree, Local(2026, 1, 20, 12));
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3 });

        // Pull request 2 is reviewed and merged between the syncs. Pull request 1 stays merged and pull
        // request 3 stays open with nothing new: neither costs a request, at this sync or the one after.
        changing.Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 27, 12)).Merged(Local(2026, 1, 28, 12));
        await pipeline.RunAllAsync(["sync"], ["sync"], ["derive"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 2 });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 2L, 4d), ("42", "merge", 2L, 5d) });
    }

    [Test]
    public async Task A_second_sync_with_nothing_changed_reads_one_page_of_the_list_and_no_reviews_and_a_backfill_reads_the_whole_list()
    {
        // Two rows a page, five pull requests: the first sync reads three pages and the reviews of all five.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.PageSize = 2;
        foreach (var (author, number) in Accounts.Authors.Select((author, index) => (author, index + 1)))
        {
            github.AddPullRequest(number, author, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 20 + number, 12));
        }

        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "3" });
        await Assert.That(github.ReviewsAskedFor.Count).IsEqualTo(5);

        // Nothing changed: the newest page is as stored, so the list is read no further and no review is asked for.
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "3", "1" });
        await Assert.That(github.ReviewsAskedFor.Count).IsEqualTo(5);

        // A backfill reads the list to its end, and still asks for no reviews of what is unchanged.
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2", "3", "1", "1", "2", "3" });
        await Assert.That(github.ReviewsAskedFor.Count).IsEqualTo(5);
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 5L, 3d) });
    }

    [Test]
    public async Task A_pull_request_last_updated_before_the_start_date_is_never_asked_for_and_one_opened_before_it_and_reviewed_after_it_is_read()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        pipeline.Config["github"]!["collectFrom"] = "2026-01-15";
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 7, 12));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 20, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"], ["sync"]);

        // The bound is on last activity, not on opening: both were opened before the date.
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 2 });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT pr_number FROM pull_request")).IsEquivalentTo(new[] { 2L });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>("SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0")).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 14d) });
    }

    [Test]
    public async Task A_host_that_refuses_stops_the_command_and_is_recorded_as_failed_not_as_read_and_empty()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "APPROVED", Local(2026, 1, 21, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state || ', roster ' || roster FROM pull_request_host WHERE host = 'github'")).ToArray()).IsEquivalentTo(new[] { "read, roster read" });

        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/repos/fake-org/fake-sample/pulls", _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal naming fake-author-one"}""", System.Net.HttpStatusCode.Forbidden));
        pipeline.Error.GetStringBuilder().Clear();
        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "GitHub answered 403 (Forbidden) to GET repos/fake-org/fake-sample/pulls?state=all&sort=updated&direction=desc&per_page=100.\n");

        // What earlier reads stored stays, and the store says the last read failed.
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state || ', roster ' || roster FROM pull_request_host WHERE host = 'github'")).ToArray()).IsEquivalentTo(new[] { "failed, roster not read" });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 1L });
    }

    [Test]
    public async Task Nothing_that_identifies_a_person_or_quotes_a_pull_request_is_written_anywhere()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Bot, "COMMENTED", Local(2026, 1, 20, 13))
            .Reviewed(Accounts.OpsMember, "APPROVED", Local(2026, 1, 20, 14))
            .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12))
            .Merged(Local(2026, 1, 23, 12));
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 21, 12)).Reviewed(Accounts.AuthorOne, "APPROVED", Local(2026, 1, 22, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await HarnessSteps.QueryEveryMetricViewAsync(pipeline);

        // The scan reads the files the data is in: the store holds both pull requests, and the report its section.
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 2L });
        await Assert.That(files[Path.Combine("reports", "sprint-42.md")]).Contains("## Pull request timing");

        // The authors are there only as salted hashes: two people, neither under an account ID.
        var authors = await pipeline.QueryAsync<string>("SELECT DISTINCT author_hash FROM pull_request");
        await Assert.That(authors.Count).IsEqualTo(2);
        await Assert.That(authors.All(hash => hash.Length == 64 && hash.All(char.IsAsciiHexDigitLower))).IsTrue();

        string[] forbidden = [.. github.ForbiddenText, PipelineHarness.GitHubToken];
        await Assert.That(forbidden).Contains(Accounts.Reviewer.Login);
        await Assert.That(forbidden).Contains(github.PullRequests[0].Title);
        await Assert.That(forbidden).Contains(github.PullRequests[0].Branch);
        await Assert.That(forbidden).Contains(github.PullRequests[0].Reviews[0].Body);
        var written = files.Select(file => (Where: file.Key, Text: file.Value))
            .Append((Where: "standard output", Text: pipeline.Out.ToString()))
            .Append((Where: "standard error", Text: pipeline.Error.ToString()));
        var leaks = (from place in written
                     from text in forbidden
                     where place.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                     select $"'{text}' in {place.Where}").ToList();
        await Assert.That(leaks).IsEmpty();

        // And no metric view gives a hash out.
        var views = pipeline.Out.ToString();
        await Assert.That(authors.Where(hash => views.Contains(hash, StringComparison.OrdinalIgnoreCase))).IsEmpty();
    }
}
