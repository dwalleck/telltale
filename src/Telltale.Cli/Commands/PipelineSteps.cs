using System.Globalization;
using Telltale.Collectors.Jira;
using Telltale.Core;
using Telltale.Core.Calendar;
using Telltale.Core.Configuration;
using Telltale.Core.Derive;
using Telltale.Core.Identity;
using Telltale.Core.Secrets;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;
using Telltale.Reporting;
using Telltale.Storage.Sqlite;

namespace Telltale.Cli.Commands;

/// <summary>
/// The steps commands are made of, over one loaded configuration and one opened store.
/// <c>run</c> is sync, derive, report; a command for a single step calls just that one.
/// </summary>
internal sealed class PipelineSteps : IDisposable
{
    private readonly AppServices _services;
    private readonly TelltaleConfig _config;
    private readonly StatusRoles _roles;
    private readonly IReadOnlySet<string> _abandonedResolutionIds;
    private readonly IReadOnlySet<string> _baselineSprintIds;
    private readonly TimeZoneInfo _timeZone;
    private readonly ITelltaleStore _store;
    private readonly IDisposable _storeLock;

    private PipelineSteps(AppServices services, TelltaleConfig config, StatusRoles roles, IReadOnlySet<string> abandonedResolutionIds, IReadOnlySet<string> baselineSprintIds, TimeZoneInfo timeZone, ITelltaleStore store, IDisposable storeLock)
    {
        _abandonedResolutionIds = abandonedResolutionIds;
        _baselineSprintIds = baselineSprintIds;
        _storeLock = storeLock;
        _services = services;
        _config = config;
        _roles = roles;
        _timeZone = timeZone;
        _store = store;
    }

    /// <summary>
    /// Loads and checks the configuration, takes the store for this command alone (<see cref="StoreLock"/>) and
    /// opens it. With <paramref name="needsJira"/> (the command will fetch), the secrets are checked first, so a
    /// run that cannot start leaves no empty database behind, and the database is created if it is missing.
    /// Without it the command only works on what the store holds, so a missing database is refused, not created.
    /// Dispose the result to release the store.
    /// </summary>
    public static async Task<PipelineSteps> StartAsync(AppServices services, bool needsJira, CancellationToken cancellationToken)
    {
        var config = await ConfigFile.LoadAsync(services.ConfigPath, cancellationToken);
        var roles = ReadStatusRoles(config, services.ConfigPath);
        var abandonedResolutionIds = ReadAbandonedResolutionIds(config, services.ConfigPath);
        var baselineSprintIds = ReadBaselineSprintIds(config, services.ConfigPath);
        var timeZone = ReadTimeZone(config, services.ConfigPath);

        if (needsJira)
        {
            await RequireSecretsAsync(services, cancellationToken);
        }
        else if (!File.Exists(services.DatabasePath))
        {
            throw new UserFacingException($"There is no database at {services.DatabasePath} yet. Run backfill first.");
        }

        var storeLock = StoreLock.Acquire(services.DatabasePath);
        try
        {
            var store = new SqliteStore(services.DatabasePath);
            await store.InitializeAsync(cancellationToken);
            return new PipelineSteps(services, config, roles, abandonedResolutionIds, baselineSprintIds, timeZone, store, storeLock);
        }
        catch
        {
            storeLock.Dispose();
            throw;
        }
    }

    public void Dispose() => _storeLock.Dispose();

