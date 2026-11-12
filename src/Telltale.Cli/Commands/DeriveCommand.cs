using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>derive</c>: rebuild the derived data and the metric views from what is already in the store.</summary>
internal static class DeriveCommand
{
    public static Command Create(AppServices services)
    {
        var command = new Command("derive", "Drop and rebuild all derived data from the raw data in the store. Jira is not called.");
        command.SetAction((_, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var pipeline = await PipelineSteps.StartAsync(services, needsJira: false, cancellationToken);
            await pipeline.DeriveAsync(cancellationToken);
        }));

        return command;
    }
}
