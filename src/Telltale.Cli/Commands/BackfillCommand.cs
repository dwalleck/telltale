using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>backfill</c>: fetch the board's history from the configured start date.</summary>
internal static class BackfillCommand
{
    public static Command Create(AppServices services)
    {
        var command = new Command("backfill", "Fetch everything from Jira since the configured backfill start date.");
        command.SetAction((_, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var pipeline = await PipelineSteps.StartAsync(services, needsJira: true, cancellationToken);
            await pipeline.BackfillAsync(cancellationToken);
        }));

        return command;
    }
}
