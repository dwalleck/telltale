using System.Net;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

public class JiraFailureTests
{
    [Test]
    public async Task A_refusal_from_jira_stops_the_run_with_the_status_and_without_the_response_body()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true,
            StubHttpHandler.Json("""{"errorMessages":["synthetic refusal quoting Fake Person One"]}""", HttpStatusCode.Unauthorized));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Jira answered 401 (Unauthorized) to POST rest/api/3/search/jql.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
    }

    // Paging belongs to a later ticket. Until then a further page must stop the run, never be dropped silently.

    [Test]
    public async Task An_issue_search_with_a_further_page_stops_the_run()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true,
            StubHttpHandler.Json("""{"issues":[],"nextPageToken":"FAKE_TOKEN_PAGE_2","isLast":false}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Jira's issue search has more than one page");
    }

    [Test]
    public async Task A_changelog_with_a_further_page_stops_the_run()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/changelog/bulkfetch", _ => true,
            StubHttpHandler.Json("""{"issueChangeLogs":[],"nextPageToken":"FAKE_CHANGELOG_TOKEN_2"}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Jira's bulk changelog has more than one page");
    }

    [Test]
    public async Task A_sprint_list_with_a_further_page_stops_the_run()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, $"/rest/agile/1.0/board/{FakeJira.BoardId}/sprint", _ => true,
            StubHttpHandler.Json("""{"maxResults":50,"startAt":0,"isLast":false,"values":[]}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Jira's sprint list has more than one page");
    }

    [Test]
    public async Task The_issue_search_is_bounded_to_the_boards_project_and_asks_for_no_free_text()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "41");

        var search = pipeline.Jira.Handler.Requests.Single(request => request.Path == "/rest/api/3/search/jql");
        using var body = System.Text.Json.JsonDocument.Parse(search.Body);
        await Assert.That(body.RootElement.GetProperty("jql").GetString())
            .IsEqualTo("project in (\"FAKE\") AND updated >= \"2025-01-01\" ORDER BY updated ASC");
        var fields = body.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()!).ToList();
        await Assert.That(fields).IsEquivalentTo(new[] { "issuetype", "status", "resolution", "assignee", "created", "updated", FakeJira.StoryPointFieldId });
        await Assert.That(pipeline.Jira.Handler.Requests.Where(request => request.Query.ContainsKey("expand") || request.Body.Contains("\"expand\"", StringComparison.Ordinal))).IsEmpty();
    }

    private static void AddSprintAndIssue(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
    }
}
