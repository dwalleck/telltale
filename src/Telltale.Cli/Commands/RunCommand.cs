using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>run</c>: sync from Jira, derive, and write one sprint's report.</summary>
internal static class RunCommand
{
    public static Command Create(AppServices services)
    {
        var sprint = new Option<long>("--sprint") { Description = "The ID of the sprint to report on.", Required = true };

        var command = new Command("run", "Sync from Jira, rebuild the derived data and write the report for a sprint.") { sprint };
        command.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var pipeline = await PipelineSteps.StartAsync(services, cancellationToken);
            await pipeline.SyncAsync(cancellationToken);
            await pipeline.DeriveAsync(cancellationToken);
            await pipeline.ReportAsync(parse.GetValue(sprint), cancellationToken);
        }));

        return command;
    }
}
