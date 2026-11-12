using System.CommandLine;

namespace Telltale.Cli.Commands;

/// <summary>The <c>--sprint</c> option of the commands that write a report.</summary>
internal static class SprintOption
{
    /// <summary>A new option instance for one command. Its value is null when the option is left out.</summary>
    public static Option<long?> Create() =>
        new("--sprint") { Description = "The ID of the sprint to report on. Without it, the most recently completed sprint." };
}
