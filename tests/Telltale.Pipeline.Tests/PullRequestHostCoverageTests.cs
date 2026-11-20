using System.Net;
using System.Text.Json.Nodes;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What the figures say when a configured host is missing from them (PR timing, ticket 08). Four things are
/// told apart, in the view's <c>coverage</c> and in the report: a host that is not configured (it has no row),
/// a configured host that was never read, a host whose last read failed, and a host that was read and has no
/// pull request with the wait (the only one of the four that is a count of zero). A combined row that lacks a
/// host is marked partial, and the hosts that were read keep their own rows. A pull request is one by host,
/// repository and number. The synthetic hosts are <see cref="FakeGitHub"/> and <see cref="FakeAdo"/>; sprint
/// 41 runs from Monday 5 January 2026 and sprint 42 from Monday 19 January to Monday 2 February.
/// </summary>
public class PullRequestHostCoverageTests
{
    private const string MergeRows =
        "SELECT sprint_id, host_scope, coverage, sample_count, median_elapsed_days FROM pr_timing_per_sprint "
        + "WHERE sprint_id IS NOT NULL AND measure = 'merge' ORDER BY sprint_id, host_scope_position;";

    private const string RowsOfSprint42 =
        "SELECT measure, host_scope, coverage, sample_count, median_elapsed_days FROM pr_timing_per_sprint "
        + "WHERE sprint_id = '42' ORDER BY measure_position, host_scope_position;";

    private const string CombinedRowsExplained =
        "- Each host has its own rows, and a combined row holds the waits of every host together: its median and P85 are taken of those waits, never of the hosts' medians or P85s.\n";

    [Test]
    public async Task A_host_whose_read_fails_keeps_what_was_read_before_under_a_label_and_the_other_host_is_still_read_and_shown()
    {
        // First everything is read: a GitHub merge in sprint 41 after one day (Tuesday 6 to Wednesday 7
        // January) and an Azure DevOps merge in sprint 42 after two (Tuesday 20 to Thursday 22 January).
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        var ado = FakeAdo.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Merged(Local(2026, 1, 7, 12));
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Completed(Local(2026, 1, 22, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Then each host has one more merge in sprint 42 (GitHub after three days; Azure DevOps after four,
        // Monday 26 to Friday 30 January), and GitHub refuses the next sync.
        github.AddPullRequest(2, Accounts.AuthorTwo, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 23, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 26, 12)).Completed(Local(2026, 1, 30, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/repos/" + FakeGitHub.Repository + "/pulls", _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));

        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        // GitHub: the merge of sprint 41 was read before and stays, labelled; nothing of sprint 42 was read,
        // which is no count, not a count of zero. Azure DevOps was read in the same sync although GitHub
        // failed first: 2 and 4 days, median 3, P85 (rank 2 of 2) 4. The combined rows hold what the store
        // has and say they are partial.
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, MergeRows)).IsEqualTo(
            """
            | sprint_id | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- |
            | 41 | github | failed read | 1 | 1 |
            | 41 | ado | read | 0 | NULL |
            | 41 | combined | partial | 1 | 1 |
            | 42 | github | failed read | NULL | NULL |
            | 42 | ado | read | 2 | 3 |
            | 42 | combined | partial | 2 | 3 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "| Merge | GitHub (last read failed) | not known | not known | not known | not known | not known |\n"
            + "| Merge | Azure DevOps | 2 | 3.0 | 4.0 | 3.0 | 4.0 |\n"
            + "| Merge | Combined (partial) | 2 | 3.0 | 4.0 | 3.0 | 4.0 |\n"
            + "\n- GitHub could not be read when pull requests were last collected. Its figures are of what earlier collections read, so a count may be short of what GitHub has now, and where they read nothing the row says \"not known\": that is not a count of zero."
            + " The combined rows are marked partial: they leave out whatever a read of GitHub would add.\n"
            + CombinedRowsExplained);
    }

    [Test]
    public async Task When_neither_host_can_be_read_the_command_says_what_each_answered_and_no_row_has_a_count()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));
        FakeAdo.For(pipeline).AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Completed(Local(2026, 1, 22, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/repos/" + FakeGitHub.Repository + "/pulls", _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ListPath, _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));

        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        var said = pipeline.Error.ToString();
        await Assert.That(said).Contains("GitHub answered 403 (Forbidden)");
        await Assert.That(said).Contains("Azure DevOps answered 403 (Forbidden)");
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, MergeRows.Replace("sprint_id IS NOT NULL", "sprint_id = '42'", StringComparison.Ordinal))).IsEqualTo(
            """
            | sprint_id | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- |
            | 42 | github | failed read | NULL | NULL |
            | 42 | ado | failed read | NULL | NULL |
            | 42 | combined | partial | NULL | NULL |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "| Merge | GitHub (last read failed) | not known | not known | not known | not known | not known |\n"
            + "| Merge | Azure DevOps (last read failed) | not known | not known | not known | not known | not known |\n"
            + "| Merge | Combined (partial) | not known | not known | not known | not known | not known |\n");
    }

    [Test]
    public async Task With_one_host_configured_and_its_read_failed_the_report_says_so_under_the_table_and_shows_no_zero()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/repos/" + FakeGitHub.Repository + "/pulls", _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));

        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "| First code review | not known | not known | not known | not known | not known |\n"
            + "| First code approval | not known | not known | not known | not known | not known |\n"
            + "| Merge | not known | not known | not known | not known | not known |\n"
            + "\n- GitHub could not be read when pull requests were last collected. Its figures are of what earlier collections read, so a count may be short of what GitHub has now, and where they read nothing the row says \"not known\": that is not a count of zero.\n"
            + "- These are");
    }

