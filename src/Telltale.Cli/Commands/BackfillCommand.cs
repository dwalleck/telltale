using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary><c>backfill</c>: fetch the board's history from the configured start date and rebuild the raw tables from it.</summary>
internal static class BackfillCommand
{
    public static Command Create(AppServices services)
    {
        var allowEmpty = new Option<bool>("--allow-empty")
        {
            Description = "Replace the stored issues or sprints even when Jira returns none. Without it such a backfill is refused, because that usually means a wrong setting or lost access.",
        };
        return PipelineCommand.Create(
            services,
            "backfill",
            "Fetch everything from Jira since the configured backfill start date, replacing what was fetched before.",
            fetches: true,
            (pipeline, parse, cancellationToken) => pipeline.BackfillAsync(parse.GetValue(allowEmpty), cancellationToken),
            allowEmpty);
    }
}
