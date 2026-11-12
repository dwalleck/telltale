using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>sync</c>: fetch what changed in Jira since the last sync or backfill.</summary>
internal static class SyncCommand
{
    public static Command Create(AppServices services)
    {
        var command = new Command("sync", "Fetch what changed in Jira since the last sync or backfill.");
        command.SetAction((_, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var pipeline = await PipelineSteps.StartAsync(services, needsJira: true, cancellationToken);
            await pipeline.SyncAsync(cancellationToken);
        }));

        return command;
    }
}
