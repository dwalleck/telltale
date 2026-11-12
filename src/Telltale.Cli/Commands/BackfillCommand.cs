using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>backfill</c>: fetch the board's history from the configured start date.</summary>
internal static class BackfillCommand
{
    public static Command Create(AppServices services) => PipelineCommand.Create(
        services,
        "backfill",
        "Fetch everything from Jira since the configured backfill start date.",
        fetches: true,
        (pipeline, _, cancellationToken) => pipeline.BackfillAsync(cancellationToken));
}
