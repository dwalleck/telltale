using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>briefVisitTreatment</c> written while no status is listed in <c>briefVisitThresholds</c> (user story 3
/// of the brief visit treatment spec; owner decision D83): the setting is optional, an absent part means
/// <c>count</c>, and <c>count</c> changes no figure. So a file that has the setting with nothing left out,
/// and no status listed, is accepted, and gives the report and the store that a file without the setting
/// gives: nothing says that the setting is there.
/// </summary>
public class TreatmentWithNoStatusListedTests
{
    [Test]
    [Arguments("{}", false)]
    [Arguments("""{ "timeInStatus": "count" }""", false)]
    [Arguments("""{ "cycleTime": "count", "timeInStatus": "count" }""", false)]
    [Arguments("""{ "cycleTime": "count", "timeInStatus": "count" }""", true)]
    public async Task The_setting_with_nothing_left_out_and_no_status_listed_gives_the_report_and_the_store_that_no_setting_gives(string treatment, bool withAnEmptyList)
    {
        // The same Jira twice: no setting, and the treatment written. Four people's issues passed through
        // Development in twenty seconds each, so there would be something to count and to leave out.
        await using var absent = new PipelineHarness();
        await using var written = new PipelineHarness();
        foreach (var pipeline in new[] { absent, written })
        {
            pipeline.Jira.AddSprint41();
            pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
            foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
            {
                Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person);
            }
        }

        written.Config["briefVisitTreatment"] = JsonNode.Parse(treatment);
        if (withAnEmptyList)
        {
            written.Config["briefVisitThresholds"] = new JsonObject();
        }

        foreach (var pipeline in new[] { absent, written })
        {
            await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        }

        // Byte for byte, as both were written at the same fixed time; and every table and view of the store
        // holds the same, the record of the settings included.
        await Assert.That(await written.ReadReportBytesAsync(41)).IsEqualTo(await absent.ReadReportBytesAsync(41));
        await Assert.That(await absent.ReadReportAsync(41)).EndsWith("\n- Issues delivered between sprints, in the whole store: suppressed\n");
        await Assert.That(await written.ReadStoreContentAsync()).IsEqualTo(await absent.ReadStoreContentAsync());
        await Assert.That(written.Error.ToString()).IsEqualTo(absent.Error.ToString());
    }
}
