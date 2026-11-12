using System.Globalization;
using Telltale.Collectors.Jira;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Derive;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;
using Telltale.Reporting;
using Telltale.Storage.Sqlite;

namespace Telltale.Cli.Commands;

/// <summary>
/// The steps commands are made of, over one loaded configuration, one opened store and one as-of
/// time. <c>run</c> is sync, derive, report; a command for a single step calls just that one.
/// </summary>
internal sealed class PipelineSteps
{
    private readonly AppServices _services;
    private readonly TelltaleConfig _config;
    private readonly StatusRoles _roles;
    private readonly TimeZoneInfo _timeZone;
    private readonly ITelltaleStore _store;
    private readonly DateTimeOffset _asOf;

    private PipelineSteps(AppServices services, TelltaleConfig config, StatusRoles roles, TimeZoneInfo timeZone, ITelltaleStore store, DateTimeOffset asOf)
    {
        _services = services;
        _config = config;
        _roles = roles;
        _timeZone = timeZone;
        _store = store;
        _asOf = asOf;
    }

    /// <summary>Loads and checks the configuration, opens the store, and reads the clock once: the command's as-of time.</summary>
    public static async Task<PipelineSteps> StartAsync(AppServices services, CancellationToken cancellationToken)
    {
        var config = await ConfigFile.LoadAsync(services.ConfigPath, cancellationToken);
        var roles = ReadStatusRoles(config, services.ConfigPath);
        var timeZone = ReadTimeZone(config, services.ConfigPath);

        var store = new SqliteStore(services.DatabasePath);
        await store.InitializeAsync(cancellationToken);

        return new PipelineSteps(services, config, roles, timeZone, store, services.Clock.GetUtcNow());
    }

    /// <summary>Brings the raw tables up to date with Jira.</summary>
    public Task SyncAsync(CancellationToken cancellationToken) =>
        new JiraCollector(_services.HttpHandler, _config.Jira, _services.Secrets).BringUpToDateAsync(_store, cancellationToken);

    /// <summary>Rebuilds every derived table and metric view from the raw tables.</summary>
    public async Task DeriveAsync(CancellationToken cancellationToken)
    {
        var raw = await _store.ReadRawAsync(cancellationToken);
        await _store.ReplaceDerivedAsync(Derivation.Derive(raw, _roles, _asOf), cancellationToken);
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
            return StatusRoles.Parse(config.StatusRoles);
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
