using System.Globalization;
using Telltale.Collectors.Jira;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Derive;
using Telltale.Core.Identity;
using Telltale.Core.Secrets;
using Telltale.Core.Storage;
using Telltale.Reporting;

namespace Telltale.Pipeline;

/// <summary>
/// What a host gives the pipeline from the outside world, besides the settings and the store: the command
/// line builds it from its services, and another host (a scheduled job, a service) would build its own.
/// </summary>
/// <param name="HttpHandler">Carries every call to Jira.</param>
/// <param name="Clock">The source of "now": the as-of time of a rebuild, and the waits Jira asks for.</param>
/// <param name="Secrets">Where the hashing salt is read from by a command that does not fetch.</param>
/// <param name="Out">Where a step says what it did: the report it wrote, what a backfill replaced, a query's rows.</param>
/// <param name="Notices">Where a step says what the operator should know and no result depends on: changelog anomalies, a wait, a sprint without a window.</param>
/// <param name="ReportDirectory">Where reports are written, one file per sprint.</param>
public sealed record PipelineEnvironment(HttpMessageHandler HttpHandler, TimeProvider Clock, ISecretStore Secrets, TextWriter Out, TextWriter Notices, string ReportDirectory);

/// <summary>
/// The pipeline's steps and every rule about when one may run, over checked settings and an opened store:
/// collect (backfill or sync), derive, report, query. A host only chooses the steps: <c>run</c> is sync,
/// derive, report. The rules are here and not in a host: the as-of time, when derived data is stale, when
/// a store was filled under other settings or another salt, and which sprints the store covers. A refusal is
/// a <see cref="UserFacingException"/> that names what repairs it.
/// </summary>
public sealed class PipelineSteps
{
    private readonly PipelineEnvironment _environment;
    private readonly TelltaleSettings _settings;
    private readonly ITelltaleStore _store;
    private readonly CollectionSecrets? _secrets;
    private readonly Lazy<string?> _saltAtHand;
    private bool _withheldBaselineSprintsNamed;

    /// <param name="environment">The outside world.</param>
    /// <param name="settings">The checked configuration.</param>
    /// <param name="store">The store, initialised, and this pipeline's alone for as long as it is used.</param>
    /// <param name="secrets">The secrets a collection needs; null for a host that will only derive, report or query.</param>
    public PipelineSteps(PipelineEnvironment environment, TelltaleSettings settings, ITelltaleStore store, CollectionSecrets? secrets)
    {
        _environment = environment;
        _settings = settings;
        _store = store;
        _secrets = secrets;

        // Read at most once, whichever steps ask for it: a host that fetches has read it already.
        _saltAtHand = new Lazy<string?>(() => secrets?.HashingSalt ?? (environment.Secrets.Get(SecretName.HashingSalt) is { } salt && !string.IsNullOrWhiteSpace(salt) ? salt.Trim() : null));
    }

