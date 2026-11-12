using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>report</c>: write one sprint's report from the derived data already in the store.</summary>
internal static class ReportCommand
{
    public static Command Create(AppServices services)
    {
        var sprint = new Option<long>("--sprint") { Description = "The ID of the sprint to report on.", Required = true };

        var command = new Command("report", "Write the report for a sprint from the derived data in the store. Jira is not called.") { sprint };
        command.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            var pipeline = await PipelineSteps.StartAsync(services, needsJira: false, cancellationToken);
            await pipeline.ReportAsync(parse.GetValue(sprint), cancellationToken);
        }));

        return command;
    }
}
