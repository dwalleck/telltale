using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>run</c>: sync from Jira, derive, and write one sprint's report.</summary>
internal static class RunCommand
{
    public static Command Create(AppServices services)
    {
        var sprint = SprintOption.Create();

        var command = new Command("run", "Sync from Jira, rebuild the derived data and write a sprint's report.") { sprint };
        command.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var pipeline = await PipelineSteps.StartAsync(services, needsJira: true, cancellationToken);
            await pipeline.SyncAsync(cancellationToken);
            await pipeline.DeriveAsync(cancellationToken);
            await pipeline.ReportAsync(parse.GetValue(sprint), cancellationToken);
        }));

        return command;
    }
}
