using System.Data.Common;
using System.Globalization;
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
/// that has been merged; add a new file. Raw data is costly to refetch, so these tables are never dropped.
/// Their rows are added and updated by Jira's own IDs (<see cref="WriteRawAsync(RawData, Watermark, CancellationToken)"/>),
/// except in <see cref="ReplaceRawAsync"/>: a backfill removes every issue, change and sprint and writes what it
/// fetched in their place, so a row for something that has left Jira does not stay. Statuses are only ever added and updated.</item>
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

    public Task WriteRawAsync(RawData batch, Watermark reached, CancellationToken cancellationToken = default) =>
        WriteRawAsync(batch, reached, replaceAll: false, cancellationToken);

    public Task ReplaceRawAsync(RawData everything, Watermark reached, CancellationToken cancellationToken = default) =>
        WriteRawAsync(everything, reached, replaceAll: true, cancellationToken);

    public async Task<RawCounts> CountRawAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        return await connection.QuerySingleAsync<RawCounts>(Command(
            cancellationToken,
            "SELECT (SELECT COUNT(*) FROM issue) AS Issues, (SELECT COUNT(*) FROM sprint) AS Sprints, (SELECT COUNT(*) FROM change) AS Changes;"));
    }

    private async Task WriteRawAsync(RawData batch, Watermark reached, bool replaceAll, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var hashedWithBefore = await ReadSaltFingerprintAsync(connection, transaction, cancellationToken);

        // A backfill's batch is everything Jira holds now, so whatever else the raw tables hold has left Jira,
        // or was fetched under settings or a salt that no longer apply (owner decision D4). Inside the
        // transaction, so a failed write leaves the rows that were there. The status table is not cleared:
        // it gives a status ID its name, and stored history can name a status Jira no longer lists.
        if (replaceAll)
        {
            await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM change; DELETE FROM issue; DELETE FROM sprint;", transaction: transaction));
        }

        // An issue that is out of scope now may have been stored while it was in scope (owner decision D13).
        // Its rows go in the same transaction as the batch, so the store never holds half of the change.
        // One statement per table, however many issues: the IDs travel as one JSON array.
        // After a replace nothing of such an issue is left to delete.
        var outOfScope = new { issueIds = JsonSerializer.Serialize(replaceAll ? [] : batch.OutOfScopeIssueIds) };
        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM change WHERE issue_id IN (SELECT value FROM json_each(@issueIds));", outOfScope, transaction));
        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM issue WHERE issue_id IN (SELECT value FROM json_each(@issueIds));", outOfScope, transaction));

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            """
            INSERT INTO issue (issue_id, issue_type_id, created, status_id, resolution_id, story_points, assignee_hash, updated, sprint_ids)
            VALUES (@issueId, @issueTypeId, @created, @statusId, @resolutionId, @storyPoints, @assigneeHash, @updated, @sprintIds)
            ON CONFLICT (issue_id) DO UPDATE SET
                issue_type_id = excluded.issue_type_id,
                created = excluded.created,
                status_id = excluded.status_id,
                resolution_id = excluded.resolution_id,
                story_points = excluded.story_points,
                assignee_hash = excluded.assignee_hash,
                updated = excluded.updated,
                sprint_ids = excluded.sprint_ids;
            """,
            ["@issueId", "@issueTypeId", "@created", "@statusId", "@resolutionId", "@storyPoints", "@assigneeHash", "@updated", "@sprintIds"],
            batch.Issues.Select(issue => new object?[]
            {
                issue.IssueId, issue.IssueTypeId, StoredTime.ToStored(issue.Created), issue.StatusId, issue.ResolutionId,
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
            INSERT INTO watermark (collector, site_url, board_id, project_keys, start_date, history_fields, updated_through)
            VALUES (@Collector, @SiteUrl, @BoardId, @ProjectKeys, @StartDate, @HistoryFields, @UpdatedThrough)
            ON CONFLICT (collector) DO UPDATE SET
                site_url = excluded.site_url,
                board_id = excluded.board_id,
                project_keys = excluded.project_keys,
                start_date = excluded.start_date,
                history_fields = excluded.history_fields,
                updated_through = excluded.updated_through;
            """,
            new
            {
                reached.Collector,
                reached.Scope.SiteUrl,
                reached.Scope.BoardId,
                reached.Scope.ProjectKeys,
                StartDate = reached.Scope.StoredStartDate,
                reached.Scope.HistoryFields,
                UpdatedThrough = StoredTime.ToStored(reached.UpdatedThrough),
            },
            transaction));

        await RecordSaltAsync(connection, transaction, batch, hashedWithBefore, cancellationToken);

        // What the raw tables hold now, identified. Written with the batch, so the digest is never of anything
        // but the stored rows, and whoever asks whether the raw data changed reads one row.
        var digest = DigestOf(await ReadRawAsync(connection, transaction, cancellationToken));
        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM raw_state; INSERT INTO raw_state (digest) VALUES (@digest);", new { digest }, transaction));
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
    /// Records the salt a batch was hashed with, after the batch's rows are written. A batch under another
    /// salt than the store's people were hashed with comes only from a backfill, which replaces every issue
    /// (<see cref="ReplaceRawAsync"/>), so no row is left with a hash of the old salt. A sync never brings one:
    /// the collector refuses to fetch under another salt than the store's.
    /// </summary>
    private static async Task RecordSaltAsync(DbConnection connection, DbTransaction transaction, RawData batch, string? hashedWithBefore, CancellationToken cancellationToken)
    {
        if (batch.SaltFingerprint is not { } fingerprint || fingerprint == hashedWithBefore)
        {
            return;
        }

        await connection.ExecuteAsync(Command(cancellationToken, "DELETE FROM person_hashing;", transaction: transaction));
        await connection.ExecuteAsync(Command(cancellationToken, "INSERT INTO person_hashing (salt_fingerprint) VALUES (@fingerprint);", new { fingerprint }, transaction));
    }

    public async Task<Watermark?> ReadWatermarkAsync(string collector, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<WatermarkRow>(Command(
            cancellationToken,
            "SELECT site_url, board_id, project_keys, start_date, history_fields, updated_through FROM watermark WHERE collector = @collector;",
            new { collector }));
        return row is null
            ? null
            : new Watermark(
                collector,
                new CollectionScope(
                    row.SiteUrl,
                    row.BoardId,
                    row.ProjectKeys,
                    row.StartDate is null ? null : DateOnly.ParseExact(row.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    row.HistoryFields),
                StoredTime.FromStoredOrNull(row.UpdatedThrough));
    }

    public async Task<string?> ReadRawDigestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(Command(cancellationToken, "SELECT digest FROM raw_state;"));
    }

    /// <summary>
    /// Identifies raw data by its content: equal raw data gives an equal digest, whenever and however often
    /// it was written. It covers every field of every raw row, so a field added to the raw records is covered
    /// without a change here. The rows must be in the store's fixed reading order.
    /// </summary>
    private static string DigestOf(RawData raw) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(raw)));

    public async Task<RawData> ReadRawAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);
        return await ReadRawAsync(connection, transaction: null, cancellationToken);
    }

    private static async Task<RawData> ReadRawAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var issues = await connection.QueryAsync<IssueRow>(Command(cancellationToken, "SELECT * FROM issue ORDER BY issue_id;", transaction: transaction));
        var changes = await connection.QueryAsync<ChangeRow>(Command(cancellationToken, "SELECT * FROM change ORDER BY issue_id, at, history_id;", transaction: transaction));
        var sprints = await connection.QueryAsync<SprintRow>(Command(cancellationToken, "SELECT * FROM sprint ORDER BY sprint_id;", transaction: transaction));
        var statuses = await connection.QueryAsync<StatusRow>(Command(cancellationToken, "SELECT * FROM status ORDER BY status_id;", transaction: transaction));

        return new RawData(
            [.. issues.Select(row => new RawIssue(row.IssueId, row.IssueTypeId, StoredTime.FromStored(row.Created), row.StatusId, row.ResolutionId, row.StoryPoints, row.AssigneeHash, StoredTime.FromStored(row.Updated), row.SprintIds))],
            [.. changes.Select(row => new RawChange(row.HistoryId, row.IssueId, StoredTime.FromStored(row.At), row.Field, row.FromValue, row.ToValue))],
            [.. sprints.Select(row => new RawSprint(row.SprintId, row.BoardId, row.Name, row.State, StoredTime.FromStoredOrNull(row.Started), StoredTime.FromStoredOrNull(row.PlannedEnd), StoredTime.FromStoredOrNull(row.Completed)))],
            [.. statuses.Select(row => new RawStatus(row.StatusId, row.Name, row.Category))]);
    }

    public async Task ReplaceDerivedAsync(DerivedData derived, DerivedSource source, CancellationToken cancellationToken = default)
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

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            """
            INSERT INTO run (as_of, raw_digest, time_zone, holidays, baseline_sprint_ids, status_roles, abandoned_resolution_ids, objects_version)
            VALUES (@asOf, @rawDigest, @timeZone, @holidays, @baselineSprintIds, @statusRoles, @abandonedResolutionIds, @objectsVersion);
            """,
            ["@asOf", "@rawDigest", "@timeZone", "@holidays", "@baselineSprintIds", "@statusRoles", "@abandonedResolutionIds", "@objectsVersion"],
            [[StoredTime.ToStored(derived.AsOf), source.RawDigest, source.TimeZone, source.Holidays, source.BaselineSprintIds, source.StatusRoles, source.AbandonedResolutionIds, DerivedObjectsVersion]],
            cancellationToken);

        await ExecuteForEachRowAsync(
            connection,
            transaction,
            "INSERT INTO anomaly (issue_id, message) VALUES (@issueId, @message);",
            ["@issueId", "@message"],
            derived.Anomalies.Select(anomaly => new object?[] { anomaly.IssueId, anomaly.Message }),
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

    public async Task<DerivedSource?> ReadDerivedSourceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);

        // The table is itself derived: before the first rebuild it does not exist.
        if (!await connection.ExecuteScalarAsync<bool>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM derived_object WHERE name = 'run');")))
        {
            return null;
        }

        // Another version's table can lack a column read here, so its version is asked first.
        await RequireDerivedByThisVersionAsync(connection, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<DerivedSource?>(Command(cancellationToken, "SELECT raw_digest, time_zone, holidays, baseline_sprint_ids, status_roles, abandoned_resolution_ids FROM run;"));
    }

    /// <summary>
    /// Stops when the derived tables and metric views were created by other scripts than this version's.
    /// Their columns and rules are then not the ones this version reads and relies on. Deriving again repairs it.
    /// </summary>
    private static async Task RequireDerivedByThisVersionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        // Derived data from before the version was recorded has no such column.
        var recordsVersion = await connection.ExecuteScalarAsync<bool>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM pragma_table_info('run') WHERE name = 'objects_version');"));
        var version = recordsVersion ? await connection.QuerySingleOrDefaultAsync<string?>(Command(cancellationToken, "SELECT objects_version FROM run;")) : null;
        if (version != DerivedObjectsVersion)
        {
            throw new UserFacingException("The derived data in the store was built by another version of this tool. Run derive first.");
        }
    }

    public async Task<IReadOnlySet<(string IssueId, string Message)>> ReadAnomaliesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_connectionString, cancellationToken);

        // Before the first rebuild there is no anomaly table, and so no anomaly the store has seen.
        if (!await connection.ExecuteScalarAsync<bool>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM derived_object WHERE name = 'anomaly');")))
        {
            return new HashSet<(string, string)>();
        }

        return (await connection.QueryAsync<(string, string)>(Command(cancellationToken, "SELECT issue_id, message FROM anomaly;"))).ToHashSet();
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
        if (!await connection.ExecuteScalarAsync<bool>(Command(cancellationToken, "SELECT EXISTS (SELECT 1 FROM derived_object WHERE name = 'run');")))
        {
            throw new UserFacingException("The store has no derived data yet. Run derive first.");
        }

        await RequireDerivedByThisVersionAsync(connection, cancellationToken);

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

    /// <summary>
    /// Opens a connection. One that can write overwrites what it deletes (<c>secure_delete</c>): without it a
    /// deleted row stays readable in the file's free pages, and a backfill under a new salt, or the removal
    /// of an issue, would leave the old person hashes in the file for anyone who holds it. The setting is
    /// per connection, so it is set on every one.
    /// </summary>
    private static async Task<SqliteConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!IsReadOnly(connectionString))
        {
            await connection.ExecuteAsync(Command(cancellationToken, "PRAGMA secure_delete = ON;"));
        }

        return connection;
    }

    private static bool IsReadOnly(string connectionString) =>
        new SqliteConnectionStringBuilder(connectionString).Mode == SqliteOpenMode.ReadOnly;

    // Row shapes as stored. Times are text; StoredTime converts them.
    private sealed class IssueRow
    {
        public string IssueId { get; init; } = "";
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
        public string SiteUrl { get; init; } = "";
        public long BoardId { get; init; }
        public string ProjectKeys { get; init; } = "";
        public string? StartDate { get; init; }
        public string HistoryFields { get; init; } = "";
        public string? UpdatedThrough { get; init; }
    }

    private sealed class StatusRow
    {
        public string StatusId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
    }
}
