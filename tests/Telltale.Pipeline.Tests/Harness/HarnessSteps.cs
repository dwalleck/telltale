using System.Text.Json.Nodes;
using Telltale.Core.Configuration;
using Telltale.Core.Storage;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Steps more than one test class takes with a <see cref="PipelineHarness"/>: reading what <c>query</c>
/// prints, and reading or editing the configuration file on disk as <c>discover</c> left it. A test class
/// brings them in with <c>using static</c>.
/// </summary>
public static class HarnessSteps
{
    /// <summary>
    /// A record of derived data built under the default numbers of the minimum group size with no status listed
    /// for brief visits, for a test that renders a report directly and is about neither: the report takes
    /// nothing else from the record.
    /// </summary>
    public static DerivedSource RecordUnderTheDefaults { get; } = new(
        "synthetic-digest",
        TimeZone: "UTC",
        Holidays: "",
        BaselineSprintIds: "[]",
        StatusRoles: "",
        AbandonedResolutionIds: "",
        MinimumGroupSize.Default.Team,
        MinimumGroupSize.Default.AssigneesBehindAFigure,
        DerivedSource.NoBriefVisitThresholds);

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

    /// <summary>
    /// Everything <c>query</c> may read, as it prints it: each metric view whole, one after another in the
    /// order of their names. The views are found in the store and not listed here, so a view added later is
    /// read too. Two stores that give the same text cannot be told apart by any query.
    /// </summary>
    public static async Task<string> QueryEveryMetricViewAsync(PipelineHarness pipeline)
    {
        var views = await pipeline.QueryAsync<string>("SELECT name FROM derived_object WHERE kind = 'view' ORDER BY name;");
        if (views.Count == 0)
        {
            throw new InvalidOperationException("The store has no metric view: derive has not run.");
        }

        return await QueryAsync(pipeline, [.. views.Select(view => $"SELECT * FROM {view};")]);
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
