using System.Globalization;
using Telltale.Collectors.Jira;
using Telltale.Core;
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
    private readonly TimeZoneInfo _timeZone;
    private readonly ITelltaleStore _store;

    private PipelineSteps(AppServices services, TelltaleConfig config, StatusRoles roles, TimeZoneInfo timeZone, ITelltaleStore store)
    {
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
        var timeZone = ReadTimeZone(config, services.ConfigPath);

        if (needsJira)
        {
            foreach (var secret in Enum.GetValues<SecretName>())
            {
                services.Secrets.Require(secret);
            }
        }

        var store = new SqliteStore(services.DatabasePath);
        await store.InitializeAsync(cancellationToken);

        return new PipelineSteps(services, config, roles, timeZone, store);
    }

    /// <summary>Fetches everything from the configured start date into the raw tables.</summary>
    public Task BackfillAsync(CancellationToken cancellationToken) =>
        new JiraCollector(_services.HttpHandler, _config.Jira, _services.Secrets).BringUpToDateAsync(_store, CollectionMode.Backfill, cancellationToken);

    /// <summary>Brings the raw tables up to date with what changed in Jira since the last sync or backfill.</summary>
    public Task SyncAsync(CancellationToken cancellationToken) =>
        new JiraCollector(_services.HttpHandler, _config.Jira, _services.Secrets).BringUpToDateAsync(_store, CollectionMode.Incremental, cancellationToken);

    /// <summary>Rebuilds every derived table and metric view from the raw tables.</summary>
    public async Task DeriveAsync(CancellationToken cancellationToken)
    {
        var raw = await _store.ReadRawAsync(cancellationToken);
        await _store.ReplaceDerivedAsync(Derivation.Derive(raw, _config.Jira.BoardId, _roles, AsOf(raw)), cancellationToken);
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
