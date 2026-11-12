using System.CommandLine;
using Telltale.Core;

namespace Telltale.Cli.Commands;

/// <summary><c>query</c>: run the owner's SQL over the metric views and print the rows (owner decision D48).</summary>
internal static class QueryCommand
{
    public static Command Create(AppServices services)
    {
        var sql = new Argument<string?>("sql") { Description = "The SQL to run: one SELECT over the metric views.", Arity = ArgumentArity.ZeroOrOne };
        var file = new Option<string?>("--file") { Description = "A file that holds the SQL, in place of the argument." };
        return PipelineCommand.Create(
            services,
            "query",
            "Run a SQL query over the metric views and print its rows as a markdown table. Jira is not called. "
            + "The query reads the metric views and nothing else in the store, so the suppression rule holds for it. "
            + "Opening the database file in another program bypasses the suppression rule: that program reads every table.",
            fetches: false,
            async (pipeline, parse, cancellationToken) =>
                await pipeline.QueryAsync(await ReadSqlAsync(parse.GetValue(sql), parse.GetValue(file), cancellationToken), cancellationToken),
            sql,
            file);
    }

    private static async Task<string> ReadSqlAsync(string? sql, string? file, CancellationToken cancellationToken)
    {
        // One rule says whether each was given: a blank is not.
        var (sqlGiven, fileGiven) = (!string.IsNullOrWhiteSpace(sql), !string.IsNullOrWhiteSpace(file));
        if (sqlGiven == fileGiven)
        {
            throw new UserFacingException("Give the SQL to run, either as the argument or in a file named with --file, and not both.");
        }

        if (!fileGiven)
        {
            return sql!;
        }

        return File.Exists(file)
            ? await File.ReadAllTextAsync(file, cancellationToken)
            : throw new UserFacingException($"The file {file} does not exist.");
    }
}
