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
            (pipeline, parse, cancellationToken) => pipeline.RunAsync(parse.GetValue(sprint), cancellationToken),
            sprint);
    }
}
