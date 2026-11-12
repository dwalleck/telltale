using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Data.Sqlite;
using Telltale.Cli;
using Telltale.Core.Secrets;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Everything one pipeline test needs: a fake Jira, a fixed clock, an in-memory secret store, and a
/// temporary directory holding the configuration file, the SQLite database and the reports.
/// Create one per test with <c>await using</c>; nothing is shared between tests.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Fixtures go into <see cref="Jira"/>.</item>
/// <item>Commands run through <see cref="RunAsync"/>, in process, exactly as the executable runs them.</item>
/// <item>Results are read with <see cref="ReadReportAsync"/> (the markdown) and <see cref="QueryAsync{T}"/> (ad hoc SQL against the metric views).</item>
/// <item>Settings are changed by editing <see cref="Config"/>, and secrets by editing <see cref="Secrets"/>, before a run.</item>
/// </list>
/// </remarks>
public sealed class PipelineHarness : IAsyncDisposable
{
    public const string JiraEmail = "tool.owner@example.invalid";
    public const string JiraApiToken = "synthetic-api-token-0001";
    public const string HashingSalt = "synthetic-salt-one";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <param name="now">What the clock reads during the run: the as-of time. Defaults to 3 February 2026, noon, team time.</param>
    public PipelineHarness(DateTimeOffset? now = null)
    {
        Clock = new SkippingClock(now ?? TeamTime.Local(2026, 2, 3, 12));
    }

    public FakeJira Jira { get; } = new();

    public SkippingClock Clock { get; }

    /// <summary>The secret store's contents. Synthetic values for all three secrets are present by default.</summary>
    public Dictionary<SecretName, string> Secrets { get; } = new()
    {
        [SecretName.JiraEmail] = JiraEmail,
        [SecretName.JiraApiToken] = JiraApiToken,
        [SecretName.HashingSalt] = HashingSalt,
    };

    /// <summary>When set, the commands get this secret store in place of the in-memory one over <see cref="Secrets"/>.</summary>
    public ISecretStore? SecretStore { get; set; }

    /// <summary>The keyboard and standard input <c>secrets set</c> reads a value from. Type or pipe the value before the run.</summary>
    public FakeConsoleInput Console { get; } = new();

    /// <summary>The configuration file's content, written to disk before each run. Edit it to change a setting.</summary>
    public JsonObject Config { get; } = new()
    {
        ["jira"] = new JsonObject
        {
            ["siteUrl"] = FakeJira.SiteUrl,
            ["boardId"] = FakeJira.BoardId,
            ["projectKeys"] = new JsonArray(FakeJira.ProjectKey),
            ["sprintFieldId"] = FakeJira.SprintFieldId,
            ["storyPointFieldId"] = FakeJira.StoryPointFieldId,
            ["backfillStart"] = "2025-01-01",
        },
        ["statusRoles"] = new JsonObject(Workflow.BoardOrder.Select(status => KeyValuePair.Create(status.Id, (JsonNode?)status.Role))),
        ["abandonedResolutionIds"] = new JsonArray(),
        ["timeZone"] = TeamTime.ZoneId,
        ["holidays"] = new JsonArray(),
    };

    /// <summary>When set, this text is written as the configuration file in place of <see cref="Config"/>, for files a JSON object cannot express.</summary>
    public string? RawConfig { get; set; }

    /// <summary>The token the commands run under: cancelling it is the user's Ctrl+C.</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>What the commands wrote to standard output.</summary>
    public StringWriter Out { get; } = new();

    /// <summary>What the commands wrote to standard error.</summary>
    public StringWriter Error { get; } = new();

    /// <summary>The temporary directory: configuration file, database files and reports. Deleted on dispose.</summary>
    public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("telltale-").FullName;

    public string ConfigPath => Path.Combine(Directory, "telltale.json");

    public string DatabasePath => Path.Combine(Directory, "store.db");

    public string ReportDirectory => Path.Combine(Directory, "reports");

    /// <summary>Runs one command line (for example <c>"run", "--sprint", "41"</c>) and returns its exit code.</summary>
    public async Task<int> RunAsync(params string[] args)
    {
        await File.WriteAllTextAsync(ConfigPath, RawConfig ?? Config.ToJsonString(Indented));

        var services = new AppServices(
            Jira.Handler,
            Clock,
            SecretStore ?? new InMemorySecretStore(Secrets),
            Out,
            Error,
            ConfigPath,
            DatabasePath,
            ReportDirectory)
        {
            ConsoleInput = Console,
        };

        return await CliApp.RunAsync(args, services, Cancellation.Token);
    }

