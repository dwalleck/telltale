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
    public async Task An_abandoned_resolution_id_that_is_null_or_blank_is_refused(string ids)
    {
        // A blank entry is a slip of the hand; read as an ID it would match nothing, or every unresolved issue.
        await using var pipeline = new PipelineHarness();
        AddSprint(pipeline.Jira);
        pipeline.Config["abandonedResolutionIds"] = JsonNode.Parse(ids);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).EndsWith(
            "telltale.json is not valid: abandonedResolutionIds must hold resolution IDs, and one entry is empty.\n");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
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
    public async Task An_issue_type_jira_marks_as_a_sub_task_or_places_off_the_standard_level_is_left_out(bool subtask, int hierarchyLevel)
    {
        // Either sign is enough on its own, and a level above epics is a container too.
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
