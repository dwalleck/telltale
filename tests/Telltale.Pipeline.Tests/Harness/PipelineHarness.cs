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
            new InMemorySecretStore(Secrets),
            Out,
            Error,
            ConfigPath,
            DatabasePath,
            ReportDirectory);

        return await CliApp.RunAsync(args, services, Cancellation.Token);
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
