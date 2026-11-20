using System.Text;
using System.Text.Json.Nodes;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Collecting pull requests from Azure DevOps (PR timing, ticket 05): only when the <c>ado</c> setting is
/// there, read-only with the token from the secret store, every answer of every list, no more requests than
/// are needed, and the same rows however often it runs. The synthetic host is <see cref="FakeAdo"/>; sprint 42
/// runs from Monday 19 January to Monday 2 February 2026. No test here has a GitHub token.
/// </summary>
public class AdoPullRequestCollectionTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 ORDER BY sprint_id, measure_position";

    /// <summary>Stands for a secret pasted into the wrong place: nothing printed may hold any part of it.</summary>
    private const string Pasted = "synthetic-pasted";

    private static (PipelineHarness Pipeline, FakeAdo Ado) AdoOnly()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        return (pipeline, FakeAdo.For(pipeline));
    }

    [Test]
    public async Task With_the_ado_setting_and_no_token_the_command_stops_before_anything_is_asked()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Secrets.Remove(SecretName.AdoToken);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        // Missing access is a failure that names the secret, never a host without pull requests.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The secret 'Azure DevOps token' is not set in the secret store. Run 'telltale secrets set ado-token'.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task Without_the_ado_setting_nothing_is_asked_of_azure_devops_and_its_token_is_not_needed()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.AdoToken);
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(pipeline.Jira.Handler.Requests.Where(request => request.Path.Contains("_apis", StringComparison.Ordinal))).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(42)).DoesNotContain("Azure DevOps");
    }

    [Test]
    public async Task Every_request_is_a_read_with_the_token_from_the_secret_store_under_the_configured_address_and_asks_for_pull_requests_in_every_state()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 21, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // The list, the answer after it that says the list is over, and the threads of the one pull request.
        await Assert.That(ado.Requests.Count).IsEqualTo(3);
        await Assert.That(ado.Requests.All(request => request.Method == HttpMethod.Get)).IsTrue();

        // A personal access token goes as the password of basic authentication, with no user name.
        var basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + PipelineHarness.AdoToken));
        await Assert.That(ado.Requests.Select(request => request.Headers.Authorization?.ToString() ?? "none").Distinct()).IsEquivalentTo(new[] { basic });
        await Assert.That(ado.Requests.Where(request => request.Path == FakeAdo.ListPath).Select(request => request.Query["searchCriteria.status"]).Distinct()).IsEquivalentTo(new[] { "all" });
        await Assert.That(ado.Requests.All(request => request.Query.ContainsKey("api-version"))).IsTrue();
        await Assert.That((await pipeline.QueryAsync<string>("SELECT repository FROM pull_request WHERE host = 'ado'")).ToArray()).IsEquivalentTo(new[] { FakeAdo.RepositoryKey });
    }

    [Test]
    public async Task Every_answer_of_the_list_and_of_a_pull_requests_threads_is_read()
    {
        // The host gives two pull requests an answer, whatever is asked: five are three answers, and a fourth
        // that is empty ends the list. It gives one thread an answer, and each pull request has three: the
        // scanner's comment, a general comment, and on the third answer a person's comment on a file.
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.PageSize = 2;
        ado.ThreadsPerAnswer = 1;
        foreach (var (author, number) in AdoIdentities.Authors.Select((author, index) => (author, index + 1)))
        {
            ado.AddPullRequest(number, author, Local(2026, 1, 20, 12))
                .FileComment(AdoIdentities.Scanner, Local(2026, 1, 20, 13))
                .Comment(AdoIdentities.Reviewer, Local(2026, 1, 20, 14))
                .FileComment(AdoIdentities.Reviewer, Local(2026, 1, 22, 12));
        }

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 5L, 2d) });
        await Assert.That(ado.ListRowsSkipped).IsEquivalentTo(new[] { "0", "2", "4", "5" });
        await Assert.That(ado.ThreadsAskedFor.Count).IsEqualTo(15);
    }

    [Test]
    public async Task Running_sync_and_derive_again_adds_no_observation_and_changes_no_wait()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12))
            .FileComment(AdoIdentities.Scanner, Local(2026, 1, 20, 13))
            .FileComment(AdoIdentities.Reviewer, Local(2026, 1, 21, 12))
            .Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 22, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12))
            .Voted(AdoIdentities.Reviewer, 5, Local(2026, 1, 23, 12))
            .Completed(Local(2026, 1, 26, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var figures = await pipeline.QueryAsync<(string, string, long, double)>(Figures);
        var store = await pipeline.ReadStoreContentAsync(withoutAsOf: true);
        var report = await pipeline.ReadReportBytesAsync(42, withoutAsOf: true);
        await Assert.That(figures.ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 2L, 2d), ("42", "first_code_approval", 2L, 2.5d), ("42", "merge", 1L, 6d) });

        await pipeline.RunAllAsync(["sync"], ["derive"], ["sync"], ["backfill"], ["run", "--sprint", "42"]);

        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(figures.ToArray());
        await Assert.That(await pipeline.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(store);
        await Assert.That(await pipeline.ReadReportBytesAsync(42, withoutAsOf: true)).IsEqualTo(report);
    }

    [Test]
    public async Task A_later_sync_asks_for_the_threads_of_open_pull_requests_and_of_one_closed_since_and_never_again_of_one_read_after_it_closed()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 21, 12)).Completed(Local(2026, 1, 22, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12)).Abandoned(Local(2026, 1, 22, 12));
        var closing = ado.AddPullRequest(3, AdoIdentities.AuthorThree, Local(2026, 1, 20, 12));
        var open = ado.AddPullRequest(4, AdoIdentities.AuthorFour, Local(2026, 1, 20, 12));
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 4 });

        // Nothing changed. The list does not say whether an open pull request changed, so the two open ones
        // are read again; the completed and the abandoned one were read after they closed and cost nothing.
        await pipeline.RunAllAsync(["sync"]);
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 4, 3, 4 });

        // Pull request 3 is approved and completed, and pull request 4 is reviewed. Each is read at the next
        // sync; at the one after, pull request 3 is closed as stored and only pull request 4 is still read.
        closing.Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 27, 12)).Completed(Local(2026, 1, 28, 12));
        open.FileComment(AdoIdentities.Reviewer, Local(2026, 1, 26, 12));
        await pipeline.RunAllAsync(["sync"], ["sync"], ["backfill"], ["derive"]);

        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 1, 2, 3, 4, 3, 4, 3, 4, 4, 4 });
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray())
            .IsEquivalentTo(new[] { ("42", "first_code_review", 3L, 6d), ("42", "first_code_approval", 2L, 4d), ("42", "merge", 2L, 5d) });
    }

    [Test]
    public async Task A_pull_request_closed_before_the_start_date_is_never_asked_for_and_one_opened_before_it_and_still_open_or_closed_after_it_is_read()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        pipeline.Config["ado"]!["collectFrom"] = "2026-01-15";
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 6, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 7, 12)).Completed(Local(2026, 1, 8, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 6, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 20, 12)).Completed(Local(2026, 1, 21, 12));
        ado.AddPullRequest(3, AdoIdentities.AuthorThree, Local(2026, 1, 6, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"], ["sync"]);

        // The bound is on the closing, not on the opening: all three were opened before the date.
        await Assert.That(ado.ThreadsAskedFor).IsEquivalentTo(new[] { 2, 3, 3 });
        await Assert.That((await pipeline.QueryAsync<long>("SELECT pr_number FROM pull_request ORDER BY pr_number")).ToArray()).IsEquivalentTo(new[] { 2L, 3L });
    }

    [Test]
    public async Task A_host_that_refuses_stops_the_command_and_is_recorded_as_failed_not_as_read_and_empty()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 21, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state || ', roster ' || roster FROM pull_request_host WHERE host = 'ado'")).ToArray()).IsEquivalentTo(new[] { "read, roster not configured" });

        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ListPath, _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal naming Fake Ado Author One"}""", System.Net.HttpStatusCode.Unauthorized));
        pipeline.Error.GetStringBuilder().Clear();
        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Azure DevOps answered 401 (Unauthorized) to GET Fake%20Project/_apis/git/repositories/fake-ado-sample/pullrequests?searchCriteria.status=all&$top=100&$skip=0&api-version=7.1.\n");

        // What earlier reads stored stays, and the store says the last read failed.
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state || ', roster ' || roster FROM pull_request_host WHERE host = 'ado'")).ToArray()).IsEquivalentTo(new[] { "failed, roster not read" });
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 1L });
    }

    [Test]
    public async Task An_answer_in_an_unknown_shape_stops_the_command_without_quoting_it_and_stores_nothing_of_that_read()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Jira.Handler.Override(
            HttpMethod.Get, FakeAdo.ListPath.Replace("/pullrequests", "/pullRequests/1/threads", StringComparison.Ordinal), _ => true,
            StubHttpHandler.Json("""{"threads":"Synthetic text of another shape naming Fake Ado Author One"}"""));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Azure DevOps's pull request threads answered in a shape this tool does not understand (KeyNotFoundException). Nothing was stored.\n");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task Nothing_that_identifies_a_person_or_quotes_a_pull_request_is_written_anywhere()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12))
            .FileComment(AdoIdentities.Scanner, Local(2026, 1, 20, 13))
            .Comment(AdoIdentities.Reviewer, Local(2026, 1, 20, 14))
            .FileComment(AdoIdentities.Reviewer, Local(2026, 1, 21, 12))
            .Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 22, 12))
            .Completed(Local(2026, 1, 23, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorTwo, Local(2026, 1, 21, 12)).Voted(AdoIdentities.AuthorOne, -5, Local(2026, 1, 22, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await HarnessSteps.QueryEveryMetricViewAsync(pipeline);

        // The scan reads the files the data is in: the store holds both pull requests and their events, and the report its section.
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 2L });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT kind || ', ' || outcome || ', ' || actor FROM pull_request_event ORDER BY pr_number, at")).ToArray()).IsEquivalentTo(new[]
        {
            "file comment, commented, automation",
            "file comment, commented, human",
            "vote, approved, human",
            "vote, changes requested, human",
        });
        await Assert.That(files[Path.Combine("reports", "sprint-42.md")]).Contains("## Pull request timing");

        // The authors are there only as salted hashes: two people, neither under an identity ID.
        var authors = await pipeline.QueryAsync<string>("SELECT DISTINCT author_hash FROM pull_request");
        await Assert.That(authors.Count).IsEqualTo(2);
        await Assert.That(authors.All(hash => hash.Length == 64 && hash.All(char.IsAsciiHexDigitLower))).IsTrue();

        string[] forbidden = [.. ado.ForbiddenText, PipelineHarness.AdoToken];
        await Assert.That(forbidden).Contains(AdoIdentities.Reviewer.Id);
        await Assert.That(forbidden).Contains(AdoIdentities.Scanner.DisplayName);
        await Assert.That(forbidden).Contains(ado.PullRequests[0].Title);
        await Assert.That(forbidden).Contains(ado.PullRequests[0].FilePath);
        await Assert.That(forbidden).Contains(ado.PullRequests[0].Threads[0].Comments[0].Content);
        var written = files.Select(file => (Where: file.Key, Text: file.Value))
            .Append((Where: "standard output", Text: pipeline.Out.ToString()))
            .Append((Where: "standard error", Text: pipeline.Error.ToString()));
        var leaks = (from place in written
                     from text in forbidden
                     where place.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                     select $"'{text}' in {place.Where}").ToList();
        await Assert.That(leaks).IsEmpty();

        // The scanner's ID is in the configuration file, which names it, and nowhere else.
        await Assert.That(files.Where(file => file.Value.Contains(AdoIdentities.Scanner.Id, StringComparison.OrdinalIgnoreCase)).Select(file => file.Key)).IsEquivalentTo(new[] { "telltale.json" });

        // And no metric view gives a hash out.
        var views = pipeline.Out.ToString();
        await Assert.That(authors.Where(hash => views.Contains(hash, StringComparison.OrdinalIgnoreCase))).IsEmpty();
    }

    [Test]
    [Arguments("""{"organization":"fake-ado-org","repositories":[]}""", "ado.repositories must list at least one repository as project/repository.")]
    [Arguments("""{"organization":"fake-ado-org","repositories":["synthetic-pasted"]}""", "ado.repositories: every entry must be a repository written as project/repository, and one is not.")]
    [Arguments("""{"organization":"fake-ado-org","repositories":["Fake Project/fake-ado-sample/synthetic-pasted"]}""", "ado.repositories: every entry must be a repository written as project/repository, and one is not.")]
    [Arguments("""{"organization":"fake-ado-org","repositories":["Fake Project/fake-ado-sample?synthetic-pasted"]}""", "ado.repositories: every entry must be a repository written as project/repository, and one is not.")]
    [Arguments("""{"organization":"synthetic-pasted/other","repositories":["Fake Project/fake-ado-sample"]}""", "ado.organization must be the organization's name as it stands in its address.")]
    [Arguments("""{"organization":"fake-ado-org","repositories":["Fake Project/fake-ado-sample"],"automationIdentities":["synthetic-pasted"]}""", "ado.automationIdentities: every entry must be an identity ID (a GUID), and one is not.")]
    [Arguments("""{"organization":"fake-ado-org","repositories":["Fake Project/fake-ado-sample"],"apiUrl":"http://synthetic-pasted.example.invalid/"}""", "ado.apiUrl must be an absolute https:// address.")]
    [Arguments("""{"organization":"fake-ado-org","repositories":["Fake Project/fake-ado-sample"],"apiUrl":"https://owner:synthetic-pasted@ado.example.invalid/"}""", "ado.apiUrl must not carry a user name or password.")]
    public async Task An_ado_setting_the_tool_cannot_use_is_refused_by_name_before_anything_is_asked_and_is_not_repeated(string ado, string refusal)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Config["ado"] = JsonNode.Parse(ado);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain(Pasted);
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_changed_repository_list_needs_a_derive()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 22, 12));
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        pipeline.Config["ado"]!["repositories"] = new JsonArray("Fake Project/fake-ado-other");
        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The pull request repositories in the configuration file changed since the derived data was built. Run derive first.\n");

        // The stored pull request is of a repository that is no longer listed: it stays in the store and is in no figure.
        await pipeline.RunAllAsync(["derive"]);
        await Assert.That(await pipeline.QueryAsync<(string, string, long, double)>(Figures)).IsEmpty();
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 1L });
    }
}