    [Test]
    public async Task A_configured_host_that_was_never_read_because_its_token_is_missing_has_no_count_and_is_not_a_host_without_pull_requests()
    {
        // Azure DevOps alone at first: one merge in sprint 42 after two days. With one host there is no combined row.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        FakeAdo.For(pipeline).AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Completed(Local(2026, 1, 22, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, MergeRows.Replace("sprint_id IS NOT NULL", "sprint_id = '42'", StringComparison.Ordinal))).IsEqualTo(
            """
            | sprint_id | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- |
            | 42 | ado | read | 1 | 2 |

            """.ReplaceLineEndings("\n"));

        // GitHub is then configured, and its token is not in the secret store: the sync stops before it asks
        // anything of any host, so the store is as the last collection left it and holds nothing of GitHub.
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));
        var asked = pipeline.Jira.Handler.Requests.Count;

        await Assert.That(await pipeline.RunAsync("sync")).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("The secret 'GitHub token' is not set in the secret store.");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(asked);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, MergeRows.Replace("sprint_id IS NOT NULL", "sprint_id = '42'", StringComparison.Ordinal))).IsEqualTo(
            """
            | sprint_id | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- |
            | 42 | github | not read | NULL | NULL |
            | 42 | ado | read | 1 | 2 |
            | 42 | combined | partial | 1 | 2 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "| Merge | GitHub (not read yet) | not known | not known | not known | not known | not known |\n"
            + "| Merge | Azure DevOps | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| Merge | Combined (partial) | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "\n- GitHub is configured and has not been read yet: no collection of it has finished. Its rows say \"not known\": that is not a count of zero."
            + " The combined rows are marked partial: they leave out whatever a read of GitHub would add.\n"
            + CombinedRowsExplained);
    }

    [Test]
    public async Task A_host_that_was_read_and_has_no_pull_request_counts_zero_and_the_combined_rows_are_complete()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        FakeGitHub.For(pipeline);
        FakeAdo.For(pipeline).AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Completed(Local(2026, 1, 22, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, MergeRows.Replace("sprint_id IS NOT NULL", "sprint_id = '42'", StringComparison.Ordinal))).IsEqualTo(
            """
            | sprint_id | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- |
            | 42 | github | read | 0 | NULL |
            | 42 | ado | read | 1 | 2 |
            | 42 | combined | complete | 1 | 2 |

            """.ReplaceLineEndings("\n"));
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains(
            "| Merge | GitHub | 0 | none | none | none | none |\n"
            + "| Merge | Azure DevOps | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| Merge | Combined | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "\n" + CombinedRowsExplained);
        await Assert.That(report).DoesNotContain("not known");
        await Assert.That(report).DoesNotContain("partial");
    }

    [Test]
    public async Task With_the_ops_roster_unreadable_on_github_the_review_rows_are_azure_devops_alone_and_say_so()
    {
        // GitHub: opened Tuesday 20 January, a comment-only review the next day and a merge on Thursday the
        // 22nd, two days. Azure DevOps: opened Tuesday the 20th and a file comment on Friday the 23rd, three
        // days; no vote and no completion. The Ops team's members cannot be read, so no GitHub review is.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12))
            .Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 21, 12))
            .Merged(Local(2026, 1, 22, 12));
        FakeAdo.For(pipeline).AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).FileComment(AdoIdentities.Reviewer, Local(2026, 1, 23, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/api/v3/orgs/fake-org/teams/fake-ops/members", _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Forbidden));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // The review waits of GitHub have no count. The combined review row is Azure DevOps's one wait,
        // partial; the combined approval row has nothing in the store and one host unread: no count, not zero.
        // A merge does not rest on reviews: both hosts are read for it.
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, RowsOfSprint42)).IsEqualTo(
            """
            | measure | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- |
            | first_code_review | github | reviews not read | NULL | NULL |
            | first_code_review | ado | read | 1 | 3 |
            | first_code_review | combined | partial | 1 | 3 |
            | first_code_approval | github | reviews not read | NULL | NULL |
            | first_code_approval | ado | read | 0 | NULL |
            | first_code_approval | combined | partial | NULL | NULL |
            | merge | github | read | 1 | 2 |
            | merge | ado | read | 0 | NULL |
            | merge | combined | complete | 1 | 2 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "| First code review | Azure DevOps | 1 | 3.0 | 3.0 | 3.0 | 3.0 |\n"
            + "| First code review | Combined (partial) | 1 | 3.0 | 3.0 | 3.0 | 3.0 |\n"
            + "| First code approval | Azure DevOps | 0 | none | none | none | none |\n"
            + "| First code approval | Combined (partial) | not known | not known | not known | not known | not known |\n"
            + "| Merge | GitHub | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| Merge | Azure DevOps | 0 | none | none | none | none |\n"
            + "| Merge | Combined | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "\n- Not shown for GitHub: First code review, First code approval. The members of the Ops group could not be read at a collection of the pull requests, so reviews by the group could not be told apart from code reviews, and the reviews of the pull requests collected then were not read and have not been read since. These waits are given for no sprint until a collection reads those reviews, which takes the group's members; nothing here says that no pull request was reviewed or approved. A merge does not depend on who reviewed, and is shown."
            + " The combined rows of these waits are marked partial: they hold the other hosts' waits only.\n"
            + CombinedRowsExplained);
    }

    [Test]
    public async Task The_same_repository_name_and_number_on_two_hosts_are_two_pull_requests_and_a_record_met_again_is_counted_once()
    {
        // Both hosts have a repository the store names "fake-org/fake-sample", and each has a pull request 7 in
        // it, opened Tuesday 20 January at noon: GitHub's merged after one day, Azure DevOps's completed after
        // three. Azure DevOps lists its one twice in the same answer.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        FakeGitHub.For(pipeline).AddPullRequest(7, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));
        AddAnAzureDevOpsRepositoryNamedAsGitHubsThatListsPullRequest7Twice(pipeline);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Two pull requests: one in each host's row and both in the combined one, median (1 + 3) / 2 = 2.
        const string twoPullRequests =
            """
            | sprint_id | host_scope | coverage | sample_count | median_elapsed_days |
            | --- | --- | --- | --- | --- |
            | 42 | github | read | 1 | 1 |
            | 42 | ado | read | 1 | 3 |
            | 42 | combined | complete | 2 | 2 |

            """;
        var rowsOfSprint42 = MergeRows.Replace("sprint_id IS NOT NULL", "sprint_id = '42'", StringComparison.Ordinal);
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, rowsOfSprint42)).IsEqualTo(twoPullRequests.ReplaceLineEndings("\n"));

        // Collected again by a sync and whole by a backfill, and derived again: still two.
        await pipeline.RunAllAsync(["sync"], ["backfill"], ["derive"], ["run", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, rowsOfSprint42)).IsEqualTo(twoPullRequests.ReplaceLineEndings("\n"));
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(
            "| Merge | GitHub | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n"
            + "| Merge | Azure DevOps | 1 | 3.0 | 3.0 | 3.0 | 3.0 |\n"
            + "| Merge | Combined | 2 | 2.0 | 3.0 | 2.0 | 3.0 |\n");
    }

    [Test]
    public async Task A_pull_request_that_githubs_list_gives_again_on_a_later_page_is_counted_once()
    {
        // The list is read newest first while pull requests change, so one can move down and be met again on
        // the next page. Here the first page gives pull requests 3 and 2, and the second gives 2 again and 1.
        // All three were opened on Tuesday 20 January at noon and merged one, two and three days later.
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        foreach (var number in new[] { 1, 2, 3 })
        {
            github.AddPullRequest(number, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 20 + number, 12));
        }

        const string list = "/api/v3/repos/" + FakeGitHub.Repository + "/pulls";
        static string Entry(int number) =>
            $$"""{"number":{{number}},"user":{"id":70000001,"login":"fake-author-one","type":"User"},"state":"closed","created_at":"2026-01-20T18:00:00Z","updated_at":"2026-01-{{20 + number}}T18:00:00Z","closed_at":"2026-01-{{20 + number}}T18:00:00Z","merged_at":"2026-01-{{20 + number}}T18:00:00Z"}""";
        pipeline.Jira.Handler.Override(HttpMethod.Get, list, _ => true, request =>
        {
            if (request.Query.GetValueOrDefault("page", "1") != "1")
            {
                return StubHttpHandler.Json($"[{Entry(2)},{Entry(1)}]")(request);
            }

            var first = StubHttpHandler.Json($"[{Entry(3)},{Entry(2)}]")(request);
            first.Headers.TryAddWithoutValidation("Link", $"<{FakeGitHub.ApiUrl}repos/{FakeGitHub.Repository}/pulls?state=all&sort=updated&direction=desc&per_page=100&page=2>; rel=\"next\"");
            return first;
        });

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Three pull requests, each asked for its reviews once: three merges, median two days, and three
        // closed without a first code review.
        const string totals =
            "SELECT t.sample_count, t.median_elapsed_days, w.closed_without_review FROM pr_timing_per_sprint t "
            + "JOIN pr_without_review_per_sprint w ON w.sprint_id = t.sprint_id AND w.host_scope = t.host_scope WHERE t.sprint_id = '42' AND t.measure = 'merge'";
        await Assert.That(github.PullRequestPagesAsked).IsEquivalentTo(new[] { "1", "2" });
        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 3L });
        await Assert.That((await pipeline.QueryAsync<(long, double, long)>(totals)).ToArray()).IsEquivalentTo(new[] { (3L, 2d, 3L) });

        // Collected again, by a sync and whole by a backfill, and derived again: still three, and no review asked for twice.
        await pipeline.RunAllAsync(["sync"], ["backfill"], ["run", "--sprint", "42"]);

        await Assert.That(github.ReviewsAskedFor).IsEquivalentTo(new[] { 1, 2, 3 });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 3L });
        await Assert.That((await pipeline.QueryAsync<(long, double, long)>(totals)).ToArray()).IsEquivalentTo(new[] { (3L, 2d, 3L) });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| Merge | 3 | 2.0 | 3.0 | 2.0 | 3.0 |\n");
    }

    /// <summary>
    /// The <c>ado</c> setting for a project and repository whose names are those of <see cref="FakeGitHub.Repository"/>,
    /// and the routes of its one pull request, number 7, which the list gives twice in its first answer and
    /// which has no thread.
    /// </summary>
    private static void AddAnAzureDevOpsRepositoryNamedAsGitHubsThatListsPullRequest7Twice(PipelineHarness pipeline)
    {
        pipeline.Config["ado"] = new JsonObject
        {
            ["apiUrl"] = FakeAdo.ApiUrl,
            ["organization"] = FakeAdo.Organization,
            ["repositories"] = new JsonArray(FakeGitHub.Repository),
        };
        const string repository = "/" + FakeAdo.Organization + "/fake-org/_apis/git/repositories/fake-sample";
        const string pullRequest =
            """
            {"pullRequestId":7,"status":"completed","createdBy":{"id":"a0000001-0000-4000-8000-00000000f001","displayName":"Fake Ado Author One"},
             "creationDate":"2026-01-20T18:00:00.000Z","closedDate":"2026-01-23T18:00:00.000Z","title":"Synthetic title","reviewers":[]}
            """;
        pipeline.Jira.Handler.On(HttpMethod.Get, repository + "/pullrequests", request => StubHttpHandler.Json(
            request.Query.GetValueOrDefault("$skip", "0") == "0" ? $$"""{"count":2,"value":[{{pullRequest}},{{pullRequest}}]}""" : """{"count":0,"value":[]}""")(request));
        pipeline.Jira.Handler.On(HttpMethod.Get, repository + "/pullRequests/7/threads", StubHttpHandler.Json("""{"count":0,"value":[]}"""));
    }
}