    /// <summary>
    /// Fetches everything from the configured start date and replaces the store's issues, changes and sprints
    /// with it, then says how many of each the store held before and holds now. A backfill removes what it
    /// did not fetch, so the counts are how the user sees one that replaced much with little.
    /// </summary>
    /// <param name="allowEmpty">Lets the backfill replace stored issues or sprints with none (<c>--allow-empty</c>).</param>
    public async Task BackfillAsync(bool allowEmpty, CancellationToken cancellationToken)
    {
        // The store is this command's alone (StoreLock), so nothing changes it between the counts and the write.
        var before = await _store.CountRawAsync(cancellationToken);
        await CollectAsync(CollectionMode.Backfill, allowEmpty, cancellationToken);
        var now = await _store.CountRawAsync(cancellationToken);
        await _services.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"The store now holds what Jira returned. Issues: {before.Issues} before, {now.Issues} now. Sprints: {before.Sprints} before, {now.Sprints} now. Changes: {before.Changes} before, {now.Changes} now."));
    }

    /// <summary>Brings the raw tables up to date with what changed in Jira since the last sync or backfill.</summary>
    public Task SyncAsync(CancellationToken cancellationToken) => CollectAsync(CollectionMode.Incremental, allowEmpty: false, cancellationToken);

    private Task CollectAsync(CollectionMode mode, bool allowEmpty, CancellationToken cancellationToken) =>
        new JiraCollector(_services.HttpHandler, _config.Jira, _services.Secrets, _services.Clock, _services.Error).BringUpToDateAsync(_store, mode, allowEmpty, cancellationToken);

    /// <summary>
    /// Rebuilds every derived table and metric view from the raw tables, and logs the changelog anomalies it
    /// found to standard error: one line with the totals, then one line for each anomaly (an issue and a kind)
    /// the previous rebuild did not have. The store's <c>anomaly</c> table holds the full list. A line holds the
    /// Jira issue ID and the kind's fixed message, never the key. Derive is the step that builds the timelines,
    /// so it is the one that logs.
    /// </summary>
    public async Task DeriveAsync(CancellationToken cancellationToken)
    {
        await RequireFilledUnderTheConfigurationAsync(cancellationToken);
        await RequireStoredSaltAsync(cancellationToken);
        var raw = await _store.ReadRawAsync(cancellationToken);
        await RequireBaselineSprintsAsync(raw, cancellationToken);
        var derived = Derivation.Derive(raw, _roles, _abandonedResolutionIds, new BusinessCalendar(_timeZone, _config.Holidays), AsOf(raw));
        var previous = await _store.ReadAnomaliesAsync(cancellationToken);
        await _store.ReplaceDerivedAsync(derived, await SourceAsync(cancellationToken), cancellationToken);

        // Owner decision D51: such a sprint stops nothing. It is in no figure, and the console says so.
        foreach (var sprintId in derived.ClosedSprintsWithoutAnEnd)
        {
            await _services.Error.WriteLineAsync($"{SprintWindows.ClosedWithoutAnEnd(sprintId)}: no delivery is counted in it, and it cannot be reported or be a baseline sprint.");
        }

        // Owner decision D50: a baseline sprint short of five assignees stops nothing. The views withhold
        // every baseline figure, and the console says which sprint is why.
        var shortOfFive = await _store.QueryViewsAsync<string>("SELECT sprint_id FROM baseline_sprint WHERE suppressed <> 0 ORDER BY sprint_id;", cancellationToken: cancellationToken);
        if (shortOfFive.Count > 0)
        {
            await _services.Error.WriteLineAsync(BaselineSprintRules.ShortOfFiveAssignees(shortOfFive));
        }

        if (derived.Anomalies.Count == 0)
        {
            return;
        }

        var unseen = derived.Anomalies.Where(anomaly => !previous.Contains((anomaly.IssueId, anomaly.Message))).ToList();
        var issues = derived.Anomalies.Select(anomaly => anomaly.IssueId).Distinct(StringComparer.Ordinal).Count();
        await _services.Error.WriteLineAsync(
            $"Changelog anomalies: {derived.Anomalies.Count} across {issues} {(issues == 1 ? "issue" : "issues")}, {unseen.Count} new since the previous derive.");
        foreach (var anomaly in unseen)
        {
            await _services.Error.WriteLineAsync($"Changelog anomaly: issue {anomaly.IssueId}: {anomaly.Message}.");
        }
    }

    /// <summary>
    /// What a rebuild from the stored raw data under the current configuration is built from. The store
    /// keeps the digest of its raw data, so this reads one row and no raw row.
    /// </summary>
    private async Task<DerivedSource> SourceAsync(CancellationToken cancellationToken) =>
        new(
            await _store.ReadRawDigestAsync(cancellationToken) ?? "",
            _config.TimeZone,
            string.Join(",", _config.Holidays.Order().Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            DerivedSource.BaselineSprintIdsOf(_baselineSprintIds),
            string.Join(",", _config.StatusRoles.Select(status => $"{status.Key}={status.Value}")),
            string.Join(",", _abandonedResolutionIds.Order(StringComparer.Ordinal)));

    /// <summary>
    /// Derive and report read what the store holds, and the store holds what the settings it was last filled
    /// under asked for. Under other settings (another site, board, projects, start date or history fields)
    /// its figures would be the old settings', so the command stops, as a sync does. A store nothing was ever
    /// fetched into has nothing of another setting's.
    /// </summary>
    private async Task RequireFilledUnderTheConfigurationAsync(CancellationToken cancellationToken) =>
        (await _store.ReadWatermarkAsync(JiraCollector.WatermarkName, cancellationToken))?.Scope.RequireSameAs(JiraCollector.ScopeOf(_config.Jira));

    /// <summary>
    /// Derive and report need no secret. But when the salt is at hand and is not the one the stored people
    /// were hashed with, the store is about to be filled under two salts, and the figures' distinct-assignee
    /// counts are not to be relied on until a backfill settles it: the command stops, as a sync would.
    /// </summary>
    private async Task RequireStoredSaltAsync(CancellationToken cancellationToken)
    {
        if (_services.Secrets.Get(SecretName.HashingSalt) is { } salt && !string.IsNullOrWhiteSpace(salt))
        {
            IdentityHash.RequireSaltOf(await _store.ReadSaltFingerprintAsync(cancellationToken), salt.Trim());
        }
    }

    /// <summary>
    /// The as-of time that closes open intervals and the active sprint's window: the clock, read when
    /// derive starts, so after any sync in the same command. It is never earlier than the newest
    /// thing Jira reported, because Jira's clock can run ahead of this machine's; otherwise an issue
    /// finished moments ago would fall outside the active sprint.
    /// </summary>
    private DateTimeOffset AsOf(RawData raw)
    {
        var now = _services.Clock.GetUtcNow();
        var newest = raw.Changes.Select(change => change.At)
            .Concat(raw.Issues.Select(issue => issue.Updated))
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        // Windows are half-open, so the as-of time must be after the newest moment, not equal to it.
        // Stored times have millisecond precision.
        return newest >= now ? newest.AddMilliseconds(1) : now;
    }

    /// <summary>
    /// Writes one sprint's report and says where. Without <paramref name="sprintId"/> it is the most recently
    /// completed sprint's. When no sprint has completed, <paramref name="needsSprint"/> decides: a command whose
    /// only job is the report fails; one that also updated the store says no report was written and succeeds
    /// (owner decision D28).
    /// </summary>
    public async Task ReportAsync(long? sprintId, bool needsSprint, CancellationToken cancellationToken)
    {
        await RequireFilledUnderTheConfigurationAsync(cancellationToken);
        await RequireStoredSaltAsync(cancellationToken);
        var derivedFrom = await _store.ReadDerivedSourceAsync(cancellationToken)
            ?? throw new UserFacingException("The store has no derived data yet. Run derive first.");

        // The views would answer from the old derived tables without a sign, so the report checks first.
        var current = await SourceAsync(cancellationToken);
        if (derivedFrom.RawDigest != current.RawDigest)
        {
            throw new UserFacingException("The raw data changed since the derived data was built. Run derive first.");
        }

        // The stored business days were counted in the old time zone, around the old holidays.
        if (derivedFrom.TimeZone != current.TimeZone || derivedFrom.Holidays != current.Holidays)
        {
            throw new UserFacingException("The time zone or the holidays in the configuration changed since the derived data was built. Run derive first.");
        }

        // What is delivered, when a cycle starts and what a reopen is were decided with the old roles and resolutions.
        if (derivedFrom.StatusRoles != current.StatusRoles || derivedFrom.AbandonedResolutionIds != current.AbandonedResolutionIds)
        {
            throw new UserFacingException("statusRoles or abandonedResolutionIds in the configuration file changed since the derived data was built. Run derive first.");
        }

        // The baseline views were built from the list as it was at the last derive. A report under another
        // list would print the old baseline as if it were the configured one. This is the one check: a list
        // equal to the derived one passed derive's checks against this same raw data.
        if (derivedFrom.BaselineSprintIds != current.BaselineSprintIds)
        {
            // Deriving again is the remedy only for a list derive would accept; otherwise say what derive would.
            await RequireBaselineSprintsAsync(await _store.ReadRawAsync(cancellationToken), cancellationToken);
            throw new UserFacingException("baselineSprintIds in the configuration file changed since the derived data was built. Run derive first.");
        }

        var sprint = sprintId?.ToString(CultureInfo.InvariantCulture)
            ?? await SprintReport.MostRecentlyCompletedSprintAsync(_store, cancellationToken);
        if (sprint is null)
        {
            if (needsSprint)
            {
                throw new UserFacingException("No sprint in the store has completed yet. Name a sprint with --sprint.");
            }

            await _services.Out.WriteLineAsync("The store is up to date. No sprint has completed yet, so no report was written.");
            return;
        }

        // When each sprint with a window started, from the views: a report reads no raw row unless it has to
        // explain a refusal.
        var starts = (await _store.QueryViewsAsync<(string SprintId, string Started)>("SELECT sprint_id, started FROM delivered_per_sprint;", cancellationToken: cancellationToken))
            .Select(window => (window.SprintId, StoredTime.FromStored(window.Started)))
            .ToList();
        if (starts.All(window => window.SprintId != sprint)
            && (await _store.ReadRawAsync(cancellationToken)).Sprints.FirstOrDefault(stored => stored.SprintId == sprint) is { } named
            && SprintWindows.IsClosedWithoutAnEnd(named))
        {
            throw new UserFacingException($"{SprintWindows.ClosedWithoutAnEnd(sprint)} and no figures. No report was written.");
        }

        // Asked at every report, not only when the list or the raw data changed: a backfill from a later
        // start date can leave both as they were and still make a sprint one the store no longer covers.
        var completeFrom = await CompleteFromAsync(cancellationToken);
        try
        {
            FetchedRange.RequireReportable(sprint, starts, completeFrom);
        }
        catch (UserFacingException refusal) when (sprintId is null && !needsSprint)
        {
            // The owner named no sprint and the store was updated: that the last completed sprint cannot be
            // reported is said, like "no sprint has completed" (owner decision D28), and is not a failure.
            await _services.Out.WriteLineAsync($"The store is up to date. {refusal.Message}");
            return;
        }

        BaselineSprintRules.RequireWithinWhatWasFetched(starts, _baselineSprintIds, completeFrom);
        var path = await SprintReport.WriteAsync(_store, sprint, _timeZone, [.. _baselineSprintIds.Order(StringComparer.Ordinal)], _services.ReportDirectory, cancellationToken);
        await _services.Out.WriteLineAsync($"Wrote {path}");
    }

    /// <summary>
    /// What the raw data can say against the baseline sprint list: every baseline sprint is a completed sprint
    /// with a sprint window, and none started before the start date the store was last filled from. A team of
    /// five is the views' rule, and one that fails it is not refused (owner decision D50).
    /// </summary>
    private async Task RequireBaselineSprintsAsync(RawData raw, CancellationToken cancellationToken)
    {
        BaselineSprintRules.RequireCompleted(raw.Sprints, _baselineSprintIds);
        BaselineSprintRules.RequireWindow(raw.Sprints, _baselineSprintIds);
        if (_baselineSprintIds.Count > 0)
        {
            BaselineSprintRules.RequireWithinWhatWasFetched(
                raw.Sprints.Where(sprint => sprint.Start is not null).Select(sprint => (sprint.SprintId, sprint.Start!.Value)),
                _baselineSprintIds,
                await CompleteFromAsync(cancellationToken));
        }
    }

    /// <summary>The moment from which the store holds every issue: the start date it was last filled from. Null when it names none.</summary>
    private async Task<DateTimeOffset?> CompleteFromAsync(CancellationToken cancellationToken) =>
        await _store.ReadWatermarkAsync(JiraCollector.WatermarkName, cancellationToken) is { } filled ? JiraCollector.CompleteFrom(filled.Scope) : null;

    /// <summary>
    /// Stops with a message naming the first secret that is not set and how to set it. A missing hashing salt
    /// is two different situations: on a database that holds nobody yet, any salt will do and one can be
    /// generated; once people are stored under a salt, only that salt keeps each of them one person, until a
    /// backfill under a new one hashes them all again.
    /// </summary>
    private static async Task RequireSecretsAsync(AppServices services, CancellationToken cancellationToken)
    {
        foreach (var secret in SecretInfo.All)
        {
            if (secret.Name != SecretName.HashingSalt || !string.IsNullOrWhiteSpace(services.Secrets.Get(secret.Name)))
            {
                services.Secrets.Require(secret.Name);
                continue;
            }

            var howToSet = services.Secrets.HowToSet(secret.Name);
            if (await SqliteStore.HoldsHashedPeopleAsync(services.DatabasePath, cancellationToken))
            {
                throw new UserFacingException(
                    $"The secret {secret.Label} is not set in the secret store, and the database already holds people hashed with the salt that was there before. "
                    + "Restore the original salt, or set a new one and run backfill, which hashes everyone again with it; "
                    + $"until that backfill every other command stops, because a second salt would make each person count as two. {howToSet}");
            }

            // Only a store 'secrets set' can write to can be given a generated salt.
            var generate = services.Secrets is IWritableSecretStore ? " Add --generate to have a new random salt made." : string.Empty;
            throw new UserFacingException($"The secret {secret.Label} is not set in the secret store. {howToSet}{generate}");
        }
    }

    /// <summary>
    /// The status roles of the configuration. Every status ID must be digits, as Jira's are: anything else
    /// could match no issue. Without <paramref name="requireDone"/>, a map in which no status has the done role
    /// is returned and not refused; only <c>discover</c> asks for that (owner decision D40).
    /// </summary>
    internal static StatusRoles ReadStatusRoles(TelltaleConfig config, string path, bool requireDone = true)
    {
        try
        {
            // The message does not repeat the ID: what is not a status ID could be a secret pasted into the wrong place.
            if (config.StatusRoles.Keys.Any(statusId => statusId.Length == 0 || !statusId.All(char.IsAsciiDigit)))
            {
                throw new FormatException("every status ID must be a Jira status ID (digits only), and one is not.");
            }

            var roles = StatusRoles.Parse(config.StatusRoles);
            if (requireDone && !roles.HasDone)
            {
                throw new FormatException("no status has the role 'done', so nothing could ever be delivered.");
            }

            return roles;
        }
        catch (FormatException exception)
        {
            throw new UserFacingException($"The configuration file {path} is not valid: statusRoles: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// The abandoned resolution IDs, trimmed. Jira's resolution IDs are digits, so anything else (a resolution's
    /// name, a stray character, an empty entry) is refused: it could match no issue, and the abandoned work it
    /// was meant to exclude would count as delivered.
    /// </summary>
    internal static HashSet<string> ReadAbandonedResolutionIds(TelltaleConfig config, string path)
    {
        // The element type says "not null", but JSON can still carry a null entry.
        var ids = config.AbandonedResolutionIds.Select(id => id?.Trim() ?? "").ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => id.Length == 0 || !id.All(char.IsAsciiDigit)))
        {
            throw new UserFacingException(
                $"The configuration file {path} is not valid: abandonedResolutionIds: every entry must be a Jira resolution ID (digits only), and one is not.");
        }

        return ids;
    }

    /// <summary>
    /// The baseline sprint IDs, trimmed, each once. Jira's sprint IDs are digits, so anything else (a sprint's
    /// name, an empty entry) is refused. Whether each is a completed sprint of the board is checked against the
    /// store, by derive and report.
    /// </summary>
    internal static HashSet<string> ReadBaselineSprintIds(TelltaleConfig config, string path)
    {
        // The element type says "not null", but JSON can still carry a null entry.
        var ids = config.BaselineSprintIds.Select(id => id?.Trim() ?? "").ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => id.Length == 0 || !id.All(char.IsAsciiDigit)))
        {
            throw new UserFacingException(
                $"The configuration file {path} is not valid: baselineSprintIds: every entry must be a Jira sprint ID (digits only), and one is not.");
        }

        return ids;
    }

    internal static TimeZoneInfo ReadTimeZone(TelltaleConfig config, string path)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new UserFacingException($"The configuration file {path} is not valid: timeZone: '{config.TimeZone}' is not a known IANA time zone ID.", exception);
        }
    }
}
