using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>derive</c>: rebuild the derived data and the metric views from what is already in the store.</summary>
internal static class DeriveCommand
{
    public static Command Create(AppServices services) => PipelineCommand.Create(
        services,
        "derive",
        "Drop and rebuild all derived data from the raw data in the store. Jira is not called.",
        fetches: false,
        (pipeline, _, cancellationToken) => pipeline.DeriveAsync(cancellationToken));
}
