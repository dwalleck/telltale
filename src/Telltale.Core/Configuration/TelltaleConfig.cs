namespace Telltale.Core.Configuration;

/// <summary>
/// The configuration file's content. It holds no secrets: the Jira email, the API token and the
/// hashing salt come from the secret store. It does name the Jira site and its projects, so it is
/// kept out of this repository, which is public (<c>.gitignore</c> lists <c>/config/</c>). Every setting
/// here is read by something: a setting is added by the change that first uses it, so the file
/// never accepts a value that has no effect.
/// </summary>
public sealed record TelltaleConfig
{
    public required JiraConfig Jira { get; init; }

    /// <summary>
    /// The role of each workflow status by status ID: "not started", "work", "blocked" or "done".
    /// The order the statuses are listed in is board order: reports list statuses in it. The type keeps
    /// that order by contract, which a plain dictionary does not.
    /// </summary>
    public required OrderedDictionary<string, string> StatusRoles { get; init; }

    /// <summary>
    /// The IDs of the resolutions that mean an issue was abandoned (closed without being done, e.g. "Won't Do").
    /// An issue in a done status with one of them is not delivered. The list may be empty.
    /// </summary>
    public required IReadOnlyList<string> AbandonedResolutionIds { get; init; }

    /// <summary>The team's time zone as an IANA ID, e.g. "America/Chicago".</summary>
    public required string TimeZone { get; init; }

    /// <summary>
    /// The company holidays, as dates in the team's time zone, e.g. <c>"2026-01-19"</c>. A holiday is not a
    /// business day. The list is required, and may be empty (ADR-0001).
    /// </summary>
    public required IReadOnlyList<DateOnly> Holidays { get; init; }

    /// <summary>
    /// The IDs of the sprints the baseline is drawn from (GLOSSARY: Baseline), e.g. <c>"41"</c>: a list fixed before
    /// the evaluation starts. Reports show baseline figures pooled over these sprints. The setting may be left out
    /// or empty: then there is no baseline, and reports show none.
    /// </summary>
    public IReadOnlyList<string> BaselineSprintIds { get; init; } = [];

    /// <summary>
    /// The minimum group size's two numbers (GLOSSARY: Minimum group size), e.g. <c>{ "team": 5, "assigneesBehindAFigure": 3 }</c>.
    /// The setting may be left out: then the numbers are <see cref="Configuration.MinimumGroupSize.Default"/>.
    /// When it is present, both numbers are required.
    /// </summary>
    public MinimumGroupSize? MinimumGroupSize { get; init; }

    /// <summary>
    /// The statuses checked for brief visits (GLOSSARY: Brief visit), by status ID, each with its threshold: the
    /// shortest believable stay in minutes of clock time, e.g. <c>{ "10001": 5 }</c> (owner decisions D58, D59).
    /// A status not listed is not checked. The setting may be left out or empty: then nothing is checked.
    /// </summary>
    public IReadOnlyDictionary<string, double> BriefVisitThresholds { get; init; } = new Dictionary<string, double>();
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

    /// <summary>The Sprint custom field, e.g. <c>customfield_10020</c>. Its value and history say which sprints an issue belonged to, and when.</summary>
    public required string SprintFieldId { get; init; }

    /// <summary>The story point custom field, e.g. <c>customfield_10016</c>.</summary>
    public required string StoryPointFieldId { get; init; }

    /// <summary>The backfill fetches issues updated on or after this date.</summary>
    public required DateOnly BackfillStart { get; init; }
}
