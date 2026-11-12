using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Telltale.Core;
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

        // Queries over the metric views cannot write to the file, whatever SQL they are given.
        _readOnlyConnectionString = ReadOnlyConnectionString(databasePath);
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

    public async Task WriteRawAsync(RawData batch, Watermark reached, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var hashedWithBefore = await ReadSaltFingerprintAsync(connection, transaction, cancellationToken);

        // An issue that is out of scope now may have been stored while it was in scope (owner decision D13).
        // Its rows go in the same transaction as the batch, so the store never holds half of the change.
        // One statement per table, however many issues: the IDs travel as one JSON array.
        var outOfScope = new { issueIds = JsonSerializer.Serialize(batch.OutOfScopeIssueIds) };
        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM change WHERE issue_id IN (SELECT value FROM json_each(@issueIds));", outOfScope, transaction));
        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM issue WHERE issue_id IN (SELECT value FROM json_each(@issueIds));", outOfScope, transaction));

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            """
            INSERT INTO issue (issue_id, issue_key, issue_type_id, created, status_id, resolution_id, story_points, assignee_hash, updated, sprint_ids)
            VALUES (@issueId, @key, @issueTypeId, @created, @statusId, @resolutionId, @storyPoints, @assigneeHash, @updated, @sprintIds)
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
            ["@issueId", "@key", "@issueTypeId", "@created", "@statusId", "@resolutionId", "@storyPoints", "@assigneeHash", "@updated", "@sprintIds"],
            batch.Issues.Select(issue => new object?[]
            {
                issue.IssueId, issue.Key, issue.IssueTypeId, StoredTime.ToStored(issue.Created), issue.StatusId, issue.ResolutionId,
                issue.StoryPoints, issue.AssigneeHash, StoredTime.ToStored(issue.Updated), issue.SprintIds,
            }),
            cancellationToken);

        // A changelog entry never changes once written, so a repeat is ignored.
        await ExecuteForEachRowAsync(
            connection,
            transaction,
            """
            INSERT INTO change (history_id, field, issue_id, at, from_value, to_value)
            VALUES (@historyId, @field, @issueId, @at, @from, @to)
            ON CONFLICT (history_id, field) DO NOTHING;
            """,
            ["@historyId", "@field", "@issueId", "@at", "@from", "@to"],
            batch.Changes.Select(change => new object?[] { change.HistoryId, change.Field, change.IssueId, StoredTime.ToStored(change.At), change.From, change.To }),
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            """
            INSERT INTO sprint (sprint_id, board_id, name, state, started, planned_end, completed)
            VALUES (@sprintId, @boardId, @name, @state, @started, @plannedEnd, @completed)
            ON CONFLICT (sprint_id) DO UPDATE SET
                board_id = excluded.board_id,
                name = excluded.name,
                state = excluded.state,
                started = excluded.started,
                planned_end = excluded.planned_end,
                completed = excluded.completed;
            """,
            ["@sprintId", "@boardId", "@name", "@state", "@started", "@plannedEnd", "@completed"],
            batch.Sprints.Select(sprint => new object?[]
            {
                sprint.SprintId, sprint.BoardId, sprint.Name, sprint.State,
                StoredTime.ToStored(sprint.Start), StoredTime.ToStored(sprint.PlannedEnd), StoredTime.ToStored(sprint.Completed),
            }),
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            """
            INSERT INTO status (status_id, name, category)
            VALUES (@statusId, @name, @category)
            ON CONFLICT (status_id) DO UPDATE SET name = excluded.name, category = excluded.category;
            """,
            ["@statusId", "@name", "@category"],
            batch.Statuses.Select(status => new object?[] { status.StatusId, status.Name, status.Category }),
            cancellationToken);

        // Set, not raised: a batch that reaches less far (a backfill starting over) leaves a watermark that
        // makes the next sync fetch more, which is always safe.
        await connection.ExecuteAsync(Command(
            cancellationToken,
            """
            INSERT INTO watermark (collector, covers, updated_through)
            VALUES (@Collector, @Covers, @UpdatedThrough)
            ON CONFLICT (collector) DO UPDATE SET covers = excluded.covers, updated_through = excluded.updated_through;
            """,
            new { reached.Collector, reached.Covers, UpdatedThrough = StoredTime.ToStored(reached.UpdatedThrough) },
            transaction));

        await RecordSaltAsync(connection, transaction, batch, hashedWithBefore, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string?> ReadSaltFingerprintAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        return await ReadSaltFingerprintAsync(connection, transaction: null, cancellationToken);
    }

    /// <summary>What a store that holds people says when it does not record which salt hashed them. No fingerprint equals it.</summary>
    private const string UnknownSalt = "unknown";

    private static async Task<string?> ReadSaltFingerprintAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<string?>(Command(cancellationToken, "SELECT salt_fingerprint FROM person_hashing;", transaction: transaction))
        ?? (await connection.ExecuteScalarAsync<long>(Command(cancellationToken, "SELECT COUNT(*) FROM issue WHERE assignee_hash IS NOT NULL;", transaction: transaction)) > 0 ? UnknownSalt : null);

    /// <summary>
    /// Records the salt a batch was hashed with, after the batch's rows are written. When it is another salt
    /// than the store's people were hashed with, every stored issue with an assignee must have been in the
    /// batch: a row left behind would keep a hash of the old salt, and the same person would count twice.
    /// </summary>
    private static async Task RecordSaltAsync(DbConnection connection, DbTransaction transaction, RawData batch, string? hashedWithBefore, CancellationToken cancellationToken)
    {
        if (batch.SaltFingerprint is not { } fingerprint || fingerprint == hashedWithBefore)
        {
            return;
        }

        if (hashedWithBefore is not null)
        {
            var rewritten = batch.Issues.Select(issue => issue.IssueId).ToHashSet(StringComparer.Ordinal);
            var withAssignee = await connection.QueryAsync<string>(Command(cancellationToken, "SELECT issue_id FROM issue WHERE assignee_hash IS NOT NULL;", transaction: transaction));
            var leftBehind = withAssignee.Count(issueId => !rewritten.Contains(issueId));
            if (leftBehind > 0)
            {
                // Thrown before the commit, so the transaction rolls back and nothing of the batch is kept.
                throw new UserFacingException(
                    (leftBehind == 1
                        ? "1 issue in the database is no longer in Jira's answer, so its assignee cannot be hashed again with the new salt."
                        : $"{leftBehind} issues in the database are no longer in Jira's answer, so their assignees cannot be hashed again with the new salt.")
                    + " Nothing was stored. Restore the original salt, or delete the database file and run backfill.");
            }
        }

        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM person_hashing;", transaction: transaction));
        await connection.ExecuteAsync(Command(cancellationToken, "INSERT INTO person_hashing (salt_fingerprint) VALUES (@fingerprint);", new { fingerprint }, transaction));
    }

    public async Task<Watermark?> ReadWatermarkAsync(string collector, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<WatermarkRow>(Command(cancellationToken, "SELECT covers, updated_through FROM watermark WHERE collector = @collector;", new { collector }));
        return row is null ? null : new Watermark(collector, row.Covers, StoredTime.FromStoredOrNull(row.UpdatedThrough));
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

    public async Task ReplaceDerivedAsync(DerivedData derived, string rawDigest, CancellationToken cancellationToken = default)
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

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            "INSERT INTO sprint_window (sprint_id, started, ended) VALUES (@sprintId, @started, @ended);",
            ["@sprintId", "@started", "@ended"],
            derived.SprintWindows.Select(window => new object?[] { window.SprintId, StoredTime.ToStored(window.Start), StoredTime.ToStored(window.End) }),
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            "INSERT INTO status_interval (issue_id, status_id, started, ended) VALUES (@issueId, @statusId, @started, @ended);",
            ["@issueId", "@statusId", "@started", "@ended"],
            derived.StatusIntervals.Select(interval => new object?[] { interval.IssueId, interval.StatusId, StoredTime.ToStored(interval.Start), StoredTime.ToStored(interval.End) }),
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            """
            INSERT INTO issue_cycle (issue_id, cycle_start, cycle_end, calendar_days, business_days, point_bucket, delivered, delivery_sprint_id, reopen_count)
            VALUES (@issueId, @cycleStart, @cycleEnd, @calendarDays, @businessDays, @pointBucket, @delivered, @deliverySprintId, @reopenCount);
            """,
            ["@issueId", "@cycleStart", "@cycleEnd", "@calendarDays", "@businessDays", "@pointBucket", "@delivered", "@deliverySprintId", "@reopenCount"],
            derived.IssueCycles.Select(cycle => new object?[]
            {
                cycle.IssueId, StoredTime.ToStored(cycle.Cycle?.Start), StoredTime.ToStored(cycle.CycleEnd), cycle.Cycle?.CalendarDays, cycle.Cycle?.BusinessDays,
                cycle.PointBucket, cycle.Delivered ? 1L : 0L, cycle.DeliverySprintId, (long)cycle.ReopenCount,
            }),
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            "INSERT INTO membership_interval (issue_id, sprint_id, started, ended) VALUES (@issueId, @sprintId, @started, @ended);",
            ["@issueId", "@sprintId", "@started", "@ended"],
            derived.MembershipIntervals.Select(interval => new object?[] { interval.IssueId, interval.SprintId, StoredTime.ToStored(interval.Start), StoredTime.ToStored(interval.End) }),
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            "INSERT INTO sprint_status_time (issue_id, sprint_id, status_id, calendar_seconds, business_seconds, business_days) VALUES (@issueId, @sprintId, @statusId, @calendarSeconds, @businessSeconds, @businessDays);",
            ["@issueId", "@sprintId", "@statusId", "@calendarSeconds", "@businessSeconds", "@businessDays"],
            derived.SprintStatusTimes.Select(time => new object?[] { time.IssueId, time.SprintId, time.StatusId, time.CalendarTime.TotalSeconds, time.BusinessTime.TotalSeconds, time.BusinessDays }),
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            "INSERT INTO board_status (status_id, position, is_done) VALUES (@statusId, @position, @isDone);",
            ["@statusId", "@position", "@isDone"],
            derived.BoardStatuses.Select(status => new object?[] { status.StatusId, (long)status.Position, status.IsDone ? 1L : 0L }),
            cancellationToken);

        await connection.ExecuteAsync(Command(cancellationToken, "INSERT INTO derived_source (raw_digest, objects_version) VALUES (@rawDigest, @DerivedObjectsVersion);", new { rawDigest, DerivedObjectsVersion }, transaction));

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

    public async Task<string?> ReadDerivedSourceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);

        // The table is itself derived: before the first rebuild it does not exist.
        var built = await connection.ExecuteScalarAsync<bool>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM derived_object WHERE name = 'derived_source');"));
        return built
            ? await connection.QuerySingleOrDefaultAsync<string?>(Command(cancellationToken, "SELECT raw_digest FROM derived_source;"))
            : null;
    }

    public async Task<IReadOnlyList<T>> QueryViewsAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_readOnlyConnectionString, cancellationToken);

        // The query runs against copies of the metric views' rows in the connection's temporary schema, and from
        // here on the connection may read nothing else. So the views' own rules (the minimum group size above all,
        // ADR-0003) are applied before the query sees a row, whatever SQL it is given. An unqualified view name
        // finds the copy first.
        var views = await CopyMetricViewsAsync(connection, sql, cancellationToken);
        var rc = raw.sqlite3_set_authorizer(connection.Handle, MetricViewsOnly, views);
        if (rc != raw.SQLITE_OK)
        {
            throw new InvalidOperationException($"SQLite did not accept the guard that limits a query to the metric views (result code {rc}), so the query was not run.");
        }

        try
        {
            return (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == raw.SQLITE_AUTH)
        {
            throw new InvalidOperationException(
                $"This query reads only the metric views ({string.Join(", ", views.Order(StringComparer.Ordinal))}), and it asked for something else: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Identifies the scripts that create the derived tables and the metric views: a digest of their names and
    /// text. A rebuild stores it, so a store whose views another version of the tool created is told apart
    /// without anyone keeping a version number up to date.
    /// </summary>
    private static readonly string DerivedObjectsVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Concat(Scripts.InFolder("Derived").Concat(Scripts.InFolder("Views")).Select(script => $"{script.Name}\n{script.Sql}\n")))));

    /// <summary>
    /// Checks that the metric views are the ones this version of the tool creates and that all of them are
    /// there, then copies the rows of each view <paramref name="sql"/> names into a temporary table of the same
    /// name. Returns the names of all metric views: what the last rebuild recorded as its views.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="UserFacingException"/> when the views cannot be trusted or read; deriving again repairs each case.
    /// A view counts as named when its name is anywhere in the text. That can copy a view the query does not
    /// read, and never misses one it does.
    /// </remarks>
    private static async Task<HashSet<string>> CopyMetricViewsAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        if (!await connection.ExecuteScalarAsync<bool>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM derived_object WHERE name = 'derived_source');")))
        {
            throw new UserFacingException("The store has no derived data yet. Run derive first.");
        }

        // Derived data from before the version was recorded has no such column.
        var recordsVersion = await connection.ExecuteScalarAsync<bool>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM pragma_table_info('derived_source') WHERE name = 'objects_version');"));
        var version = recordsVersion ? await connection.QuerySingleOrDefaultAsync<string?>(Command(cancellationToken, "SELECT objects_version FROM derived_source;")) : null;
        if (version != DerivedObjectsVersion)
        {
            throw new UserFacingException("The derived data in the store was built by another version of this tool. Run derive first.");
        }

        // SQLite matches table names without regard to case, and so must the guard.
        var views = (await connection.QueryAsync<string>(Command(cancellationToken, "SELECT name FROM derived_object WHERE kind = 'view' ORDER BY name;"))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var present = (await connection.QueryAsync<string>(Command(cancellationToken, "SELECT name FROM sqlite_master WHERE type = 'view';"))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (views.FirstOrDefault(view => !present.Contains(view)) is { } missing)
        {
            throw new UserFacingException($"The metric view {missing} is not in the store. Run derive first.");
        }

        foreach (var view in views.Where(view => sql.Contains(view, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                await connection.ExecuteAsync(Command(cancellationToken, $"CREATE TEMP TABLE {Quoted(view)} AS SELECT * FROM main.{Quoted(view)};"));
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == raw.SQLITE_ERROR)
            {
                // SQLITE_ERROR is a fault in the statement, here in the view: a table it reads is gone or changed.
                // A locked or damaged file has other codes and is reported as what it is.
                throw new UserFacingException($"The metric view {view} could not be read. Run derive first.", exception);
            }
        }

        return views;
    }

    /// <summary>
    /// SQLite's authorizer for a query over the metric views. It allows a SELECT, the functions it calls and
    /// reads of the temporary schema, which holds the copies and nothing else. Every other action is denied:
    /// a read of a raw or derived table or of the schema, a write, a PRAGMA, an ATTACH.
    /// </summary>
    private static int MetricViewsOnly(object userData, int action, string? first, string? second, string? database, string? accessor)
    {
        var allowed = action switch
        {
            raw.SQLITE_SELECT or raw.SQLITE_FUNCTION => true,

            // A read that names no column (COUNT(*), EXISTS) comes without a schema name. The table's name
            // decides then: the metric views' names are all it may be.
            raw.SQLITE_READ => database == "temp" || (string.IsNullOrEmpty(second) && first is not null && ((HashSet<string>)userData).Contains(first)),
            _ => false,
        };

        return allowed ? raw.SQLITE_OK : raw.SQLITE_DENY;
    }

    /// <summary>
    /// Whether the database at this path holds at least one hashed person. A hash is only worth something with
    /// the salt that made it, so this is what a missing or new salt would break. The file is opened read-only
    /// and never created: no file, or one from before the raw tables existed, holds nobody.
    /// </summary>
    public static async Task<bool> HoldsHashedPeopleAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        await using var connection = await OpenAsync(ReadOnlyConnectionString(databasePath), cancellationToken);
        var hasIssues = await connection.ExecuteScalarAsync<long>(Command(cancellationToken, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'issue';"));
        return hasIssues > 0
            && await connection.ExecuteScalarAsync<long>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM issue WHERE assignee_hash IS NOT NULL);")) > 0;
    }

    /// <summary>A connection that cannot write, whatever SQL it is given, and never creates the file.</summary>
    private static string ReadOnlyConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, ForeignKeys = true, Mode = SqliteOpenMode.ReadOnly }.ToString();

    private static CommandDefinition Command(CancellationToken cancellationToken, string sql, object? parameters = null, DbTransaction? transaction = null) =>
        new(sql, parameters, transaction, cancellationToken: cancellationToken);

    /// <summary>
    /// Runs one statement once per row through one command: the statement is prepared once and only the
    /// parameter values change per row. Every write of many rows goes through here: a batch of raw rows, and
    /// the derived tables, which are rebuilt in full on every run.
    /// </summary>
    private static async Task ExecuteForEachRowAsync(DbConnection connection, DbTransaction transaction, string sql, string[] parameterNames, IEnumerable<object?[]> rows, CancellationToken cancellationToken)
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

    private static async Task<SqliteConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
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

    private sealed class WatermarkRow
    {
        public string Covers { get; init; } = "";
        public string? UpdatedThrough { get; init; }
    }

    private sealed class StatusRow
    {
        public string StatusId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
    }
}
