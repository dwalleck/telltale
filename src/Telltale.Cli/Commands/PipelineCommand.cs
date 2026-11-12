using System.CommandLine;
using Telltale.Pipeline;

namespace Telltale.Cli.Commands;

/// <summary>
/// The shape every command over the store has: load the configuration, take the store for the length of
/// the command, do the steps, and turn a failure the user can act on into one plain line.
/// </summary>
internal static class PipelineCommand
{
    /// <param name="fetches">Whether the command calls Jira (see <see cref="PipelineSession.StartAsync"/>).</param>
    /// <param name="steps">The command's work, given the parsed command line for its option values.</param>
    public static Command Create(
        AppServices services,
        string name,
        string description,
        bool fetches,
        Func<PipelineSteps, ParseResult, CancellationToken, Task> steps,
        params Symbol[] optionsAndArguments)
    {
        var command = new Command(name, description);
        foreach (var symbol in optionsAndArguments)
        {
            switch (symbol)
            {
                case Option option:
                    command.Add(option);
                    break;
                case Argument argument:
                    command.Add(argument);
                    break;
                default:
                    throw new ArgumentException($"{symbol.Name} is neither an option nor an argument.", nameof(optionsAndArguments));
            }
        }

        command.SetAction((parse, cancellationToken) => CliApp.GuardAsync(services, async () =>
        {
            using var session = await PipelineSession.StartAsync(services, needsJira: fetches, cancellationToken);
            await steps(session.Steps, parse, cancellationToken);
        }));

        return command;
    }
}
