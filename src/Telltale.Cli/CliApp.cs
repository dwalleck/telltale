using System.CommandLine;
using System.Data.Common;
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
            SecretsCommand.Create(services),
        };

    /// <summary>The conventional exit code of a run ended by Ctrl+C (128 + SIGINT).</summary>
    public const int CancelledExitCode = 130;

    /// <summary>
    /// Wraps a command's work so a failure the user can act on, a problem with a file or the database, or a
    /// cancellation prints one plain line. Anything else is a bug and keeps its stack trace.
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
        catch (OperationCanceledException)
        {
            // The user's Ctrl+C. A request Jira never answered is reported by the collector, not here.
            await services.Error.WriteLineAsync("Cancelled.");
            return CancelledExitCode;
        }
        catch (DbException exception)
        {
            // A locked, unreadable or damaged database file. SQLite's message names the condition, never row data.
            await services.Error.WriteLineAsync($"The database {services.DatabasePath} could not be used: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A report open in another program, a configuration file that cannot be read, a full disk.
            await services.Error.WriteLineAsync($"A file could not be read or written: {exception.Message}");
            return 1;
        }
    }
}
