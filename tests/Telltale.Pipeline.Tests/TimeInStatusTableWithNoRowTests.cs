using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What the report says in place of a time in status table that has no row, for a sprint with a team. While
/// brief visits are counted, no row means no issue spent time in a status that is not done, and the report
/// says so. When brief visits are left out of time in status (GLOSSARY: Treatment; owner decision D84) a table
/// can also have no row because every issue was left out of every row, so the sentence is another one, true
/// of both, and it is the same whether or not anything was left out: a sentence that depended on that would
/// tell a count of nothing from a withheld one.
/// </summary>
public class TimeInStatusTableWithNoRowTests
{
    private const string Heading = "\n## Time in status\n\n";

    private const string NoTimeAtAll = "No issue of this sprint spent time in a status that is not done.\n";

    private const string NoTimeOnceLeftOut = "No issue of this sprint has time in a status that is not done once brief visits are left out (adjusted: see the footer).\n";

    [Test]
    public async Task A_sprint_whose_every_row_was_left_out_does_not_say_that_no_issue_spent_time_in_a_status()
    {
        // Five people's issues were each created on Wednesday 7 January at 10:00, moved to Development twenty
        // seconds later and done twenty seconds after that. New and Development are listed.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        var wednesday = Local(2026, 1, 7, 10);
        foreach (var (person, number) in People.Team.Take(5).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", wednesday).AssignedTo(person).InSprints(41)
                .Moved(wednesday.AddSeconds(20), Workflow.Development)
                .Moved(wednesday.AddSeconds(40), Workflow.Done);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5, [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var counted = await pipeline.ReadReportAsync(41);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var leftOut = await pipeline.ReadReportAsync(41);

        // Counted, the table has the two rows. Left out, it has none, and the footer says of each status that
        // five issues were left out of its row: the report must not say that no issue spent time there.
        await Assert.That(counted).Contains(ReportText.TimeInStatusHeader + "| Fake New | 5 | 0.0 | 0.0 |\n| Fake Development | 5 | 0.0 | 0.0 |\n" + ReportText.MethodHeading);
        await Assert.That(leftOut).Contains(Heading + NoTimeOnceLeftOut + ReportText.MethodHeading);
        await Assert.That(leftOut).DoesNotContain(NoTimeAtAll);
        await Assert.That(leftOut).EndsWith(
            "\n- Fake New: 5 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("5")
            + "\n- Fake Development: 5 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("5")
            + "\n" + ReportText.CyclesThatStartWithBriefVisit("5"));
    }

    [Test]
    [Arguments(false, null, NoTimeAtAll)]
    [Arguments(true, null, NoTimeAtAll)]
    [Arguments(true, "count", NoTimeAtAll)]
    [Arguments(true, "exclude", NoTimeOnceLeftOut)]
    public async Task A_sprint_in_which_no_issue_spent_time_in_a_status_says_so_and_under_exclude_says_what_it_says_of_every_table_with_no_row(bool listed, string? timeInStatus, string sentence)
    {
        // Five people's issues were done on the Friday before the sprint, straight from New, and are in the
        // sprint: it has a team, and nobody spent time in a status that is not done.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Take(5).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 2, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 2, 10), Workflow.Done);
        }

        if (listed)
        {
            pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5, [Workflow.Development.Id] = 5 };
        }

        if (timeInStatus is not null)
        {
            pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = timeInStatus };
        }

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(Heading + sentence + ReportText.MethodHeading);
    }
}
