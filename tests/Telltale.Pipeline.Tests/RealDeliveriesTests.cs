using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>Abandoned issues, epics and sub-tasks are not deliveries (glossary: Delivered; system design: Scope).</summary>
public class RealDeliveriesTests
{
    // Synthetic resolution IDs. Which of them mean "abandoned" is the configuration's call, never the name's.
    private const string ResolutionFinished = "300001";
    private const string ResolutionWontDo = "300002";
    private const string ResolutionDuplicate = "300003";

    [Test]
    public async Task An_issue_that_is_done_with_an_abandoned_resolution_is_not_delivered()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = new JsonArray(ResolutionWontDo);
        FinishedInSprint(pipeline.Jira, "FAKE-1").Resolved(ResolutionFinished);
        FinishedInSprint(pipeline.Jira, "FAKE-2").Resolved(ResolutionWontDo);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", 1L) }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task An_issue_that_is_done_with_any_other_resolution_or_none_is_delivered()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = new JsonArray(ResolutionWontDo);
        FinishedInSprint(pipeline.Jira, "FAKE-1").Resolved(ResolutionFinished);
        FinishedInSprint(pipeline.Jira, "FAKE-2").Resolved(ResolutionDuplicate);
        FinishedInSprint(pipeline.Jira, "FAKE-3");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", 3L) }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    [Arguments("", 3L)]
    [Arguments(ResolutionDuplicate, 2L)]
    [Arguments(ResolutionWontDo + "," + ResolutionDuplicate, 1L)]
    public async Task Which_resolutions_mean_abandoned_comes_from_the_configuration(string abandoned, long expectedDelivered)
    {
        // The same Jira every time; only the configuration file differs.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = new JsonArray([.. abandoned.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => (JsonNode)id)]);
        FinishedInSprint(pipeline.Jira, "FAKE-1").Resolved(ResolutionFinished);
        FinishedInSprint(pipeline.Jira, "FAKE-2").Resolved(ResolutionWontDo);
        FinishedInSprint(pipeline.Jira, "FAKE-3").Resolved(ResolutionDuplicate);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", expectedDelivered) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_configuration_that_does_not_say_which_resolutions_mean_abandoned_is_refused()
    {
        // Leaving the setting out would count abandoned work as delivered without anyone having decided so.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config.Remove("abandonedResolutionIds");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid").And.Contains("abandonedResolutionIds");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain("   at ");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    [Arguments("[null]")]
    [Arguments("[\"\"]")]
    [Arguments("[\"300002\", \" \"]")]
    [Arguments("[\"Fake Won't Do\"]")]
    [Arguments("[\"30 0002\"]")]
    public async Task An_abandoned_resolution_entry_that_is_not_a_jira_id_is_refused(string ids)
    {
        // Jira's resolution IDs are digits. A name or a slip of the hand would match no issue, and abandoned
        // work would count as delivered with nothing to show for it.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = JsonNode.Parse(ids);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith(
            "telltale.json is not valid: abandonedResolutionIds: every entry must be a Jira resolution ID (digits only), and one is not.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task An_abandoned_resolution_id_with_spaces_around_it_still_matches()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = new JsonArray($" {ResolutionWontDo}  ");
        FinishedInSprint(pipeline.Jira, "FAKE-1").Resolved(ResolutionFinished);
        FinishedInSprint(pipeline.Jira, "FAKE-2").Resolved(ResolutionWontDo);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", 1L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_abandoned_issue_keeps_its_cycle_end_but_is_not_delivered_and_has_no_delivery_sprint()
    {
        // Spec, metric rules: delivered is "has a cycle end and its resolution is not in the abandoned list".
        // issue_cycle is a derived table, not a view; later views filter on its delivered flag, so its content is pinned here.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = new JsonArray(ResolutionWontDo);
        FinishedInSprint(pipeline.Jira, "FAKE-1").Resolved(ResolutionFinished);
        FinishedInSprint(pipeline.Jira, "FAKE-2").Resolved(ResolutionWontDo);

        await pipeline.RunAsync("run", "--sprint", "41");

        var rows = await pipeline.QueryAsync<(string IssueKey, string? CycleEnd, long Delivered, string? DeliverySprintId)>(
            "SELECT i.issue_key, c.cycle_end, c.delivered, c.delivery_sprint_id FROM issue_cycle c JOIN issue i ON i.issue_id = c.issue_id ORDER BY i.issue_key;");

        // Both were moved to Done on 9 January at 15:00 in the team's zone, which is 21:00 UTC.
        await Assert.That(rows).IsEquivalentTo(
            new (string, string?, long, string?)[]
            {
                ("FAKE-1", "2026-01-09T21:00:00.000Z", 1L, "41"),
                ("FAKE-2", "2026-01-09T21:00:00.000Z", 0L, null),
            },
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Epics_and_sub_tasks_change_no_figure_in_the_report_or_the_views()
    {
        // The same story on two sites. The second site also has an epic and a sub-task, both finished in the sprint.
        await using var storyOnly = new PipelineHarness();
        AddSprint(storyOnly.Jira);
        FinishedInSprint(storyOnly.Jira, "FAKE-1").AssignedTo(People.One);
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        FinishedInSprint(pipeline.Jira, "FAKE-1").AssignedTo(People.One);
        FinishedInSprint(pipeline.Jira, "FAKE-2").OfType(FakeIssueType.Epic).AssignedTo(People.Two);
        FinishedInSprint(pipeline.Jira, "FAKE-3").OfType(FakeIssueType.SubTask).AssignedTo(People.Three);

        var storyOnlyExit = await storyOnly.RunAsync("run", "--sprint", "41");
        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(storyOnlyExit).IsEqualTo(0);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", 1L) }, CollectionOrdering.Matching);
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 1\n");
        await Assert.That(report).IsEqualTo(await storyOnly.ReadReportAsync(41));
    }

    [Test]
    [Arguments(true, 0)]
    [Arguments(false, -1)]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(true, null)]
    public async Task An_issue_type_jira_marks_as_a_sub_task_or_places_off_the_standard_level_is_left_out(bool subtask, int? hierarchyLevel)
    {
        // Either sign is enough on its own (the flag even without a level), and a level above epics is a container too.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        FinishedInSprint(pipeline.Jira, "FAKE-1");
        FinishedInSprint(pipeline.Jira, "FAKE-2").OfType(new FakeIssueType("800009", "Fake Odd Type", subtask, hierarchyLevel));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", 1L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Nothing_of_an_epic_or_a_sub_task_is_stored_and_no_history_is_asked_for_it()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        var story = FinishedInSprint(pipeline.Jira, "FAKE-11");
        FinishedInSprint(pipeline.Jira, "FAKE-12").OfType(FakeIssueType.Epic).AssignedTo(People.Two);
        FinishedInSprint(pipeline.Jira, "FAKE-13").OfType(FakeIssueType.SubTask).AssignedTo(People.Three);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var store = (await pipeline.ReadAllFilesAsync())["store.db"];
        await Assert.That(store).Contains("FAKE-11").And.DoesNotContain("FAKE-12").And.DoesNotContain("FAKE-13");
        await Assert.That(store).DoesNotContain(FakeIssueType.Epic.Id).And.DoesNotContain(FakeIssueType.SubTask.Id);
        var request = pipeline.Jira.Handler.Requests.Single(request => request.Path == "/rest/api/3/changelog/bulkfetch");
        using var body = System.Text.Json.JsonDocument.Parse(request.Body);
        var asked = body.RootElement.GetProperty("issueIdsOrKeys").EnumerateArray().Select(id => id.GetString() ?? "").ToList();
        await Assert.That(asked).IsEquivalentTo(new[] { story.Id });
    }

    [Test]
    [Arguments(true, -1)]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    public async Task A_stored_issue_that_became_an_epic_or_a_sub_task_leaves_the_store_and_the_figures_at_the_next_sync(bool subtask, int hierarchyLevel)
    {
        // Owner decision D13. Both issues were stories at the first sync; FAKE-2 is then converted in Jira.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        FinishedInSprint(pipeline.Jira, "FAKE-1");
        var converted = FinishedInSprint(pipeline.Jira, "FAKE-2");
        await pipeline.RunAsync("run", "--sprint", "41");
        var before = await DeliveredPerSprintAsync(pipeline);
        converted.OfType(new FakeIssueType("800009", "Fake Converted Type", subtask, hierarchyLevel));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(before).IsEquivalentTo(new[] { ("41", 2L) }, CollectionOrdering.Matching);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", 1L) }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");

        // Its raw rows are gone, the issue and its history; the other issue keeps both of its changes.
        var issueKeys = await pipeline.QueryAsync<string>("SELECT issue_key FROM issue ORDER BY issue_key;");
        await Assert.That(issueKeys).IsEquivalentTo(new[] { "FAKE-1" }, CollectionOrdering.Matching);
        var changes = await pipeline.QueryAsync<(string IssueId, long Changes)>("SELECT issue_id, COUNT(*) FROM change GROUP BY issue_id ORDER BY issue_id;");
        await Assert.That(changes).IsEquivalentTo(new[] { (pipeline.Jira.Issues[0].Id, 2L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_epic_whose_other_fields_have_an_unknown_shape_does_not_stop_the_run()
    {
        // The epic is out of scope, so nothing else of it is read: not its status, its dates or its assignee.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        var story = FinishedInSprint(pipeline.Jira, "FAKE-1");
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/search/jql", _ => true, StubHttpHandler.Json($$"""
            {"issues":[
              {"id":"{{story.Id}}","key":"FAKE-1","fields":{"issuetype":{"id":"800001","subtask":false,"hierarchyLevel":0},"created":"2026-01-06T15:00:00.000+0000","updated":"2026-01-09T21:00:00.000+0000","status":{"id":"{{Workflow.Done.Id}}"} } },
              {"id":"900099","key":null,"fields":{"issuetype":{"id":"800003","subtask":false,"hierarchyLevel":1},"created":"synthetic text that is not a time","status":null,"assignee":{} } }
            ]}
            """));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await DeliveredPerSprintAsync(pipeline)).IsEquivalentTo(new[] { ("41", 1L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Every_standard_issue_type_counts_not_only_stories()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        FinishedInSprint(pipeline.Jira, "FAKE-1");
        FinishedInSprint(pipeline.Jira, "FAKE-2").OfType(new FakeIssueType("800004", "Fake Bug", Subtask: false, HierarchyLevel: 0));

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    public async Task An_issue_type_without_a_hierarchy_level_stops_the_run_and_nothing_is_stored()
    {
        // Without the level nothing says whether the issue is an epic, and guessing would put it in the figures.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        FinishedInSprint(pipeline.Jira, "FAKE-1").OfType(new FakeIssueType("800009", "Fake Odd Type", Subtask: false, HierarchyLevel: null));

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Jira's issue search answered in a shape this tool does not understand (KeyNotFoundException). Nothing was stored.\n");
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files.Keys).Contains("store.db");
        await Assert.That(files["store.db"]).DoesNotContain("FAKE-1").And.DoesNotContain("Fake Sprint 41");
    }

    private static Task<IReadOnlyList<(string SprintId, long DeliveredCount)>> DeliveredPerSprintAsync(PipelineHarness pipeline) =>
        pipeline.QueryAsync<(string SprintId, long DeliveredCount)>("SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");

    /// <summary>Sprint 41, 5 to 19 January 2026, completed.</summary>
    private static void AddSprint(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));

    /// <summary>An issue worked on and moved to Done inside sprint 41.</summary>
    private static FakeIssue FinishedInSprint(FakeJira jira, string key) =>
        jira.AddIssue(key, Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
}
