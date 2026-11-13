using System.Text.Json.Nodes;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Steps more than one test class takes with a <see cref="PipelineHarness"/>: reading what <c>query</c>
/// prints, and reading or editing the configuration file on disk as <c>discover</c> left it. A test class
/// brings them in with <c>using static</c>.
/// </summary>
public static class HarnessSteps
{
    /// <summary>
    /// What the <c>query</c> command prints for each statement in turn, with line endings normalised to
    /// <c>\n</c>. Standard output is cleared first, and a statement that is refused fails the test.
    /// </summary>
    public static async Task<string> QueryAsync(PipelineHarness pipeline, params string[] statements)
    {
        pipeline.Out.GetStringBuilder().Clear();
        await pipeline.RunAllAsync([.. statements.Select(sql => new[] { "query", sql })]);
        return pipeline.Out.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>Changes the file on disk as the owner would in an editor.</summary>
    public static async Task EditWrittenConfigAsync(PipelineHarness pipeline, Action<JsonNode> edit)
    {
        var config = await ReadWrittenConfigAsync(pipeline);
        edit(config);
        await File.WriteAllTextAsync(pipeline.ConfigPath, config.ToJsonString());
    }

    /// <summary>The configuration file as it is on disk.</summary>
    public static async Task<JsonNode> ReadWrittenConfigAsync(PipelineHarness pipeline) =>
        JsonNode.Parse(await File.ReadAllTextAsync(pipeline.ConfigPath))!;
}
