using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A configuration file that is not valid JSON (owner decision D110): every command that reads it, and
/// <c>discover</c>, refuses it with the line and the position of the fault and no text from the file. The
/// runtime's own message for such a file can quote the file from the fault to its end, a key written twice
/// and every value included, so it is never passed on. Lines and positions are counted from one, the
/// position in bytes. The position is where the reader stopped: the comma after a misspelled "tru" (18),
/// the second letter of text that is no literal, the digit of a status ID written without quotes, the
/// end of a file cut short, the first character after the closing brace, the hyphen of a date without quotes.
/// </summary>
public class FileThatIsNotJsonTests
{
    /// <summary>Stands for a secret pasted into the wrong place: nothing printed may hold any part of it.</summary>
    private const string Pasted = "synthetic-pasted-token";

    /// <summary>A misspelled <c>true</c> early in the file, and after it a made-up key written twice.</summary>
    private const string MisspelledLiteralBeforeAKeyWrittenTwice =
        $$"""{ "timeZone": tru, "statusRoles": { "{{Pasted}}": 1, "{{Pasted}}": 2 } }""";

    /// <summary>Every command that loads the configuration, apart from <c>discover</c>.</summary>
    public static IEnumerable<string> EveryCommand() => ["backfill", "sync", "derive", "report", "run", "query"];

    /// <summary>Files with one fault each, and where the fault is: the line and the position, from one.</summary>
    public static IEnumerable<(string File, int Line, int Position)> Faults() =>
    [
        (MisspelledLiteralBeforeAKeyWrittenTwice, 1, 18),
        ("this is not JSON", 1, 2),
        ("", 1, 1),
        ($$"""{ "briefVisitThresholds": { 600004: 5 }, "{{Pasted}}": 1 }""", 1, 29),
        ($$"""{ "timeZone": "{{Pasted}}" """, 1, 40),
        ($$"""{ "briefVisitTreatment": { "{{Pasted}}": 1 } } {{Pasted}}""", 1, 60),
        ("{\n  \"timeZone\": \"Etc/UTC\",\n  \"holidays\": [ 2026-01-19 ],\n  \"" + Pasted + "\": 1\n}\n", 3, 21),
    ];

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task Every_command_refuses_a_misspelled_literal_with_where_it_is_and_no_text_from_the_file(string command)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.RawConfig = MisspelledLiteralBeforeAKeyWrittenTwice;

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedByACommandAsync(pipeline, exit, line: 1, position: 18);
    }

    [Test]
    public async Task Discover_refuses_a_misspelled_literal_with_where_it_is_and_no_text_from_the_file()
    {
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, MisspelledLiteralBeforeAKeyWrittenTwice);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await AssertRefusedByDiscoverAsync(pipeline, exit, MisspelledLiteralBeforeAKeyWrittenTwice, line: 1, position: 18);
    }

    [Test]
    [MethodDataSource(nameof(Faults))]
    public async Task Run_says_where_the_fault_is_for_every_kind_of_fault(string file, int line, int position)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.RawConfig = file;

        var exit = await RunAsync(pipeline, "run");

        await AssertRefusedByACommandAsync(pipeline, exit, line, position);
    }

    [Test]
    [MethodDataSource(nameof(Faults))]
    public async Task Discover_says_where_the_fault_is_for_every_kind_of_fault(string file, int line, int position)
    {
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, file);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await AssertRefusedByDiscoverAsync(pipeline, exit, file, line, position);
    }

    /// <summary>Runs <paramref name="command"/> with what it cannot run without: a sprint for <c>report</c> and <c>run</c>, a statement for <c>query</c>.</summary>
    private static Task<int> RunAsync(PipelineHarness pipeline, string command) => pipeline.RunAsync(command switch
    {
        "report" or "run" => [command, "--sprint", "41"],
        "query" => [command, "SELECT sprint_id FROM delivered_per_sprint;"],
        _ => [command],
    });

    private static string WhereTheFaultIs(int line, int position) =>
        $"it is not JSON the tool can read, and the fault is at line {line}, position {position}";

    /// <summary>The command failed and said only where the fault is, no request went to Jira, and no database was made.</summary>
    private static async Task AssertRefusedByACommandAsync(PipelineHarness pipeline, int exit, int line, int position)
    {
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The configuration file {pipeline.ConfigPath} is not valid: {WhereTheFaultIs(line, position)}.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }

    /// <summary><c>discover</c> failed and said only where the fault is, no request went to Jira, and the file is as it was, with nothing beside it.</summary>
    private static async Task AssertRefusedByDiscoverAsync(PipelineHarness pipeline, int exit, string file, int line, int position)
    {
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} cannot be read ({WhereTheFaultIs(line, position)}), so it was left as it is. Correct it or move it away, then run discover again.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(file);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }
}
