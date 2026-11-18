using System.Text;
using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>discover</c> reads the file's bytes as <c>run</c> does, so a file that is not UTF-8, or not JSON at all,
/// is refused by both, before any call to Jira, with a message that is true of the file and repeats nothing
/// typed in it (owner decisions D43, D108, D110).
/// </summary>
public class DiscoverReadsTheFileAsRunDoesTests
{
    /// <summary>Stands for a secret pasted into the wrong place as a key: nothing printed may hold any part of it.</summary>
    private const string Pasted = "synthetic-pasted";

    /// <summary>A key with one letter in Windows-1252 (é as the single byte E9), as a file saved in that encoding has it.</summary>
    private static byte[] Windows1252Key(string key) => [.. Encoding.ASCII.GetBytes($"\"{key}"), 0xE9, .. Encoding.ASCII.GetBytes("\"")];

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    /// <summary>A file to try, named for the test runner by what is wrong with it.</summary>
    public sealed record Case(string Name, byte[] File)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<Func<Case>> KeysThatAreNotUtf8()
    {
        yield return () => new("one key", [.. Ascii("{ \"statusRoles\": { "), .. Windows1252Key(Pasted), .. Ascii(": \"work\" } }")]);
        yield return () => new("two such keys in one object", [.. Ascii("{ \"statusRoles\": { "), .. Windows1252Key(Pasted), .. Ascii(": \"work\", "), .. Windows1252Key(Pasted + "-other"), .. Ascii(": \"done\" } }")]);
    }

    [Test]
    [MethodDataSource(nameof(KeysThatAreNotUtf8))]
    public async Task A_key_that_is_not_utf8_is_refused_by_discover_as_run_refuses_it_before_any_call_to_jira(Case @case)
    {
        // Read as text, the byte becomes a replacement character: discover then called Jira, repeated the key,
        // or, with two such keys, said that a key was written twice, which is not true of the file.
        var (discover, run) = await BothAsync(@case.File);

        var refusal = $"The configuration file {discover.Path} is not valid: a key holds bytes that are not UTF-8, so it is not text. Save the file as UTF-8.";
        await Assert.That(discover.Error).IsEqualTo($"discover did not write {discover.Path}, because run would refuse it: {refusal}\n");
        await Assert.That(run.Error).IsEqualTo(refusal.Replace(discover.Path, run.Path, StringComparison.Ordinal) + "\n");
        await AssertDiscoverChangedNothingAsync(discover, @case.File);
    }

