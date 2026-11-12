namespace Telltale.Core.Configuration;

/// <summary>
/// The configuration file's content. It holds no secrets, so it can be committed and reviewed;
/// the Jira email, the API token and the hashing salt come from the secret store.
/// </summary>
public sealed record TelltaleConfig
{
    public required JiraConfig Jira { get; init; }

    /// <summary>The role of each workflow status by status ID: "not started", "work", "blocked" or "done".</summary>
    public required IReadOnlyDictionary<string, string> StatusRoles { get; init; }

    /// <summary>Resolution IDs that mean the issue was abandoned, not delivered.</summary>
    public IReadOnlyList<string> AbandonedResolutionIds { get; init; } = [];

    /// <summary>The team's time zone as an IANA ID, e.g. "America/Chicago".</summary>
    public required string TimeZone { get; init; }

    /// <summary>Company holidays: not business days.</summary>
    public IReadOnlyList<DateOnly> Holidays { get; init; } = [];

    /// <summary>The sprints that make up the baseline, fixed before the pilot starts.</summary>
    public IReadOnlyList<long> BaselineSprintIds { get; init; } = [];
}

/// <summary>Where the team's work lives in Jira.</summary>
public sealed record JiraConfig
{
    /// <summary>The site's base address, e.g. <c>https://example.atlassian.net/</c>.</summary>
    public required Uri SiteUrl { get; init; }

    /// <summary>The team's Scrum board.</summary>
    public required long BoardId { get; init; }

    /// <summary>The Sprint custom field, e.g. <c>customfield_10020</c>.</summary>
    public required string SprintFieldId { get; init; }

    /// <summary>The story point custom field, e.g. <c>customfield_10016</c>.</summary>
    public required string StoryPointFieldId { get; init; }

    /// <summary>The backfill fetches issues updated on or after this date.</summary>
    public required DateOnly BackfillStart { get; init; }
}
