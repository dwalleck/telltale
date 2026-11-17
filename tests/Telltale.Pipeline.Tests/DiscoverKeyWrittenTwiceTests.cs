using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>discover</c> and a key written twice (owner decision D108): it prints no part of such a key, in any
/// setting and at any depth of the configuration file, because what was typed as a key could be a secret
/// pasted into the wrong place. It says that a key is written twice, and under which setting when that
/// setting's name is one the tool knows. A part of <c>briefVisitTreatment</c> written twice directly under
/// that setting gets <c>run</c>'s message instead (owner decision D105;
/// <see cref="DiscoverBriefVisitTreatmentTests"/>).
/// </summary>
public class DiscoverKeyWrittenTwiceTests
{
    /// <summary>Stands for a secret pasted into the wrong place as a key: nothing printed may hold any part of it.</summary>
    private const string Pasted = "synthetic-pasted-token";

    private const string Twice = $"\"{Pasted}\": 1, \"{Pasted}\": 2";

    [Test]
    [Arguments($$"""{ "briefVisitThresholds": { {{Twice}} } }""", " under briefVisitThresholds")]
    [Arguments($$"""{ "statusRoles": { {{Twice}} } }""", " under statusRoles")]
    [Arguments($$"""{ "minimumGroupSize": { {{Twice}} } }""", " under minimumGroupSize")]
    [Arguments($$"""{ "jira": { "briefVisitTreatment": { {{Twice}} } } }""", " under jira")]
    [Arguments($$"""{ "holidays": [ "2026-01-19", { "deeper": [ { {{Twice}} } ] } ] }""", " under holidays")]
    [Arguments($$"""{ "briefVisitTreatment": { "timeInStatus": { {{Twice}} } } }""", null)]
    [Arguments($$"""{ {{Twice}} }""", "")]
    [Arguments($$"""{ "{{Pasted}}": { {{Twice}} } }""", "")]
    [Arguments($$"""{ "{{Pasted}}": { "briefVisitTreatment": { "timeInStatus": "exclude", "timeInStatus": "exclude" } } }""", "")]
    [Arguments($$"""[ { {{Twice}} } ]""", "")]
    [Arguments($$"""[ { "briefVisitTreatment": { {{Twice}} } } ]""", "")]
    public async Task Discover_says_that_a_key_is_written_twice_and_under_which_known_setting_and_prints_no_part_of_the_key(string file, string? where)
    {
        // A known setting, a misplaced briefVisitTreatment object, a list inside a setting, the top level, a
        // setting the tool does not know, and a file whose root is a list. Under a part of the real
        // briefVisitTreatment the part's own message speaks, as run's would (D105): the row with no place.
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, file);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        var refusal = where is null
            ? $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: "
                + "briefVisitTreatment.timeInStatus must be written as \"count\" or \"exclude\"."
            : $"The configuration file {pipeline.ConfigPath} cannot be read (a key is written twice{where}), so it was left as it is. Correct it or move it away, then run discover again.";
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(refusal + "\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(file);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task With_a_key_written_twice_in_another_setting_and_a_fault_in_the_treatment_discover_reports_the_treatment()
    {
        // Owner decision D104: with two faults discover reports the one it finds first, and each message is
        // true of the file. Here that is the treatment's, in run's words; run itself stops at the other.
        await using var pipeline = new PipelineHarness();
        var file = $$"""{ "statusRoles": { {{Twice}} }, "briefVisitTreatment": { "{{Pasted}}": "exclude" } }""";
        await File.WriteAllTextAsync(pipeline.ConfigPath, file);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: "
            + "briefVisitTreatment has a part the tool does not know. The parts are timeInStatus and cycleTime.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(file);
    }

    [Test]
    public async Task A_key_written_twice_with_an_escape_in_one_spelling_is_still_one_key_written_twice()
    {
        // "synthetic-pasted-token" spells the same key: the reader of the file sees one key twice.
        await using var pipeline = new PipelineHarness();
        var file = $$"""{ "statusRoles": { "{{Pasted}}": 1, "s{{Pasted[1..]}}": 2 } }""";
        await File.WriteAllTextAsync(pipeline.ConfigPath, file);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} cannot be read (a key is written twice under statusRoles), so it was left as it is. Correct it or move it away, then run discover again.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(file);
    }
}
