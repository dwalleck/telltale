using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// JSON can spell half of a character pair as a key (<c>"\uD800"</c>), which no text can hold. A configuration
/// file with such a key, anywhere in it, is refused by every command with one plain line that names the file
/// and repeats nothing written in it, before anything is fetched or stored; <c>discover</c> says what
/// <c>run</c> says and leaves the file as it is.
/// </summary>
public class KeyThatIsNotTextTests
{
    private const string Refusal = "a key is written with an escape for half of a character pair, which no text can hold.";

    /// <summary>Every command that loads the configuration, apart from <c>discover</c>.</summary>
    public static IEnumerable<string> EveryCommand() => ["backfill", "sync", "derive", "report", "run", "query"];

    /// <summary>Files with such a key at the top level, under each kind of setting, inside a list, and at depth.</summary>
    public static IEnumerable<string> Places() =>
    [
        """{ "\uD800": 1 }""",
        """{ "jira": { "\uD800": 1 } }""",
        """{ "statusRoles": { "\uD800": "work" } }""",
        """{ "minimumGroupSize": { "\uD800": 1 } }""",
        """{ "briefVisitThresholds": { "\uD800": 5 } }""",
        """{ "briefVisitTreatment": { "\uD800": "exclude" } }""",
        """{ "holidays": [ { "deeper": { "\uDFFF": 1 } } ] }""",
        """{ "synthetic-pasted-token": { "synthetic-pasted-\uD800": 1 } }""",
        """[ { "\uD800": 1 } ]""",
    ];

    public static IEnumerable<(string Command, string File)> EveryCommandAndPlace() =>
        from command in EveryCommand()
        from file in Places()
        select (command, file);

    [Test]
    [MethodDataSource(nameof(EveryCommandAndPlace))]
    public async Task Every_command_refuses_the_file_and_repeats_nothing_written_in_it(string command, string file)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.RawConfig = file;

        var exit = await pipeline.RunAsync(command switch
        {
            "report" or "run" => [command, "--sprint", "41"],
            "query" => [command, "SELECT sprint_id FROM delivered_per_sprint;"],
            _ => [command],
        });

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The configuration file {pipeline.ConfigPath} is not valid: {Refusal}\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(Places))]
    public async Task Discover_says_what_run_says_and_leaves_the_file_as_it_is(string file)
    {
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, file);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {Refusal}\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(file);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task Discover_refuses_such_a_key_written_twice_as_a_key_that_is_not_text()
    {
        // Two faults in one key: the one that stops every command is reported, and no part of the key.
        await using var pipeline = new PipelineHarness();
        const string File_ = """{ "statusRoles": { "synthetic-pasted-\uD800": 1, "synthetic-pasted-\uD800": 2 } }""";
        await File.WriteAllTextAsync(pipeline.ConfigPath, File_);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {Refusal}\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(File_);
    }
}