    public static IEnumerable<Func<Case>> FilesThatAreNotJson()
    {
        const string Json = """{ "timeZone": "America/Chicago" }""";

        // PowerShell 5.1's Out-File writes UTF-16 with a byte order mark by default.
        yield return () => new("UTF-16", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Json)]);
        yield return () => new("two UTF-8 byte order marks", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Json)]);
    }

    [Test]
    [MethodDataSource(nameof(FilesThatAreNotJson))]
    public async Task A_file_run_reads_as_not_json_is_refused_by_discover_with_the_same_line_and_position(Case @case)
    {
        var (discover, run) = await BothAsync(@case.File);

        const string Fault = "it is not JSON the tool can read, and the fault is at line 1, position 1";
        await Assert.That(run.Error).IsEqualTo($"The configuration file {run.Path} is not valid: {Fault}.\n");
        await Assert.That(discover.Error).IsEqualTo(
            $"The configuration file {discover.Path} cannot be read ({Fault}), so it was left as it is. Correct it or move it away, then run discover again.\n");
        await AssertDiscoverChangedNothingAsync(discover, @case.File);
    }

    [Test]
    public async Task The_position_of_a_fault_counts_characters_as_an_editor_does_not_bytes()
    {
        // "é" and "€" are two and three bytes in UTF-8: counted in bytes, the position would be three past the
        // character an editor shows at the fault. The fault is the bare word on the second line.
        const string File = "{\n  \"timeZone\": \"Amérique/€\", oops }";
        var expected = File.Split('\n')[1].IndexOf("oops", StringComparison.Ordinal) + 1;
        var (discover, run) = await BothAsync(Encoding.UTF8.GetBytes(File));

        var fault = $"it is not JSON the tool can read, and the fault is at line 2, position {expected}";
        await Assert.That(run.Error).IsEqualTo($"The configuration file {run.Path} is not valid: {fault}.\n");
        await Assert.That(discover.Error).IsEqualTo(
            $"The configuration file {discover.Path} cannot be read ({fault}), so it was left as it is. Correct it or move it away, then run discover again.\n");
    }

    [Test]
    [Arguments("""{ "minimumGroupSize": { "team": "\uD800", "assigneesBehindAFigure": 3 } }""")]
    [Arguments("""{ "minimumGroupSize": "\uD800" }""")]
    [Arguments("""{ "briefVisitThresholds": { "600004": "\uD800" } }""")]
    [Arguments("""{ "briefVisitThresholds": "\uD800" }""")]
    public async Task A_value_no_text_can_hold_in_a_setting_discover_checks_is_refused_as_run_refuses_it_without_a_stack_trace(string file)
    {
        // "\uD800" is an escape for half of a character pair. Written back out from what was parsed, it ended
        // discover with a stack trace; read as typed, it reaches the setting's reader as it reaches run's.
        await Assert.That(file).Contains("\\uD800");
        var (discover, run) = await BothAsync(Encoding.UTF8.GetBytes(file));

        await Assert.That(run.Exit).IsEqualTo(1);
        await Assert.That(run.Error).StartsWith($"The configuration file {run.Path} is not valid: ");
        await Assert.That(discover.Error).IsEqualTo(
            $"discover did not write {discover.Path}, because run would refuse it: " + run.Error.Replace(run.Path, discover.Path, StringComparison.Ordinal));
        await AssertDiscoverChangedNothingAsync(discover, Encoding.UTF8.GetBytes(file));
    }

    [Test]
    public async Task A_value_that_is_not_utf8_is_refused_by_discover_before_any_call_to_jira()
    {
        // Read as text, the value would be written back with a replacement character in place of what was typed.
        byte[] file = [.. Ascii("{ \"timeZone\": \"America/Chicag"), 0xF6, .. Ascii("\" }")];
        await using var pipeline = new PipelineHarness();
        await File.WriteAllBytesAsync(pipeline.ConfigPath, file);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} cannot be read (it holds bytes that are not UTF-8; save the file as UTF-8), so it was left as it is. Correct it or move it away, then run discover again.\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(await File.ReadAllBytesAsync(pipeline.ConfigPath)).IsEquivalentTo(file);
    }

    private sealed record Outcome(string Path, string Out, string Error, int Exit, int JiraRequests, byte[] FileAfter);

    /// <summary>The same bytes as the configuration file, once for discover and once for run.</summary>
    private static async Task<(Outcome Discover, Outcome Run)> BothAsync(byte[] file)
    {
        var discover = new PipelineHarness();
        var run = new PipelineHarness();
        await File.WriteAllBytesAsync(discover.ConfigPath, file);
        await File.WriteAllBytesAsync(run.ConfigPath, file);

        var discoverExit = await discover.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);
        var runExit = await run.RunWithFilesAsTheyAreAsync("run");

        Outcome Of(PipelineHarness pipeline, int exit) => new(pipeline.ConfigPath, pipeline.Out.ToString(), pipeline.Error.ToString().ReplaceLineEndings("\n"),
            exit, pipeline.Jira.Handler.Requests.Count, File.ReadAllBytes(pipeline.ConfigPath));
        var outcomes = (Of(discover, discoverExit), Of(run, runExit));
        await discover.DisposeAsync();
        await run.DisposeAsync();
        return outcomes;
    }

    private static async Task AssertDiscoverChangedNothingAsync(Outcome discover, byte[] file)
    {
        await Assert.That(discover.Exit).IsEqualTo(1);
        await Assert.That(discover.JiraRequests).IsEqualTo(0);
        await Assert.That(discover.Out).IsEmpty();
        await Assert.That(discover.Error).DoesNotContain(Pasted);
        await Assert.That(discover.FileAfter).IsEquivalentTo(file);
    }
}
