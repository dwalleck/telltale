using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Telltale.Core.Storage;

namespace Telltale.Storage.Sqlite;

/// <summary>
/// The store as one SQLite file. The schema lives in the embedded scripts under <c>Sql/</c>; this
/// class runs them and maps rows. Scripts in a folder run in file-name order.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>Sql/Migrations</c>: raw tables. Each script runs once per database, recorded by file name. Never edit one
/// that has been merged; add a new file. Raw data is costly to refetch, so these tables are never dropped.</item>
/// <item><c>Sql/Derived</c>: derived tables. Every rebuild drops what the last one created and runs every script, so a
/// changed definition needs no migration (ADR-0002). A script only creates; it never drops.</item>
/// <item><c>Sql/Views</c>: metric views, the only thing Reporting and ad hoc SQL read. Rebuilt the same way, after the
/// derived tables. A group-size guard belongs inside its view (ADR-0003).</item>
/// <item>Tables and views the scripts create are recorded in <c>derived_object</c>. Anything else in the database
/// (a view the owner saved by hand) is left alone.</item>
/// </list>
/// Times are stored as UTC text (<see cref="StoredTime"/>).
/// </remarks>
public sealed class SqliteStore : ITelltaleStore
{
    private readonly string _connectionString;
    private readonly string _readOnlyConnectionString;

