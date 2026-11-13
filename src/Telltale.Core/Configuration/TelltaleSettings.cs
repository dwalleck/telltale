using System.Globalization;
using Telltale.Core.Calendar;
using Telltale.Core.Derive;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Configuration;

/// <summary>A setting of the configuration that is refused. The message starts with the setting's name and never repeats a refused value, except a time zone (owner decision D43).</summary>
public sealed class SettingRefusedException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// The configuration, checked: every value here passed its rule, and each is in the form the code works
/// with. It can only be made by <see cref="From"/>, so whatever holds one need not check a setting again.
/// </summary>
public sealed record TelltaleSettings
{
    private TelltaleSettings()
    {
    }

    /// <summary>Where the team's work lives in Jira. The site address ends in a slash.</summary>
    public required JiraConfig Jira { get; init; }

    /// <summary>The role of every status, in board order.</summary>
    public required StatusRoles StatusRoles { get; init; }

    /// <summary>The IDs of the resolutions that mean an issue was abandoned.</summary>
    public required IReadOnlySet<string> AbandonedResolutionIds { get; init; }

    /// <summary>The IDs of the baseline sprints, each once. Whether each is a completed sprint is asked of the store.</summary>
    public required IReadOnlySet<string> BaselineSprintIds { get; init; }

    public required TimeZoneInfo TimeZone { get; init; }

    public required IReadOnlyList<DateOnly> Holidays { get; init; }

    /// <summary>The threshold of every status checked for brief visits (GLOSSARY: Brief visit), by status ID. Empty when nothing is checked.</summary>
    public required IReadOnlyDictionary<string, TimeSpan> BriefVisitThresholds { get; init; }

    /// <summary>The settings as a rebuild records them, apart from the raw data's digest: see <see cref="DerivedFrom"/>.</summary>
    private DerivedSource Recorded { get; init; } = new("", "", "", "", "", "", MinimumGroupSize.Default.Team, MinimumGroupSize.Default.AssigneesBehindAFigure, "{}");

    /// <summary>Everything a rebuild is decided with besides the raw data and the as-of time.</summary>
    public DerivationSettings Derivation => new(new DeliveryRules(StatusRoles, AbandonedResolutionIds), new BusinessCalendar(TimeZone, Holidays)) { BriefVisitThresholds = BriefVisitThresholds };

    /// <summary>What a rebuild of raw data with the digest <paramref name="rawDigest"/> under these settings is built from.</summary>
    public DerivedSource DerivedFrom(string rawDigest) => Recorded with { RawDigest = rawDigest };

