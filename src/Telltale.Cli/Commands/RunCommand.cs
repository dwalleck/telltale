using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>run</c>: sync from Jira, derive, and write one sprint's report.</summary>
internal static class RunCommand
{
    public static Command Create(AppServices services)
    {
        var sprint = SprintOption.Create();
        return PipelineCommand.Create(
            services,
            "run",
            "Sync from Jira, rebuild the derived data and write a sprint's report.",
            fetches: true,
            async (pipeline, parse, cancellationToken) =>
            {
                await pipeline.SyncAsync(cancellationToken);
                await pipeline.DeriveAsync(cancellationToken);
                await pipeline.ReportAsync(parse.GetValue(sprint), cancellationToken);
            },
            sprint);
    }
}