    /// <summary>
    /// Fetches everything from the configured start date and replaces the store's issues, changes and sprints
    /// with it, then says how many of each the store held before and holds now. A backfill removes what it
    /// did not fetch, so the counts are how the user sees one that replaced much with little.
    /// </summary>
    /// <param name="allowEmpty">Lets the backfill replace stored issues or sprints with none (<c>--allow-empty</c>).</param>
    public async Task BackfillAsync(bool allowEmpty, CancellationToken cancellationToken)
    {
        // The store is this pipeline's alone (the host sees to it), so nothing changes it between the counts and the write.
        var before = await _store.CountRawAsync(cancellationToken);
        await CollectAsync(CollectionMode.Backfill, allowEmpty, cancellationToken);
        var now = await _store.CountRawAsync(cancellationToken);
        await _environment.Out.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"The store now holds what Jira returned. Issues: {before.Issues} before, {now.Issues} now. Sprints: {before.Sprints} before, {now.Sprints} now. Changes: {before.Changes} before, {now.Changes} now."));
    }

    /// <summary>
    /// The whole pipeline: sync, derive, and the report of <paramref name="sprintId"/> or, without one, of the
    /// most recently completed sprint. When there is no sprint to report, the store has still been brought up
    /// to date, and that is said, not failed (owner decision D28).
    /// </summary>
    public async Task RunAsync(long? sprintId, CancellationToken cancellationToken)
    {
        await SyncAsync(cancellationToken);
        await DeriveAsync(cancellationToken);
        await ReportAsync(sprintId, needsSprint: false, cancellationToken);
    }

    /// <summary>Brings the raw tables up to date with what changed in Jira since the last sync or backfill.</summary>
    public Task SyncAsync(CancellationToken cancellationToken) => CollectAsync(CollectionMode.Incremental, allowEmpty: false, cancellationToken);

    private Task CollectAsync(CollectionMode mode, bool allowEmpty, CancellationToken cancellationToken) =>
        new JiraCollector(_environment.HttpHandler, _settings.Jira, _secrets ?? throw new InvalidOperationException("This command was started without the secrets a collection needs."), _environment.Clock, _environment.Notices)
            .BringUpToDateAsync(_store, mode, allowEmpty, cancellationToken);

    /// <summary>
    /// Rebuilds every derived table and metric view from the raw tables, and logs the changelog anomalies it
    /// found to standard error: one line with the totals, then one line for each anomaly (an issue and a kind)
    /// the previous rebuild did not have. The store's <c>anomaly</c> table holds the full list. A line holds the
    /// Jira issue ID and the kind's fixed message, never the key. Derive is the step that builds the timelines,
    /// so it is the one that logs.
    /// </summary>
    public async Task DeriveAsync(CancellationToken cancellationToken)
    {
        var completeFrom = await RequireFilledUnderTheConfigurationAsync(cancellationToken);
        await RequireStoredSaltAsync(cancellationToken);
        var raw = await _store.ReadRawAsync(cancellationToken);
        RequireBaselineSprints(raw, completeFrom);
        var derived = Derivation.Derive(raw, _settings.Derivation, AsOf(raw));
        var previous = await _store.ReadAnomaliesAsync(cancellationToken);
        var source = await SourceAsync(cancellationToken);
        await _store.ReplaceDerivedAsync(derived, source, cancellationToken);

        // Owner decision D51: such a sprint stops nothing. It is in no figure, and the console says so.
        foreach (var sprintId in derived.ClosedSprintsWithoutAnEnd)
        {
            await _environment.Notices.WriteLineAsync($"{SprintWindows.ClosedWithoutAnEnd(sprintId)}: no delivery is counted in it, and it cannot be reported or be a baseline sprint.");
        }

        await NameWithheldBaselineSprintsAsync(NumbersInForce.Of(source), cancellationToken);

        if (derived.Anomalies.Count == 0)
        {
            return;
        }

        // The totals and the per-issue lines are written whatever the report's footer does with its count:
        // the footer withholds it for a small store or too few assignees (three by default), and this log goes to the
        // operator, who holds the store and its per-issue derived tables anyway.

        var unseen = derived.Anomalies.Where(anomaly => !previous.Contains((anomaly.IssueId, anomaly.Message))).ToList();
        var issues = derived.Anomalies.Select(anomaly => anomaly.IssueId).Distinct(StringComparer.Ordinal).Count();
        await _environment.Notices.WriteLineAsync(
            $"Changelog anomalies: {derived.Anomalies.Count} across {issues} {(issues == 1 ? "issue" : "issues")}, {unseen.Count} new since the previous derive.");
        foreach (var anomaly in unseen)
        {
            await _environment.Notices.WriteLineAsync($"Changelog anomaly: issue {anomaly.IssueId}: {anomaly.Message}.");
        }
    }

    /// <summary>
    /// What a rebuild from the stored raw data under the current configuration is built from. The store
    /// keeps the digest of its raw data, so this reads one row and no raw row.
    /// </summary>
    private async Task<DerivedSource> SourceAsync(CancellationToken cancellationToken) =>
        _settings.DerivedFrom(await _store.ReadRawDigestAsync(cancellationToken) ?? "");

    /// <summary>
    /// Derive and report read what the store holds, and the store holds what the settings it was last filled
    /// under asked for. Under other settings (another site, board, projects, start date or history fields)
    /// its figures would be the old settings', so the command stops, as a sync does. A store nothing was ever
    /// fetched into has nothing of another setting's.
    /// Returns the moment from which the store holds every issue (the start date it was last filled from; null
    /// when it names none), so a step reads what the store was filled from once and passes this on.
    /// </summary>
    private async Task<DateTimeOffset?> RequireFilledUnderTheConfigurationAsync(CancellationToken cancellationToken)
    {
        if (await _store.ReadWatermarkAsync(JiraCollector.WatermarkName, cancellationToken) is not { } filled)
        {
            return null;
        }

        filled.Scope.RequireSameAs(JiraCollector.ScopeOf(_settings.Jira));
        return JiraCollector.CompleteFrom(filled.Scope);
    }

    /// <summary>
    /// Derive and report need no secret. But when the salt is at hand and is not the one the stored people
    /// were hashed with, the store is about to be filled under two salts, and the figures' distinct-assignee
    /// counts are not to be relied on until a backfill settles it: the command stops, as a sync would.
    /// </summary>
    private async Task RequireStoredSaltAsync(CancellationToken cancellationToken)
    {
        if (_saltAtHand.Value is { } salt)
        {
            IdentityHash.RequireSaltOf(await _store.ReadSaltFingerprintAsync(cancellationToken), salt);
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
        var now = _environment.Clock.GetUtcNow();
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
        var (completeFrom, derivedFrom) = await RequireCurrentDerivedDataAsync(cancellationToken);

        var sprint = sprintId?.ToString(CultureInfo.InvariantCulture)
            ?? await SprintReport.MostRecentlyCompletedSprintAsync(_store, cancellationToken);
        if (sprint is null)
        {
            if (needsSprint)
            {
                throw new UserFacingException("No sprint in the store has completed yet. Name a sprint with --sprint.");
            }

            await _environment.Out.WriteLineAsync("The store is up to date. No sprint has completed yet, so no report was written.");
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
        try
        {
            FetchedRange.RequireReportable(sprint, starts, completeFrom);
        }
        catch (UserFacingException refusal) when (sprintId is null && !needsSprint)
        {
            // The owner named no sprint and the store was updated: that the last completed sprint cannot be
            // reported is said, like "no sprint has completed" (owner decision D28), and is not a failure.
            await _environment.Out.WriteLineAsync($"The store is up to date. {refusal.Message}");
            return;
        }

        BaselineSprintRules.RequireWithinWhatWasFetched(starts, _settings.BaselineSprintIds, completeFrom);

        // The numbers the derived data was built under, from its record: read once, for the console and the report.
        var numbers = NumbersInForce.Of(derivedFrom);
        await NameWithheldBaselineSprintsAsync(numbers, cancellationToken);
        var path = await SprintReport.WriteAsync(_store, sprint, _settings.TimeZone, [.. _settings.BaselineSprintIds.Order(StringComparer.Ordinal)], numbers, _environment.ReportDirectory, cancellationToken);
        await _environment.Out.WriteLineAsync($"Wrote {path}");
    }

    /// <summary>
    /// Owner decisions D50, D56: a baseline sprint whose delivered count is withheld stops nothing. The views
    /// withhold every baseline figure, and the console says which sprint is why: when the derived data is
    /// rebuilt, and when a report is written from it. Once for one pipeline, so a run says it once.
    /// <paramref name="numbers"/> are the ones the derived data was built under.
    /// </summary>
    private async Task NameWithheldBaselineSprintsAsync(NumbersInForce numbers, CancellationToken cancellationToken)
    {
        if (_withheldBaselineSprintsNamed)
        {
            return;
        }

        _withheldBaselineSprintsNamed = true;
        var withheld = await BaselineSprintRules.ReadWithheldAsync(_store, cancellationToken);
        if (withheld.Count > 0)
        {
            await _environment.Notices.WriteLineAsync(BaselineSprintRules.WithheldOnTheConsole(withheld, numbers));
        }
    }

    /// <summary>
    /// Runs the owner's SQL over the metric views and prints the rows as a markdown table (owner decision
    /// D48). It is refused wherever a report would be: the views answer from the derived data as it is, so
    /// derived data that is missing, stale or from other settings would be passed off as current.
    /// </summary>
    public async Task QueryAsync(string sql, CancellationToken cancellationToken)
    {
        await RequireCurrentDerivedDataAsync(cancellationToken);
        try
        {
            await _environment.Out.WriteAsync(Markdown.Table(await _store.QueryViewRowsAsync(sql, cancellationToken)));
        }
        catch (InvalidOperationException refusal) when (refusal.InnerException is System.Data.Common.DbException)
        {
            // The guard's refusal: the SQL asked for something that is not a metric view.
            throw new UserFacingException(refusal.Message, refusal);
        }
    }

    /// <summary>
    /// What every command that reads the metric views asks first: the store was filled under the configured
    /// collection settings, its people are hashed with the salt at hand, and the derived data was built from
    /// the raw data and the settings as they are now. Each refusal names the command that repairs it.
    /// Returns the moment from which the store holds every issue (see <see cref="RequireFilledUnderTheConfigurationAsync"/>),
    /// and the record of what the derived data was built from, which then equals what a rebuild would record now.
    /// </summary>
    private async Task<(DateTimeOffset? CompleteFrom, DerivedSource DerivedFrom)> RequireCurrentDerivedDataAsync(CancellationToken cancellationToken)
    {
        var completeFrom = await RequireFilledUnderTheConfigurationAsync(cancellationToken);
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
            RequireBaselineSprints(await _store.ReadRawAsync(cancellationToken), completeFrom);
            throw new UserFacingException("baselineSprintIds in the configuration file changed since the derived data was built. Run derive first.");
        }

        // The views withhold by the numbers the last derive recorded (minimum_group_size). Under other numbers
        // they would show, or withhold, what the configuration no longer says. Deriving again is all it takes:
        // the numbers decide nothing about what is fetched.
        if (derivedFrom.MinimumTeam != current.MinimumTeam
            || derivedFrom.MinimumAssigneesBehindAFigure != current.MinimumAssigneesBehindAFigure)
        {
            throw new UserFacingException("minimumGroupSize in the configuration file changed since the derived data was built. Run derive first.");
        }

        // brief_visit and the view over it were built under the thresholds the last derive recorded. Deriving
        // again is all it takes: the thresholds decide nothing about what is fetched.
        if (derivedFrom.BriefVisitThresholds != current.BriefVisitThresholds)
        {
            throw new UserFacingException("briefVisitThresholds in the configuration file changed since the derived data was built. Run derive first.");
        }

        // Each check above compares the fields it knows and says which setting changed. This one compares the
        // two records whole, so that a field added later and forgotten above still stops the command: the
        // message is less exact, and stale derived data is never passed off as current.
        if (derivedFrom != current)
        {
            throw new UserFacingException("The configuration or the raw data changed since the derived data was built. Run derive first.");
        }

        return (completeFrom, derivedFrom);
    }

    /// <summary>
    /// What the raw data can say against the baseline sprint list: every baseline sprint is a completed sprint
    /// with a sprint window, and none started before the start date the store was last filled from. The smallest
    /// team (five by default) is the views' rule, and a sprint that fails it is not refused (owner decision D50).
    /// </summary>
    private void RequireBaselineSprints(RawData raw, DateTimeOffset? completeFrom)
    {
        BaselineSprintRules.RequireCompleted(raw.Sprints, _settings.BaselineSprintIds);
        BaselineSprintRules.RequireWindow(raw.Sprints, _settings.BaselineSprintIds);
        if (_settings.BaselineSprintIds.Count > 0)
        {
            BaselineSprintRules.RequireWithinWhatWasFetched(
                raw.Sprints.Where(sprint => sprint.Start is not null).Select(sprint => (sprint.SprintId, sprint.Start!.Value)),
                _settings.BaselineSprintIds,
                completeFrom);
        }
    }
}