    /// <summary>
    /// Runs several command lines in order, for example <c>["sync"], ["derive"], ["report"]</c>, and fails the
    /// test at the first one that does not exit with 0, quoting what it wrote to standard error.
    /// </summary>
    public async Task RunAllAsync(params string[][] commandLines)
    {
        foreach (var args in commandLines)
        {
            var exit = await RunAsync(args);
            if (exit != 0)
            {
                throw new InvalidOperationException($"'{string.Join(' ', args)}' exited with {exit}: {Error}");
            }
        }
    }

    /// <summary>The markdown report written for a sprint, with line endings normalised to <c>\n</c>.</summary>
    public async Task<string> ReadReportAsync(long sprintId)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(ReportDirectory, $"sprint-{sprintId}.md"));
        return text.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Ad hoc SQL against the store, as the owner would run it. Query metric views only. Map rows to
    /// tuples or to classes with <c>init</c> properties (research note 04); SQLite integers are <c>long</c>.
    /// </summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return (await connection.QueryAsync<T>(sql, parameters)).AsList();
    }

    /// <summary>
    /// Every row of every table and view in the store as text, one block per table in name order with its rows
    /// sorted, for comparing the whole store before and after a command. It names no table, so it holds
    /// whatever the schema becomes.
    /// </summary>
    /// <param name="withoutAsOf">
    /// Leaves the stored as-of time (<c>run.as_of</c> and <c>data_quality.as_of</c>, nothing else) out, for
    /// comparing stores derived at different times: "identical" means identical apart from the as-of time
    /// (owner decision D33).
    /// </param>
    public async Task<string> ReadStoreContentAsync(bool withoutAsOf = false)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var names = (await connection.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%' ORDER BY name;")).AsList();

        var content = new System.Text.StringBuilder();
        foreach (var name in names)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
            await using var reader = await command.ExecuteReaderAsync();

            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                for (var column = 0; withoutAsOf && column < values.Length; column++)
                {
                    if (name is "run" or "data_quality" && reader.GetName(column) == "as_of")
                    {
                        values[column] = "(as-of time)";
                    }
                }

                rows.Add(string.Join(" | ", values.Select(value => value is DBNull ? "NULL" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))));
            }

            rows.Sort(StringComparer.Ordinal);
            content.Append("## ").Append(name).Append('\n');
            content.AppendJoin('\n', rows).Append('\n');
        }

        return content.ToString();
    }

    /// <summary>
    /// The bytes of the report file written for a sprint, exactly as they are on disk, as hexadecimal text:
    /// two reports are byte for byte the same when these strings are equal.
    /// </summary>
    /// <param name="sprintId">The sprint whose report is read.</param>
    /// <param name="withoutAsOf">
    /// Leaves the as-of time out of the footer's as-of line (the time only; the rest of the line stays), for
    /// comparing reports derived at different times (owner decision D33). The report must hold exactly one such time.
    /// </param>
    public async Task<string> ReadReportBytesAsync(long sprintId, bool withoutAsOf = false)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(ReportDirectory, $"sprint-{sprintId}.md"));
        if (!withoutAsOf)
        {
            return Convert.ToHexString(bytes);
        }

        // Latin-1 maps every byte to one character and back, so only the time's bytes are replaced.
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        var asOfTime = new System.Text.RegularExpressions.Regex(@"(?<=\n- As-of time: )\d{4}-\d{2}-\d{2} \d{2}:\d{2} [+-]\d{2}:\d{2}(?= \()");
        var found = asOfTime.Matches(text).Count;
        if (found != 1)
        {
            throw new InvalidOperationException($"The report for sprint {sprintId} holds {found} as-of times; expected 1.");
        }

        return Convert.ToHexString(System.Text.Encoding.Latin1.GetBytes(asOfTime.Replace(text, "(as-of time)")));
    }

    /// <summary>Runs a statement against the store with a writable connection, as the owner could from any SQLite client.</summary>
    public async Task ExecuteAsync(string sql)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql);
    }

    /// <summary>
    /// Every file the run left behind (configuration, database and its journal files, reports), as
    /// UTF-8 text with invalid bytes replaced, for scanning. SQLite stores text as UTF-8.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ReadAllFilesAsync()
    {
        var files = new Dictionary<string, string>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(Directory, path)] = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
        }

        return files;
    }

    public ValueTask DisposeAsync()
    {
        System.IO.Directory.Delete(Directory, recursive: true);
        Out.Dispose();
        Cancellation.Dispose();
        Error.Dispose();
        return ValueTask.CompletedTask;
    }
}