    static SqliteStore()
    {
        // Process-wide Dapper setting: issue_id maps to IssueId. Set once here, never in a test.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public SqliteStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // No pooling: a pooled connection keeps the file open after dispose, and a command-line run gains nothing from a pool.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();

        // Queries over the metric views cannot write, whatever SQL they are given.
        _readOnlyConnectionString = new SqliteConnectionStringBuilder(_connectionString) { Mode = SqliteOpenMode.ReadOnly }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        await connection.ExecuteAsync(Command(
            cancellationToken,
            """
            CREATE TABLE IF NOT EXISTS schema_migration (name TEXT PRIMARY KEY) STRICT;
            CREATE TABLE IF NOT EXISTS derived_object (name TEXT PRIMARY KEY, kind TEXT NOT NULL) STRICT;
            """));
        var applied = (await connection.QueryAsync<string>(Command(cancellationToken, "SELECT name FROM schema_migration;"))).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, sql) in Scripts.InFolder("Migrations").Where(script => !applied.Contains(script.Name)))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(Command(cancellationToken, sql, transaction: transaction));
            await connection.ExecuteAsync(Command(cancellationToken, "INSERT INTO schema_migration (name) VALUES (@name);", new { name }, transaction));
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task WriteRawAsync(RawData batch, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(Command(
            cancellationToken,
            """
            INSERT INTO issue (issue_id, issue_key, issue_type_id, created, status_id, resolution_id, story_points, assignee_hash, updated, sprint_ids)
            VALUES (@IssueId, @Key, @IssueTypeId, @Created, @StatusId, @ResolutionId, @StoryPoints, @AssigneeHash, @Updated, @SprintIds)
            ON CONFLICT (issue_id) DO UPDATE SET
                issue_key = excluded.issue_key,
                issue_type_id = excluded.issue_type_id,
                created = excluded.created,
                status_id = excluded.status_id,
                resolution_id = excluded.resolution_id,
                story_points = excluded.story_points,
                assignee_hash = excluded.assignee_hash,
                updated = excluded.updated,
                sprint_ids = excluded.sprint_ids;
            """,
            batch.Issues.Select(issue => new
            {
                issue.IssueId,
                issue.Key,
                issue.IssueTypeId,
                Created = StoredTime.ToStored(issue.Created),
                issue.StatusId,
                issue.ResolutionId,
                issue.StoryPoints,
                issue.AssigneeHash,
                Updated = StoredTime.ToStored(issue.Updated),
                issue.SprintIds,
            }),
            transaction));

        // A changelog entry never changes once written, so a repeat is ignored.
        await connection.ExecuteAsync(Command(
            cancellationToken,
            """
            INSERT INTO change (history_id, field, issue_id, at, from_value, to_value)
            VALUES (@HistoryId, @Field, @IssueId, @At, @From, @To)
            ON CONFLICT (history_id, field) DO NOTHING;
            """,
            batch.Changes.Select(change => new
            {
                change.HistoryId,
                change.Field,
                change.IssueId,
                At = StoredTime.ToStored(change.At),
                change.From,
                change.To,
            }),
            transaction));

        await connection.ExecuteAsync(Command(
            cancellationToken,
            """
            INSERT INTO sprint (sprint_id, board_id, name, state, started, planned_end, completed)
            VALUES (@SprintId, @BoardId, @Name, @State, @Started, @PlannedEnd, @Completed)
            ON CONFLICT (sprint_id) DO UPDATE SET
                board_id = excluded.board_id,
                name = excluded.name,
                state = excluded.state,
                started = excluded.started,
                planned_end = excluded.planned_end,
                completed = excluded.completed;
            """,
            batch.Sprints.Select(sprint => new
            {
                sprint.SprintId,
                sprint.BoardId,
                sprint.Name,
                sprint.State,
                Started = StoredTime.ToStored(sprint.Start),
                PlannedEnd = StoredTime.ToStored(sprint.PlannedEnd),
                Completed = StoredTime.ToStored(sprint.Completed),
            }),
            transaction));

        await connection.ExecuteAsync(Command(
            cancellationToken,
            """
            INSERT INTO status (status_id, name, category)
            VALUES (@StatusId, @Name, @Category)
            ON CONFLICT (status_id) DO UPDATE SET name = excluded.name, category = excluded.category;
            """,
            batch.Statuses,
            transaction));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<RawData> ReadRawAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);

        var issues = await connection.QueryAsync<IssueRow>(Command(cancellationToken, "SELECT * FROM issue ORDER BY issue_id;"));
        var changes = await connection.QueryAsync<ChangeRow>(Command(cancellationToken, "SELECT * FROM change ORDER BY issue_id, at, history_id;"));
        var sprints = await connection.QueryAsync<SprintRow>(Command(cancellationToken, "SELECT * FROM sprint ORDER BY sprint_id;"));
        var statuses = await connection.QueryAsync<StatusRow>(Command(cancellationToken, "SELECT * FROM status ORDER BY status_id;"));

        return new RawData(
            [.. issues.Select(row => new RawIssue(row.IssueId, row.IssueKey, row.IssueTypeId, StoredTime.FromStored(row.Created), row.StatusId, row.ResolutionId, row.StoryPoints, row.AssigneeHash, StoredTime.FromStored(row.Updated), row.SprintIds))],
            [.. changes.Select(row => new RawChange(row.HistoryId, row.IssueId, StoredTime.FromStored(row.At), row.Field, row.FromValue, row.ToValue))],
            [.. sprints.Select(row => new RawSprint(row.SprintId, row.BoardId, row.Name, row.State, StoredTime.FromStoredOrNull(row.Started), StoredTime.FromStoredOrNull(row.PlannedEnd), StoredTime.FromStoredOrNull(row.Completed)))],
            [.. statuses.Select(row => new RawStatus(row.StatusId, row.Name, row.Category))]);
    }

    public async Task ReplaceDerivedAsync(DerivedData derived, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Drop what the previous rebuild created, and only that: a table or view the owner made by hand is
        // not the tool's to drop, and one whose script has since been removed must not linger.
        // Views go first, because they read the derived tables.
        var previous = await connection.QueryAsync<(string Name, string Kind)>(Command(cancellationToken, "SELECT name, kind FROM derived_object ORDER BY kind = 'view' DESC, name;", transaction: transaction));
        foreach (var (name, kind) in previous)
        {
            await connection.ExecuteAsync(Command(cancellationToken, $"DROP {(kind == "view" ? "VIEW" : "TABLE")} IF EXISTS {Quoted(name)};", transaction: transaction));
        }

        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM derived_object;", transaction: transaction));
        var before = (await connection.QueryAsync<string>(Command(cancellationToken, AllTablesAndViews, transaction: transaction))).ToHashSet(StringComparer.Ordinal);

        foreach (var (_, sql) in Scripts.InFolder("Derived"))
        {
            await connection.ExecuteAsync(Command(cancellationToken, sql, transaction: transaction));
        }

        await InsertRowsAsync(
            connection,
            transaction,
            "INSERT INTO sprint_window (sprint_id, started, ended) VALUES (@sprintId, @started, @ended);",
            ["@sprintId", "@started", "@ended"],
            derived.SprintWindows.Select(window => new object?[] { window.SprintId, StoredTime.ToStored(window.Start), StoredTime.ToStored(window.End) }),
            cancellationToken);

        await InsertRowsAsync(
            connection,
            transaction,
            "INSERT INTO status_interval (issue_id, status_id, started, ended) VALUES (@issueId, @statusId, @started, @ended);",
            ["@issueId", "@statusId", "@started", "@ended"],
            derived.StatusIntervals.Select(interval => new object?[] { interval.IssueId, interval.StatusId, StoredTime.ToStored(interval.Start), StoredTime.ToStored(interval.End) }),
            cancellationToken);

        await InsertRowsAsync(
            connection,
            transaction,
            "INSERT INTO issue_cycle (issue_id, cycle_end, delivered, delivery_sprint_id) VALUES (@issueId, @cycleEnd, @delivered, @deliverySprintId);",
            ["@issueId", "@cycleEnd", "@delivered", "@deliverySprintId"],
            derived.IssueCycles.Select(cycle => new object?[] { cycle.IssueId, StoredTime.ToStored(cycle.CycleEnd), cycle.Delivered ? 1L : 0L, cycle.DeliverySprintId }),
            cancellationToken);

        await InsertRowsAsync(
            connection,
            transaction,
            "INSERT INTO membership_interval (issue_id, sprint_id, started, ended) VALUES (@issueId, @sprintId, @started, @ended);",
            ["@issueId", "@sprintId", "@started", "@ended"],
            derived.MembershipIntervals.Select(interval => new object?[] { interval.IssueId, interval.SprintId, StoredTime.ToStored(interval.Start), StoredTime.ToStored(interval.End) }),
            cancellationToken);

        await InsertRowsAsync(
            connection,
            transaction,
            "INSERT INTO sprint_status_time (issue_id, sprint_id, status_id, calendar_seconds, business_seconds, business_days) VALUES (@issueId, @sprintId, @statusId, @calendarSeconds, @businessSeconds, @businessDays);",
            ["@issueId", "@sprintId", "@statusId", "@calendarSeconds", "@businessSeconds", "@businessDays"],
            derived.SprintStatusTimes.Select(time => new object?[] { time.IssueId, time.SprintId, time.StatusId, time.CalendarTime.TotalSeconds, time.BusinessTime.TotalSeconds, time.BusinessDays }),
            cancellationToken);

        await InsertRowsAsync(
            connection,
            transaction,
            "INSERT INTO board_status (status_id, position, is_done) VALUES (@statusId, @position, @isDone);",
            ["@statusId", "@position", "@isDone"],
            derived.BoardStatuses.Select(status => new object?[] { status.StatusId, (long)status.Position, status.IsDone ? 1L : 0L }),
            cancellationToken);

        foreach (var (_, sql) in Scripts.InFolder("Views"))
        {
            await connection.ExecuteAsync(Command(cancellationToken, sql, transaction: transaction));
        }

        // Whatever the scripts created is the tool's, and is what the next rebuild drops.
        await connection.ExecuteAsync(Command(
            cancellationToken,
            "INSERT INTO derived_object (name, kind) SELECT name, type FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT IN @before;",
            new { before },
            transaction));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<T>> QueryViewsAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_readOnlyConnectionString, cancellationToken);
        return (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();
    }

    private static CommandDefinition Command(CancellationToken cancellationToken, string sql, object? parameters = null, DbTransaction? transaction = null) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    /// <summary>
    /// Inserts many rows through one command: the statement is prepared once and only the parameter
    /// values change per row. The derived tables are rebuilt in full on every run, so this is the hot path.
    /// </summary>
    private static async Task InsertRowsAsync(DbConnection connection, DbTransaction transaction, string sql, string[] parameterNames, IEnumerable<object?[]> rows, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in parameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            command.Parameters.Add(parameter);
        }

        var prepared = false;
        foreach (var row in rows)
        {
            for (var index = 0; index < row.Length; index++)
            {
                command.Parameters[index].Value = row[index] ?? DBNull.Value;
            }

            if (!prepared)
            {
                await command.PrepareAsync(cancellationToken);
                prepared = true;
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private const string AllTablesAndViews = "SELECT name FROM sqlite_master WHERE type IN ('table', 'view');";

    private static string Quoted(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static async Task<DbConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // Row shapes as stored. Times are text; StoredTime converts them.
    private sealed class IssueRow
    {
        public string IssueId { get; init; } = "";
        public string IssueKey { get; init; } = "";
        public string IssueTypeId { get; init; } = "";
        public string Created { get; init; } = "";
        public string StatusId { get; init; } = "";
        public string? ResolutionId { get; init; }
        public double? StoryPoints { get; init; }
        public string? AssigneeHash { get; init; }
        public string Updated { get; init; } = "";
        public string? SprintIds { get; init; }
    }

    private sealed class ChangeRow
    {
        public string HistoryId { get; init; } = "";
        public string Field { get; init; } = "";
        public string IssueId { get; init; } = "";
        public string At { get; init; } = "";
        public string? FromValue { get; init; }
        public string? ToValue { get; init; }
    }

    private sealed class SprintRow
    {
        public string SprintId { get; init; } = "";
        public long BoardId { get; init; }
        public string Name { get; init; } = "";
        public string State { get; init; } = "";
        public string? Started { get; init; }
        public string? PlannedEnd { get; init; }
        public string? Completed { get; init; }
    }

    private sealed class StatusRow
    {
        public string StatusId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
    }
}
