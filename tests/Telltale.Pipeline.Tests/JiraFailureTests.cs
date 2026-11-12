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

    [Test]
    public async Task A_tls_or_proxy_failure_is_reported_plainly_and_names_the_proxy_and_the_certificate()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true,
            _ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "synthetic TLS failure"));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Could not reach Jira (GET rest/api/3/status): SecureConnectionError. "
            + "On the corporate network this usually means the proxy or the certificate: check that the system proxy is set and that the corporate root certificate is trusted.\n");
    }

    [Test]
    public async Task A_request_that_times_out_is_reported_plainly()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true,
            _ => throw new TaskCanceledException("synthetic timeout", new TimeoutException()));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Jira did not answer GET rest/api/3/status in time.\n");
    }

    [Test]
    public async Task A_web_page_served_in_place_of_jiras_answer_is_reported_plainly()
    {
        // A proxy block page or a sign-in page arrives as HTML with status 200.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true,
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>synthetic block page for Fake Person One</html>", System.Text.Encoding.UTF8, "text/html") });

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The answer to GET rest/api/3/status was not JSON. A proxy or sign-in page may have been served in place of Jira's answer.\n");
    }

    [Test]
    public async Task An_answer_without_the_expected_fields_is_reported_plainly_and_quotes_nothing()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true,
            StubHttpHandler.Json("""{"issues":[{"id":"900001","key":"FAKE-1","fields":{"created":"not a time by Fake Person One"}}]}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's issue search answered in a shape this tool does not understand (KeyNotFoundException). Nothing was stored.\n");
    }

    [Test]
    public async Task A_timestamp_jira_sends_in_an_unknown_form_is_reported_plainly_and_not_quoted()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, $"/rest/agile/1.0/board/{FakeJira.BoardId}/sprint", _ => true,
            StubHttpHandler.Json("""{"isLast":true,"values":[{"id":41,"state":"closed","name":"Fake Sprint 41","startDate":"5 January, says Fake Person One"}]}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's sprint list answered in a shape this tool does not understand (FormatException). Nothing was stored.\n");
    }

    [Test]
    public async Task A_proxy_that_asks_for_authentication_is_not_blamed_on_jira()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, "/rest/api/3/status", _ => true,
            StubHttpHandler.Json("{}", HttpStatusCode.ProxyAuthenticationRequired));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The proxy asked for authentication (407) on GET rest/api/3/status, so the request never reached Jira. Check the system proxy settings and that this account may use the proxy.\n");
    }

    [Test]
    public async Task The_production_http_handler_offers_the_signed_in_users_credentials_to_the_system_proxy()
    {
        using var handler = Telltale.Cli.AppServices.ForProduction().HttpHandler;

        await Assert.That(handler).IsTypeOf<SocketsHttpHandler>();
        var sockets = (SocketsHttpHandler)handler;
        await Assert.That(sockets.UseProxy).IsTrue();
        await Assert.That(sockets.DefaultProxyCredentials).IsSameReferenceAs(CredentialCache.DefaultCredentials);
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
    public async Task A_sprint_list_that_does_not_say_it_is_the_last_page_and_holds_fewer_sprints_than_the_total_stops_the_run()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, $"/rest/agile/1.0/board/{FakeJira.BoardId}/sprint", _ => true,
            StubHttpHandler.Json("""{"maxResults":1,"startAt":0,"total":2,"values":[{"id":41,"state":"closed","name":"Fake Sprint 41","startDate":"2026-01-05T15:00:00.000Z","completeDate":"2026-01-19T16:00:00.000Z"}]}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Jira's sprint list has more than one page");
    }

    [Test]
    public async Task A_sprint_list_that_says_neither_that_it_is_the_last_page_nor_how_many_sprints_there_are_stops_the_run()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, $"/rest/agile/1.0/board/{FakeJira.BoardId}/sprint", _ => true,
            StubHttpHandler.Json("""{"values":[{"id":41,"state":"closed","name":"Fake Sprint 41","startDate":"2026-01-05T15:00:00.000Z","completeDate":"2026-01-19T16:00:00.000Z"}]}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's sprint list does not say whether it is complete, so sprints could be missing. Paging is not supported yet.\n");
    }

    [Test]
    public async Task A_sprint_list_without_the_last_page_flag_is_accepted_when_its_total_shows_it_is_complete()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, $"/rest/agile/1.0/board/{FakeJira.BoardId}/sprint", _ => true,
            StubHttpHandler.Json("""{"maxResults":50,"startAt":0,"total":1,"values":[{"id":41,"state":"closed","name":"Fake Sprint 41","startDate":"2026-01-05T15:00:00.000Z","completeDate":"2026-01-19T16:00:00.000Z"}]}"""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task An_answer_whose_json_is_not_an_object_is_reported_plainly()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Get, $"/rest/agile/1.0/board/{FakeJira.BoardId}/sprint", _ => true, StubHttpHandler.Json("[]"));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's sprint list answered in a shape this tool does not understand (InvalidOperationException). Nothing was stored.\n");
    }

    [Test]
    public async Task A_search_answer_whose_json_is_not_an_object_is_reported_plainly()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true, StubHttpHandler.Json("\"synthetic text\""));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's issue search answered in a shape this tool does not understand (InvalidOperationException). Nothing was stored.\n");
    }

    [Test]
    [Arguments("""{"issues":[{"id":"900001","key":null,"fields":{"issuetype":{"id":"800001","hierarchyLevel":0}}}]}""")]
    [Arguments("""{"issues":[{"id":"900001","key":"FAKE-1","fields":{"issuetype":{"id":"800001","hierarchyLevel":0},"created":"2026-01-06T15:00:00.000+0000","status":{"id":null}}}]}""")]
    public async Task A_null_where_jira_must_give_a_value_is_reported_plainly_and_nothing_is_stored(string searchAnswer)
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true, StubHttpHandler.Json(searchAnswer));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's issue search answered in a shape this tool does not understand (FormatException). Nothing was stored.\n");

        // "Nothing was stored": the store holds no issue key, and the status and sprint lists fetched before the search are not in it either.
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files.Keys).Contains("store.db");
        await Assert.That(files["store.db"]).DoesNotContain("FAKE-1").And.DoesNotContain("Fake Sprint 41");
    }

    [Test]
    public async Task The_issue_search_is_bounded_to_the_configured_projects_and_asks_for_no_free_text()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Config["jira"]!["projectKeys"] = new System.Text.Json.Nodes.JsonArray("FAKE", "OTHER2");

        await pipeline.RunAsync("run", "--sprint", "41");

        var search = pipeline.Jira.Handler.Requests.Single(request => request.Path == "/rest/api/3/search/jql");
        using var body = System.Text.Json.JsonDocument.Parse(search.Body);
        await Assert.That(body.RootElement.GetProperty("jql").GetString())
            .IsEqualTo("project in (\"FAKE\", \"OTHER2\") AND updated >= \"2025-01-01\" ORDER BY updated ASC");

        // The project keys are configuration (owner decision D2): Jira is not asked for the board's projects.
        await Assert.That(pipeline.Jira.Handler.Requests.Select(request => request.Path)).DoesNotContain($"/rest/agile/1.0/board/{FakeJira.BoardId}/project");
        var fields = body.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()!).ToList();
        await Assert.That(body.RootElement.GetProperty("maxResults").GetInt32()).IsEqualTo(5000);
        await Assert.That(fields).IsEquivalentTo(new[] { "issuetype", "status", "resolution", "assignee", "created", "updated", FakeJira.StoryPointFieldId });
        await Assert.That(pipeline.Jira.Handler.Requests.Where(request => request.Query.ContainsKey("expand") || request.Body.Contains("\"expand\"", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task The_changelog_request_asks_for_the_largest_page_jira_documents()
    {
        // Without it Jira's default page applies and a board of ordinary size needs paging, which is not built yet.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "41");

        var request = pipeline.Jira.Handler.Requests.Single(request => request.Path == "/rest/api/3/changelog/bulkfetch");
        using var body = System.Text.Json.JsonDocument.Parse(request.Body);
        await Assert.That(body.RootElement.GetProperty("maxResults").GetInt32()).IsEqualTo(10000);
    }

    [Test]
    public async Task The_search_date_is_written_in_the_gregorian_calendar_whatever_the_machines_culture()
    {
        // ar-SA uses the Um Al Qura calendar by default, where 2025-01-01 is a date in 1446.
        // The current culture flows with the async context, so this does not touch other tests.
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);

        await pipeline.RunAsync("run", "--sprint", "41");

        var search = pipeline.Jira.Handler.Requests.Single(request => request.Path == "/rest/api/3/search/jql");
        using var body = System.Text.Json.JsonDocument.Parse(search.Body);
        await Assert.That(body.RootElement.GetProperty("jql").GetString()).Contains("updated >= \"2025-01-01\"");
        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith("# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n");
    }

    private static void AddSprintAndIssue(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
    }
}
