using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>report</c>: write one sprint's report from the derived data already in the store.</summary>
internal static class ReportCommand
{
    public static Command Create(AppServices services)
    {
        var sprint = SprintOption.Create();
        return PipelineCommand.Create(
            services,
            "report",
            "Write a sprint's report from the derived data in the store. Jira is not called.",
            fetches: false,
            (pipeline, parse, cancellationToken) => pipeline.ReportAsync(parse.GetValue(sprint), needsSprint: true, cancellationToken),
            sprint);
    }
}
