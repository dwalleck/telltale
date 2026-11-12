using System.Globalization;

namespace Telltale.Core.Storage;

/// <summary>
/// How far one collector has got. <see cref="Scope"/> is what it was asked to fetch; the watermark says
/// nothing about anything else, so a collector whose settings no longer match must not continue from it.
/// <see cref="UpdatedThrough"/> is the newest change fetched, in the source's own time (for Jira, an issue's
/// <c>updated</c>), never this machine's clock; null when nothing has been fetched yet.
/// </summary>
public sealed record Watermark(string Collector, CollectionScope Scope, DateTimeOffset? UpdatedThrough);

/// <summary>
/// What the raw tables were last filled from: the settings that decide which site, issues, sprints and
/// history a fetch asks for. The store keeps one column for each, and every rule about them is a comparison
/// of two of these records. A store filled before the columns existed gives empty values, which equal no
/// configured setting.
/// </summary>
/// <param name="SiteUrl">The Jira site's base address.</param>
/// <param name="BoardId">The board whose sprints were fetched.</param>
/// <param name="ProjectKeys">The project keys in ordinal order, separated by commas.</param>
/// <param name="StartDate">The date issues were fetched from (<c>jira.backfillStart</c>). Null when the store names none.</param>
/// <param name="HistoryFields">The IDs of the fields whose history was fetched, separated by commas.</param>
public sealed record CollectionScope(string SiteUrl, long BoardId, string ProjectKeys, DateOnly? StartDate, string HistoryFields)
{
    /// <summary>
    /// Stops when the store was filled under other settings than <paramref name="configured"/>: what it holds
    /// is then what the old settings asked for, and a sync, a rebuild or a report under the new ones would
    /// pass that off as theirs. Only a backfill makes the store the configured settings' again.
    /// </summary>
    public void RequireSameAs(CollectionScope configured)
    {
        if (this == configured)
        {
            return;
        }

        if (SiteUrl.Length == 0)
        {
            throw new UserFacingException(
                "The store does not record which Jira site, board, projects and start date it was last filled from: an earlier version of this tool filled it. Run backfill.");
        }

        List<string> changed =
        [
            .. SiteUrl != configured.SiteUrl ? ["jira.siteUrl"] : Array.Empty<string>(),
            .. BoardId != configured.BoardId ? ["jira.boardId"] : Array.Empty<string>(),
            .. ProjectKeys != configured.ProjectKeys ? ["jira.projectKeys"] : Array.Empty<string>(),
            .. StartDate != configured.StartDate ? ["jira.backfillStart"] : Array.Empty<string>(),
        ];
        if (changed.Count > 0)
        {
            var settings = changed.Count == 1 ? changed[0] : $"{string.Join(", ", changed.Take(changed.Count - 1))} and {changed[^1]}";
            throw new UserFacingException(
                $"{settings} in the configuration file changed since the store was last filled, so the store holds what the old {(changed.Count == 1 ? "setting" : "settings")} asked for. Run backfill.");
        }

        // An issue already stored comes back only when it changes in Jira, so every other issue would lack the
        // history of a newly named field, and an issue without history reads as one whose sprints or estimate
        // never changed.
        throw new UserFacingException(
            "The store was last filled without the history of the fields that jira.sprintFieldId and jira.storyPointFieldId name now, so sprint membership and point buckets could be wrong. Run backfill.");
    }

    /// <summary><see cref="StartDate"/> as the store keeps it: an ISO date, or null.</summary>
    public string? StoredStartDate => StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
