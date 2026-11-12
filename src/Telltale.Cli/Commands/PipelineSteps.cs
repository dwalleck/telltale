using System.Globalization;
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
internal sealed class PipelineSteps
{
    private readonly AppServices _services;
    private readonly TelltaleConfig _config;
    private readonly StatusRoles _roles;
    private readonly IReadOnlySet<string> _abandonedResolutionIds;
    private readonly TimeZoneInfo _timeZone;
    private readonly ITelltaleStore _store;

    private PipelineSteps(AppServices services, TelltaleConfig config, StatusRoles roles, IReadOnlySet<string> abandonedResolutionIds, TimeZoneInfo timeZone, ITelltaleStore store)
    {
        _abandonedResolutionIds = abandonedResolutionIds;
        _services = services;
        _config = config;
        _roles = roles;
        _timeZone = timeZone;
        _store = store;
    }

    /// <summary>
    /// Loads and checks the configuration and opens the store. With <paramref name="needsJira"/> (the command will
    /// sync), the secrets are checked first, so a run that cannot start leaves no empty database behind.
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

        var store = new SqliteStore(services.DatabasePath);
        await store.InitializeAsync(cancellationToken);

        return new PipelineSteps(services, config, roles, abandonedResolutionIds, timeZone, store);
    }

    /// <summary>Brings the raw tables up to date with Jira.</summary>
    public Task SyncAsync(CancellationToken cancellationToken) =>
        new JiraCollector(_services.HttpHandler, _config.Jira, _services.Secrets).BringUpToDateAsync(_store, cancellationToken);

    /// <summary>Rebuilds every derived table and metric view from the raw tables.</summary>
    public async Task DeriveAsync(CancellationToken cancellationToken)
    {
        var raw = await _store.ReadRawAsync(cancellationToken);
        await _store.ReplaceDerivedAsync(Derivation.Derive(raw, _config.Jira.BoardId, _roles, _abandonedResolutionIds, new BusinessCalendar(_timeZone, _config.Holidays), AsOf(raw)), cancellationToken);
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

    /// <summary>Writes one sprint's report and says where.</summary>
    public async Task ReportAsync(long sprintId, CancellationToken cancellationToken)
    {
        var path = await SprintReport.WriteAsync(_store, sprintId.ToString(CultureInfo.InvariantCulture), _timeZone, _services.ReportDirectory, cancellationToken);
        await _services.Out.WriteLineAsync($"Wrote {path}");
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

    private static StatusRoles ReadStatusRoles(TelltaleConfig config, string path)
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
    private static HashSet<string> ReadAbandonedResolutionIds(TelltaleConfig config, string path)
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

    private static TimeZoneInfo ReadTimeZone(TelltaleConfig config, string path)
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
