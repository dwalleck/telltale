using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>sync</c>: fetch what changed in Jira since the last sync or backfill.</summary>
internal static class SyncCommand
{
    public static Command Create(AppServices services) => PipelineCommand.Create(
        services,
        "sync",
        "Fetch what changed in Jira since the last sync or backfill.",
        fetches: true,
        (pipeline, _, cancellationToken) => pipeline.SyncAsync(cancellationToken));
}
