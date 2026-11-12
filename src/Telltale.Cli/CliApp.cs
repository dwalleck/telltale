using System.CommandLine;
using Telltale.Cli.Commands;
using Telltale.Core;

namespace Telltale.Cli;

/// <summary>The command line, built fresh for every invocation from the services it is given.</summary>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args, AppServices services, CancellationToken cancellationToken = default)
    {
        var invocation = new InvocationConfiguration { Output = services.Out, Error = services.Error };
        return await Build(services).Parse(args).InvokeAsync(invocation, cancellationToken);
    }

    /// <summary>The command tree. A new command is a class in <c>Commands/</c> and a line here.</summary>
    public static RootCommand Build(AppServices services) =>
        new("Telltale: delivery metrics for the team's work, never for individuals.")
        {
            RunCommand.Create(services),
        };

    /// <summary>
    /// Wraps a command's work so a failure the user can act on prints its message alone and exits with 1.
    /// Anything else is a bug and keeps its stack trace.
    /// </summary>
    internal static async Task<int> GuardAsync(AppServices services, Func<Task> work)
    {
        try
        {
            await work();
            return 0;
        }
        catch (UserFacingException exception)
        {
            await services.Error.WriteLineAsync(exception.Message);
            return 1;
        }
    }
}
