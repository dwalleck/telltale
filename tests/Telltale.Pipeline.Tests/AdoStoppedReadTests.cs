using System.Net;
using System.Text.Json.Nodes;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// An Azure DevOps read that stops after it read the threads of some pull requests (PR timing, review 2,
/// finding 11). Those pull requests are stored with their events, as on GitHub, so that the next read asks
/// only for what the store lacks: a first read of a large repository would otherwise start from nothing
/// every time it stopped late. The read still failed: the host is recorded as failed, with the start date
/// and the repositories the store recorded before, and the message says what was kept. Three things stop a
/// read that way: the third pull request in a row whose threads cannot be read, a client that gave up on a
/// thread request, and the list of a later repository that cannot be read. The synthetic host is
/// <see cref="FakeAdo"/>; sprint 42 runs from Monday 19 January to Monday 2 February 2026.
/// </summary>
public class AdoStoppedReadTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 AND sprint_id IS NOT NULL ORDER BY sprint_id, measure_position";
    private const string Histories = "SELECT pr_number || ': ' || history FROM pull_request WHERE host = 'ado' ORDER BY pr_number";
    private const string HostRow = "SELECT state || ', from ' || COALESCE(collect_from, 'no date') || ', repositories ' || COALESCE(repositories, 'not recorded') || ', source ' || COALESCE(source, 'none') FROM pull_request_host WHERE host = 'ado'";
    private const string Kept = " pull requests were read before that and are stored, and the next sync reads the rest. Until a read of Azure DevOps finishes, a report says that Azure DevOps's figures rest on a read that failed.\n";

    private static readonly Func<StubRequest, HttpResponseMessage> Refusal = StubHttpHandler.Json("""{"message":"Synthetic refusal naming Fake Ado Reviewer One"}""", HttpStatusCode.Forbidden);

    private static string ThreadsCall(int number) => $"GET Fake%20Project/_apis/git/repositories/fake-ado-sample/pullRequests/{number}/threads?api-version=7.1";

    private static (PipelineHarness Pipeline, FakeAdo Ado) AdoOnly()
    {
        var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        pipeline.Secrets.Remove(SecretName.GitHubToken);
        return (pipeline, FakeAdo.For(pipeline));
    }

    /// <summary>Pull requests 1 to 3, each voted on a day after it was opened and completed a day after that.</summary>
    private static void AddThreeCompleted(FakeAdo ado)
    {
        foreach (var number in new[] { 1, 2, 3 })
        {
            ado.AddPullRequest(number, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 21, 12)).Completed(Local(2026, 1, 22, 12));
        }
    }

    [Test]
    public async Task A_third_unreadable_pull_request_in_a_row_stops_the_read_and_those_read_before_it_are_stored_and_not_asked_for_again()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        AddThreeCompleted(ado);
        var refused = true;
        foreach (var number in new[] { 4, 5, 6 })
        {
            ado.AddPullRequest(number, AdoIdentities.AuthorTwo, Local(2026, 1, 20, 12)).Voted(AdoIdentities.Reviewer, 10, Local(2026, 1, 23, 12));
            pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(number), _ => refused, Refusal);
        }

        var exit = await pipeline.RunAsync("sync");

        // The command fails and says what it kept. The two unreadable ones before the third are stored
        // without their threads, and the command says that of each; the third is not stored at all.
        await Assert.That(exit).IsEqualTo(1);
        var stored = "It is stored without them and counted as a pull request whose history was not read; its threads are asked for again at the next sync.\n";
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Azure DevOps: the threads of a pull request could not be read (Azure DevOps answered 403 (Forbidden) to {ThreadsCall(4)}.). " + stored
            + $"Azure DevOps: the threads of a pull request could not be read (Azure DevOps answered 403 (Forbidden) to {ThreadsCall(5)}.). " + stored
            + $"Azure DevOps answered 403 (Forbidden) to {ThreadsCall(6)}. It was the third pull request in a row whose threads could not be read, so the read was stopped. "
            + "The threads of 3" + Kept);
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: read", "4: failed", "5: failed" });
        await Assert.That((await pipeline.QueryAsync<long>("SELECT DISTINCT pr_number FROM pull_request_event ORDER BY pr_number")).ToArray()).IsEquivalentTo(new[] { 1L, 2L, 3L });

        // The host is recorded as failed, with no start date and no repository gone through, as before the
        // read, and with the source the rows were read from.
        await Assert.That((await pipeline.QueryAsync<string>(HostRow)).ToArray()).IsEquivalentTo(new[] { "failed, from no date, repositories not recorded, source https://ado.example.invalid/fake-ado-org/" });

        // A report of the store as it is shows the three it holds and says the read failed.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT DISTINCT coverage FROM pr_timing_per_sprint")).ToArray()).IsEquivalentTo(new[] { "failed read" });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("- Azure DevOps could not be read when pull requests were last collected.");

        // The next read asks for the three it lacks and for none of the three it has.
        refused = false;
        var asked = ado.ThreadsAskedFor.Count;
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(ado.ThreadsAskedFor.Skip(asked).ToArray()).IsEquivalentTo(new[] { 4, 5, 6 });
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: read", "4: read", "5: read", "6: read" });
        await Assert.That((await pipeline.QueryAsync<string>(HostRow)).ToArray()).IsEquivalentTo(new[] { "read, from no date, repositories [\"fake project/fake-ado-sample\"], source https://ado.example.invalid/fake-ado-org/" });

        // Three waited one day for the vote and three waited three: the median of the six is two.
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 6L, 2d), ("42", "first_code_approval", 6L, 2d), ("42", "merge", 3L, 2d) });
        await Assert.That(await pipeline.ReadReportAsync(42)).DoesNotContain("Azure DevOps could not be read");
    }

    [Test]
    public async Task A_client_that_gives_up_on_a_thread_request_keeps_the_pull_requests_read_before_it_and_the_start_date_the_store_recorded()
    {
        // A first read under a start date ends. Then the date is taken out, three more pull requests are
        // there, and the read stops at the last of them.
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        pipeline.Config["ado"]!["collectFrom"] = "2026-01-10";
        ado.AddPullRequest(9, AdoIdentities.AuthorTwo, Local(2026, 1, 12, 12)).Completed(Local(2026, 1, 13, 12));
        await pipeline.RunAllAsync(["sync"]);
        var recorded = (await pipeline.QueryAsync<string>("SELECT collect_from FROM pull_request_host WHERE host = 'ado'")).Single();

        ((JsonObject)pipeline.Config["ado"]!).Remove("collectFrom");
        AddThreeCompleted(ado);
        var limited = true;
        pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(3), _ => limited, StubHttpHandler.RateLimited(3600));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"Azure DevOps is limiting how often it can be asked: it answered 429 (too many requests) to {ThreadsCall(3)} and asked for a wait of more than 5 minutes, or one this tool cannot read. Run the command again later. "
            + "The threads of 2" + Kept);
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "9: read" });
        await Assert.That((await pipeline.QueryAsync<string>(HostRow)).ToArray()).IsEquivalentTo(new[] { $"failed, from {recorded}, repositories [\"fake project/fake-ado-sample\"], source https://ado.example.invalid/fake-ado-org/" });

        limited = false;
        var asked = ado.ThreadsAskedFor.Count;
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That(ado.ThreadsAskedFor.Skip(asked).ToArray()).IsEquivalentTo(new[] { 3 });
        await Assert.That((await pipeline.QueryAsync<string>(HostRow)).ToArray()).IsEquivalentTo(new[] { "read, from no date, repositories [\"fake project/fake-ado-sample\"], source https://ado.example.invalid/fake-ado-org/" });
    }

    [Test]
    public async Task A_list_of_a_later_repository_that_cannot_be_read_keeps_the_pull_requests_read_of_the_repositories_before_it()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        AddThreeCompleted(ado);
        pipeline.Config["ado"]!["repositories"] = new JsonArray(FakeAdo.Repository, "Fake Project/fake-ado-other");
        var refused = true;
        const string otherList = "/fake-ado-org/Fake%20Project/_apis/git/repositories/fake-ado-other/pullrequests";
        pipeline.Jira.Handler.On(HttpMethod.Get, otherList, _ => refused, Refusal);
        pipeline.Jira.Handler.On(HttpMethod.Get, otherList, StubHttpHandler.Json("""{"value":[],"count":0}"""));

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Azure DevOps answered 403 (Forbidden) to GET Fake%20Project/_apis/git/repositories/fake-ado-other/pullrequests?searchCriteria.status=all&$top=100&$skip=0&api-version=7.1. "
            + "The threads of 3" + Kept);
        await Assert.That((await pipeline.QueryAsync<string>(Histories)).ToArray()).IsEquivalentTo(new[] { "1: read", "2: read", "3: read" });
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'ado'")).ToArray()).IsEquivalentTo(new[] { "failed" });

        // The next read asks for no thread: the store has every pull request of the first repository as it is.
        refused = false;
        var asked = ado.ThreadsAskedFor.Count;
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That(ado.ThreadsAskedFor.Count).IsEqualTo(asked);
        await Assert.That((await pipeline.QueryAsync<string>("SELECT state FROM pull_request_host WHERE host = 'ado'")).ToArray()).IsEquivalentTo(new[] { "read" });
    }

    [Test]
    public async Task A_read_that_stops_before_the_threads_of_any_pull_request_were_read_stores_nothing_of_it()
    {
        var (pipeline, ado) = AdoOnly();
        await using var _ = pipeline;
        foreach (var number in new[] { 1, 2, 3 })
        {
            ado.AddPullRequest(number, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12));
            pipeline.Jira.Handler.Override(HttpMethod.Get, FakeAdo.ThreadsPath(number), _ => true, Refusal);
        }

        var exit = await pipeline.RunAsync("sync");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("are stored").And.DoesNotContain("It is stored without them");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pull_request")).IsEquivalentTo(new[] { 0L });
    }
}