    /// <summary>
    /// Checks every setting and throws <see cref="SettingRefusedException"/> for the first that is refused.
    /// Without <paramref name="requireDone"/>, a map in which no status has the done role is accepted; only
    /// <c>discover</c> asks for that (owner decision D40).
    /// </summary>
    public static TelltaleSettings From(TelltaleConfig config, bool requireDone = true)
    {
        // The file's reader refuses a null before this runs; a host that builds the configuration in code has
        // no reader, and "every value here passed its rule" must hold for it too.
        RequireNotNull(config.Jira, "jira");
        RequireNotNull(config.StatusRoles, "statusRoles");
        RequireNotNull(config.AbandonedResolutionIds, "abandonedResolutionIds");
        RequireNotNull(config.BaselineSprintIds, "baselineSprintIds");
        RequireNotNull(config.Holidays, "holidays");
        RequireNotNull(config.TimeZone, "timeZone");
        RequireNotNull(config.BriefVisitThresholds, "briefVisitThresholds");

        var jira = CheckedJira(config.Jira);
        var roles = CheckedStatusRoles(config.StatusRoles, requireDone);
        var abandonedResolutionIds = CheckedIds(config.AbandonedResolutionIds, "abandonedResolutionIds", "resolution");
        var baselineSprintIds = CheckedIds(config.BaselineSprintIds, "baselineSprintIds", "sprint");
        var timeZone = SettingRules.FindTimeZone(config.TimeZone)
            ?? throw new SettingRefusedException($"timeZone: '{config.TimeZone}' is not a known IANA time zone ID.");
        var minimumGroupSize = CheckedMinimumGroupSize(config.MinimumGroupSize ?? MinimumGroupSize.Default);
        var briefVisitThresholds = CheckedBriefVisitThresholds(config.BriefVisitThresholds, roles);

        return new TelltaleSettings
        {
            Jira = jira,
            StatusRoles = roles,
            AbandonedResolutionIds = abandonedResolutionIds,
            BaselineSprintIds = baselineSprintIds,
            TimeZone = timeZone,
            Holidays = config.Holidays,
            BriefVisitThresholds = briefVisitThresholds,
            Recorded = new DerivedSource(
                RawDigest: "",
                config.TimeZone,
                string.Join(",", config.Holidays.Order().Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
                DerivedSource.BaselineSprintIdsOf(baselineSprintIds),
                string.Join(",", config.StatusRoles.Select(status => $"{status.Key}={status.Value}")),
                string.Join(",", abandonedResolutionIds.Order(StringComparer.Ordinal)),
                minimumGroupSize.Team,
                minimumGroupSize.AssigneesBehindAFigure,
                DerivedSource.BriefVisitThresholdsOf(briefVisitThresholds.ToDictionary(listed => listed.Key, listed => listed.Value.TotalMinutes))),
        };
    }

    private static void RequireNotNull(object? value, string setting)
    {
        if (value is null)
        {
            throw new SettingRefusedException($"{setting} must not be null.");
        }
    }

    /// <summary>
    /// Each number is zero or more: zero turns its check off (owner decision D65), and a number below zero means
    /// nothing. Any two such numbers are accepted (owner decision D66): the team number is a minimum, not the
    /// size of the team, so a figure may need more assignees than it. Public for <c>discover</c>, which asks
    /// this of the numbers in an existing file before it calls Jira; throws <see cref="SettingRefusedException"/>.
    /// </summary>
    public static MinimumGroupSize CheckedMinimumGroupSize(MinimumGroupSize minimumGroupSize)
    {
        if (minimumGroupSize.Team < 0)
        {
            throw new SettingRefusedException(MinimumGroupSize.NumberRefused("team"));
        }

        if (minimumGroupSize.AssigneesBehindAFigure < 0)
        {
            throw new SettingRefusedException(MinimumGroupSize.NumberRefused("assigneesBehindAFigure"));
        }

        return minimumGroupSize;
    }

    /// <summary>What is said of a threshold of <c>briefVisitThresholds</c> that is not a number above zero. The file's reader says it too, of a value that is not a number at all.</summary>
    public const string BriefVisitThresholdRefused = "briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.";

    /// <summary>
    /// <c>briefVisitThresholds</c>: status ID to threshold in minutes (owner decisions D58, D59), as the length
    /// of time each threshold is. That length is the one form of a threshold from here on: derive applies it,
    /// and a rebuild records it, so the view and the report state the threshold that was applied. Every status
    /// listed must have a role in <paramref name="roles"/>, and not the done role: a done status has no time in
    /// status row, so its line could never appear. Every threshold must be above zero, and a length of time the
    /// tool can hold that is not nothing: one too large would stop the command, and under one that rounds to no
    /// time no visit is brief. Public for <c>discover</c>, which asks this of an existing file before it calls
    /// Jira, and passes no roles when the file has none yet. Throws <see cref="SettingRefusedException"/>; no
    /// message repeats a status ID or a threshold (owner decision D43).
    /// </summary>
    public static IReadOnlyDictionary<string, TimeSpan> CheckedBriefVisitThresholds(IReadOnlyDictionary<string, double> thresholds, StatusRoles? roles)
    {
        if (roles is not null)
        {
            foreach (var statusId in thresholds.Keys)
            {
                switch (roles.RoleOf(statusId))
                {
                    case null:
                        throw new SettingRefusedException("briefVisitThresholds: every status listed must be a status in statusRoles, and one is not.");
                    case DoneRole:
                        throw new SettingRefusedException("briefVisitThresholds: a status with the role done cannot be listed, and one is. A done status has no time in status row.");
                }
            }
        }

        return thresholds.ToDictionary(listed => listed.Key, listed => CheckedBriefVisitThreshold(listed.Value), StringComparer.Ordinal);
    }

    /// <summary>
    /// The length of time a threshold in minutes is, to the nearest step of time the tool measures in (a
    /// ten-millionth of a second). To the nearest, and not cut off, because a number with a fraction times the
    /// steps in a minute can fall a hair under the whole number of steps it stands for, and cut off it would
    /// be recorded and printed as another number than the owner wrote.
    /// </summary>
    private static TimeSpan CheckedBriefVisitThreshold(double minutes)
    {
        // Written this way round so that a value that is not a number (built in code; JSON has none) is refused too.
        if (!(minutes > 0))
        {
            throw new SettingRefusedException(BriefVisitThresholdRefused);
        }

        // Less than one step is no time the tool can tell from none, so it is not rounded up to a step.
        var steps = minutes * TimeSpan.TicksPerMinute;
        if (steps < 1)
        {
            throw new SettingRefusedException("briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too small: it rounds to no time at all.");
        }

        // Every number at or above this one is more steps than a length of time can hold; infinity is too.
        return steps < long.MaxValue
            ? TimeSpan.FromTicks((long)Math.Round(steps))
            : throw new SettingRefusedException("briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too large.");
    }

    private static JiraConfig CheckedJira(JiraConfig jira)
    {
        if (SettingRules.SiteUrlProblem(jira.SiteUrl) is { } siteUrlProblem)
        {
            throw new SettingRefusedException("jira.siteUrl" + siteUrlProblem);
        }

        if (SettingRules.BoardIdProblem(jira.BoardId) is { } boardIdProblem)
        {
            throw new SettingRefusedException("jira.boardId" + boardIdProblem);
        }

        if (string.IsNullOrWhiteSpace(jira.StoryPointFieldId))
        {
            throw new SettingRefusedException("jira.storyPointFieldId must not be empty.");
        }

        if (SettingRules.CustomFieldIdProblem(jira.StoryPointFieldId, "customfield_10016") is { } storyPointFieldProblem)
        {
            throw new SettingRefusedException("jira.storyPointFieldId" + storyPointFieldProblem);
        }

        if (string.IsNullOrWhiteSpace(jira.SprintFieldId))
        {
            throw new SettingRefusedException("jira.sprintFieldId must not be empty.");
        }

        if (SettingRules.CustomFieldIdProblem(jira.SprintFieldId, "customfield_10020") is { } sprintFieldProblem)
        {
            throw new SettingRefusedException("jira.sprintFieldId" + sprintFieldProblem);
        }

        // The collector stores each field's history under its own name, so they cannot be one field.
        if (jira.SprintFieldId == jira.StoryPointFieldId)
        {
            throw new SettingRefusedException("jira.sprintFieldId and jira.storyPointFieldId name the same field.");
        }

        if (jira.ProjectKeys is not { Count: > 0 })
        {
            throw new SettingRefusedException("jira.projectKeys must list at least one project key.");
        }

        try
        {
            JiraProjectKey.RequireValid(jira.ProjectKeys);
        }
        catch (FormatException exception)
        {
            throw new SettingRefusedException($"jira.projectKeys: {exception.Message}", exception);
        }

        return jira with { SiteUrl = SettingRules.WithTrailingSlash(jira.SiteUrl) };
    }

    /// <summary>Every status ID must be digits, as Jira's are, and every role one of the four.</summary>
    private static StatusRoles CheckedStatusRoles(OrderedDictionary<string, string> statusRoles, bool requireDone)
    {
        try
        {
            if (!statusRoles.Keys.All(SettingRules.IsJiraId))
            {
                throw new FormatException("every status ID must be a Jira status ID (digits only), and one is not.");
            }

            var roles = StatusRoles.Parse(statusRoles);
            if (requireDone && !roles.HasDone)
            {
                throw new FormatException("no status has the role 'done', so nothing could ever be delivered.");
            }

            return roles;
        }
        catch (FormatException exception)
        {
            throw new SettingRefusedException($"statusRoles: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// A list of Jira IDs, trimmed, each once. Jira's IDs are digits, so anything else (a name, a stray
    /// character, an empty entry) is refused: it could match nothing, and what it was meant to name (abandoned
    /// work to exclude, a baseline sprint) would be left out without a sign.
    /// </summary>
    private static HashSet<string> CheckedIds(IReadOnlyList<string> listed, string setting, string what)
    {
        // The element type says "not null", but JSON can still carry a null entry.
        var ids = listed.Select(id => id?.Trim() ?? "").ToHashSet(StringComparer.Ordinal);
        return ids.All(SettingRules.IsJiraId)
            ? ids
            : throw new SettingRefusedException($"{setting}: every entry must be a Jira {what} ID (digits only), and one is not.");
    }
}
