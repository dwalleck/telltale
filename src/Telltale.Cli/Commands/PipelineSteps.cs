using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Telltale.Collectors.Jira;
using Telltale.Core;
using Telltale.Core.Calendar;
using Telltale.Core.Configuration;
using Telltale.Core.Derive;
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
    private readonly TimeZoneInfo _timeZone;
    private readonly ITelltaleStore _store;
    private readonly IDisposable _storeLock;

    private PipelineSteps(AppServices services, TelltaleConfig config, StatusRoles roles, IReadOnlySet<string> abandonedResolutionIds, TimeZoneInfo timeZone, ITelltaleStore store, IDisposable storeLock)
    {
        _abandonedResolutionIds = abandonedResolutionIds;
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
            return new PipelineSteps(services, config, roles, abandonedResolutionIds, timeZone, store, storeLock);
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

    /// <summary>Rebuilds every derived table and metric view from the raw tables.</summary>
    public async Task DeriveAsync(CancellationToken cancellationToken)
    {
        var raw = await _store.ReadRawAsync(cancellationToken);
        await _store.ReplaceDerivedAsync(Derivation.Derive(raw, _config.Jira.BoardId, _roles, _abandonedResolutionIds, new BusinessCalendar(_timeZone, _config.Holidays), AsOf(raw)), DigestOf(raw), cancellationToken);
    }

    /// <summary>
    /// Identifies the raw data's content: equal raw data gives an equal digest, whenever and however often it
    /// was written. It covers every field of every raw row, so a field added to the raw records is covered
    /// without a change here. Rows come from the store in a fixed order.
    /// </summary>
    private static string DigestOf(RawData raw) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(raw)));

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
        var derivedFrom = await _store.ReadDerivedSourceAsync(cancellationToken)
            ?? throw new UserFacingException("The store has no derived data yet. Run derive first.");

        // The views would answer from the old derived tables without a sign, so the report checks first.
        var raw = await _store.ReadRawAsync(cancellationToken);
        if (derivedFrom != DigestOf(raw))
        {
            throw new UserFacingException("The raw data changed since the derived data was built. Run derive first.");
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

        await RequireSprintWithinWhatWasFetchedAsync(raw, sprint, cancellationToken);
        var path = await SprintReport.WriteAsync(_store, sprint, _timeZone, _services.ReportDirectory, cancellationToken);
        await _services.Out.WriteLineAsync($"Wrote {path}");
    }

    /// <summary>
    /// Refuses a sprint that started before the start date the store was last filled from. The store holds
    /// only issues updated since that date, so issues delivered in such a sprint and not touched since are
    /// missing, and the report would show too few deliveries without a sign.
    /// </summary>
    private async Task RequireSprintWithinWhatWasFetchedAsync(RawData raw, string sprintId, CancellationToken cancellationToken)
    {
        var filled = await _store.ReadWatermarkAsync(JiraCollector.WatermarkName, cancellationToken);
        if (filled is not null
            && JiraCollector.CompleteFrom(filled.Covers) is { } completeFrom
            && raw.Sprints.FirstOrDefault(sprint => sprint.SprintId == sprintId)?.Start < completeFrom)
        {
            throw new UserFacingException(
                $"Sprint {sprintId} started before {completeFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, the jira.backfillStart the store was last filled from, "
                + "so issues delivered in it can be missing and its figures would be too low. "
                + "No report was written. To report on it, set jira.backfillStart to a date before the sprint started and run backfill.");
        }
    }

    /// <summary>
    /// Stops with a message naming the first secret that is not set and how to set it. A missing hashing salt
    /// is two different situations: on a database that holds nobody yet, any salt will do and one can be
    /// generated; once people are stored under a salt, only that salt keeps each of them one person.
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
                    + $"A new salt would give every person a second hash, so each would count as two. Restore the original salt. {howToSet}");
            }

            // Only a store 'secrets set' can write to can be given a generated salt.
            var generate = services.Secrets is IWritableSecretStore ? " Add --generate to have a new random salt made." : string.Empty;
            throw new UserFacingException($"The secret {secret.Label} is not set in the secret store. {howToSet}{generate}");
        }
    }

    internal static StatusRoles ReadStatusRoles(TelltaleConfig config, string path)
    {
        try
        {
            var roles = StatusRoles.Parse(config.StatusRoles);
            if (!roles.HasDone)
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
