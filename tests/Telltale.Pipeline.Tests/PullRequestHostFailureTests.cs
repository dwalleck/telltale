using System.Net;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What a command still does when a pull request host fails after Jira was read and stored (PR timing,
/// review 2, findings 6 and 22): a backfill says what it replaced, and <c>run</c> writes the report, which
/// marks the host as one whose last read failed. Either way the command ends with the host's message and
/// exit code 1. The synthetic host is <see cref="FakeGitHub"/>; sprint 42 runs from Monday 19 January to
/// Monday 2 February 2026.
/// </summary>
public class PullRequestHostFailureTests
{
    private const string PullsPath = "/api/v3/repos/" + FakeGitHub.Repository + "/pulls";
    private const string Refused = "GitHub answered 401 (Unauthorized) to GET repos/fake-org/fake-sample/pulls?state=all&sort=updated&direction=desc&per_page=100.\n";

    /// <summary>GitHub does not take the token: every request for the list of pull requests answers 401.</summary>
    private static void RefuseTheToken(PipelineHarness pipeline) =>
        pipeline.Jira.Handler.Override(HttpMethod.Get, PullsPath, _ => true, StubHttpHandler.Json("""{"message":"Synthetic refusal"}""", HttpStatusCode.Unauthorized));

    [Test]
    public async Task A_backfill_says_what_it_replaced_although_a_pull_request_host_then_fails()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        Delivered(pipeline.Jira, 42, "FAKE-11", points: 3, People.One, days: 1);
        Delivered(pipeline.Jira, 42, "FAKE-12", points: 3, People.Two, days: 1);
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        RefuseTheToken(pipeline);

        var exit = await pipeline.RunAsync("backfill");

        // Jira's batch is committed by then, so its counts are said; the command still fails with GitHub's message.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Refused);
        await Assert.That(pipeline.Out.ToString()).Contains("The store now holds what Jira returned. Issues: 0 before, 2 now. Sprints: 0 before, 2 now.");
    }

    [Test]
    public async Task Run_still_derives_and_writes_the_report_when_only_a_pull_request_host_failed_and_then_fails()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        Delivered(pipeline.Jira, 42, "FAKE-11", points: 3, People.One, days: 1);
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        RefuseTheToken(pipeline);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        // The report is written from what the store holds, and says of GitHub that its last read failed.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Refused);
        await Assert.That(pipeline.Out.ToString()).Contains("Wrote ");
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("\nDelivered issues: 1\n");
        await Assert.That(report).Contains("- GitHub could not be read when pull requests were last collected.");
    }

    [Test]
    public async Task Run_writes_no_report_when_Jira_fails_whatever_the_pull_request_hosts_would_answer()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12));
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true, StubHttpHandler.Json("""{"errorMessages":["Synthetic refusal"]}""", HttpStatusCode.Forbidden));

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(github.Requests).IsEmpty();
        await Assert.That(Directory.Exists(pipeline.ReportDirectory) && Directory.EnumerateFiles(pipeline.ReportDirectory).Any()).IsFalse();
    }
}
