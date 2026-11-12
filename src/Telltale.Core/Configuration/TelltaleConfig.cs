namespace Telltale.Core.Configuration;

/// <summary>
/// The configuration file's content. It holds no secrets, so it can be committed and reviewed;
/// the Jira email, the API token and the hashing salt come from the secret store. Every setting
/// here is read by something: a setting is added by the change that first uses it, so the file
/// never accepts a value that has no effect.
/// </summary>
public sealed record TelltaleConfig
{
    public required JiraConfig Jira { get; init; }

    /// <summary>The role of each workflow status by status ID: "not started", "work", "blocked" or "done".</summary>
    public required IReadOnlyDictionary<string, string> StatusRoles { get; init; }

    /// <summary>The team's time zone as an IANA ID, e.g. "America/Chicago".</summary>
    public required string TimeZone { get; init; }

    /// <summary>
    /// Company holidays as dates (<c>"2026-01-19"</c>), read in the team's time zone. Together with weekends
    /// they are the days that are not business days. An empty list means weekends only.
    /// </summary>
    public required IReadOnlyList<DateOnly> Holidays { get; init; }
}

/// <summary>Where the team's work lives in Jira.</summary>
public sealed record JiraConfig
{
    /// <summary>The site's base address, e.g. <c>https://example.atlassian.net/</c>.</summary>
    public required Uri SiteUrl { get; init; }

    /// <summary>The team's Scrum board.</summary>
    public required long BoardId { get; init; }

    /// <summary>The keys of the board's projects, e.g. <c>PAY</c>. Issues are searched in these projects only.</summary>
    public required IReadOnlyList<string> ProjectKeys { get; init; }

    /// <summary>The story point custom field, e.g. <c>customfield_10016</c>.</summary>
    public required string StoryPointFieldId { get; init; }

    /// <summary>The backfill fetches issues updated on or after this date.</summary>
    public required DateOnly BackfillStart { get; init; }
}
