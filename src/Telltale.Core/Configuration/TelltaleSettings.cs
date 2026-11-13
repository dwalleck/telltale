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

    /// <summary>The settings as a rebuild records them, apart from the raw data's digest: see <see cref="DerivedFrom"/>.</summary>
    private DerivedSource Recorded { get; init; } = new("", "", "", "", "", "", MinimumGroupSize.Default.Team, MinimumGroupSize.Default.AssigneesBehindAFigure);

    /// <summary>Everything a rebuild is decided with besides the raw data and the as-of time.</summary>
    public DerivationSettings Derivation => new(new DeliveryRules(StatusRoles, AbandonedResolutionIds), new BusinessCalendar(TimeZone, Holidays));

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

        var jira = CheckedJira(config.Jira);
        var roles = CheckedStatusRoles(config.StatusRoles, requireDone);
        var abandonedResolutionIds = CheckedIds(config.AbandonedResolutionIds, "abandonedResolutionIds", "resolution");
        var baselineSprintIds = CheckedIds(config.BaselineSprintIds, "baselineSprintIds", "sprint");
        var timeZone = SettingRules.FindTimeZone(config.TimeZone)
            ?? throw new SettingRefusedException($"timeZone: '{config.TimeZone}' is not a known IANA time zone ID.");
        var minimumGroupSize = CheckedMinimumGroupSize(config.MinimumGroupSize ?? MinimumGroupSize.Default);

        return new TelltaleSettings
        {
            Jira = jira,
            StatusRoles = roles,
            AbandonedResolutionIds = abandonedResolutionIds,
            BaselineSprintIds = baselineSprintIds,
            TimeZone = timeZone,
            Holidays = config.Holidays,
            Recorded = new DerivedSource(
                RawDigest: "",
                config.TimeZone,
                string.Join(",", config.Holidays.Order().Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
                DerivedSource.BaselineSprintIdsOf(baselineSprintIds),
                string.Join(",", config.StatusRoles.Select(status => $"{status.Key}={status.Value}")),
                string.Join(",", abandonedResolutionIds.Order(StringComparer.Ordinal)),
                minimumGroupSize.Team,
                minimumGroupSize.AssigneesBehindAFigure),
        };
    }

    private static void RequireNotNull(object? value, string setting)
    {
        if (value is null)
        {
            throw new SettingRefusedException($"{setting} must not be null.");
        }
    }

    /// <summary>Each number is zero or more: zero turns its check off (owner decision D65), and a number below zero means nothing.</summary>
    private static MinimumGroupSize CheckedMinimumGroupSize(MinimumGroupSize minimumGroupSize)
    {
        if (minimumGroupSize.Team < 0)
        {
            throw new SettingRefusedException("minimumGroupSize.team must be a whole number of zero or more.");
        }

        if (minimumGroupSize.AssigneesBehindAFigure < 0)
        {
            throw new SettingRefusedException("minimumGroupSize.assigneesBehindAFigure must be a whole number of zero or more.");
        }

        return minimumGroupSize;
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
