using System.Globalization;
using System.Text.Json;
using Telltale.Core;
using static Telltale.Collectors.Jira.JiraJson;

namespace Telltale.Collectors.Jira;

/// <summary>How <see cref="OffsetPages"/> decides that a list has ended.</summary>
internal enum OffsetPaging
{
    /// <summary>
    /// For lists a person reads and corrects (<c>discover</c>): the list ends where Jira says so, and in any
    /// case at the first page that brings nothing new.
    /// </summary>
    EndsWhenNothingNew,

    /// <summary>
    /// For lists the figures are built on (the board's sprints): every sign that an entry could be missing
    /// stops the run. See <see cref="OffsetPages.ReadAllAsync"/>.
    /// </summary>
    StopsWhenInDoubt,
}

/// <summary>One page of an offset-paged list as Jira's envelope describes it. Jira may leave out all three of the last values.</summary>
internal sealed record OffsetPage<T>(List<T> Values, bool? IsLast, long? Total, long? StartAt);

/// <summary>
/// Reads the lists Jira pages by offset (the Agile API's sprints and projects, the resolution search): each
/// request names the position to start at, which is the number of entries received so far.
/// </summary>
internal static class OffsetPages
{
    /// <summary>
    /// Reads every page. Each entry is kept once, by <paramref name="keyOf"/>. The loop always ends: a page
    /// that brings nothing new ends the list or stops the run.
    /// </summary>
    /// <remarks>
    /// Under <see cref="OffsetPaging.StopsWhenInDoubt"/> a page is the last when <c>isLast</c> says so; without
    /// <c>isLast</c>, when the position reaches <c>total</c>; with neither, when it is empty. The run stops
    /// when a page does not start where it was asked to, when <c>isLast</c> and <c>total</c> disagree, when a
    /// page that is not the last brings nothing new, and when the list ends with another number of entries
    /// than <c>total</c>: the list then changed while it was read, and an entry can have slipped between two pages.
    /// </remarks>
    /// <param name="pageSize">The <c>maxResults</c> to ask for, or null to leave it to Jira.</param>
    /// <param name="read">The caller's wrapper that turns an answer in an unknown shape into its plain message.</param>
    /// <param name="nothingKept">The sentence that ends a stop's message, e.g. "Nothing was stored."</param>
    public static async Task<List<T>> ReadAllAsync<T>(
        JiraClient jira,
        string path,
        int? pageSize,
        OffsetPaging paging,
        string what,
        string nothingKept,
        Func<Func<OffsetPage<T>>, OffsetPage<T>> read,
        Func<JsonElement, T> parse,
        Func<T, string> keyOf,
        CancellationToken cancellationToken)
    {
        var strict = paging == OffsetPaging.StopsWhenInDoubt;
        var all = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long received = 0;
        while (true)
        {
            var startAt = received;
            var maxResults = pageSize is { } size ? $"&maxResults={size.ToString(CultureInfo.InvariantCulture)}" : "";
            using var document = await jira.GetAsync($"{path}?startAt={startAt.ToString(CultureInfo.InvariantCulture)}{maxResults}", cancellationToken);
            var page = read(() =>
            {
                var root = document.RootElement;
                return new OffsetPage<T>(
                    [.. root.GetProperty("values").EnumerateArray().Select(parse)],
                    IsLast(root, strict),
                    Optional(root, "total")?.GetInt64(),
                    Optional(root, "startAt")?.GetInt64());
            });

            received += page.Values.Count;
            var fresh = page.Values.Where(value => seen.Add(keyOf(value))).ToList();
            all.AddRange(fresh);

            if (!strict)
            {
                var isLast = page.IsLast == true;
                var total = page.Total;
                if (isLast || fresh.Count == 0 || received >= total)
                {
                    return all;
                }

                continue;
            }

            if (page.StartAt is { } startsAt && startsAt != startAt)
            {
                throw new UserFacingException($"Jira's {what} gave a page that does not start where it was asked to, so entries could be missing. {nothingKept}");
            }

            bool? reachesTotal = page.Total is { } count ? received >= count : null;
            if (page.IsLast is { } last && reachesTotal is { } reached && last != reached)
            {
                throw new UserFacingException($"Jira's {what} gave a page whose isLast and total disagree on whether more follow, so entries could be missing. {nothingKept}");
            }

            if (page.IsLast ?? reachesTotal ?? page.Values.Count == 0)
            {
                if (page.Total is { } expected && seen.Count != expected)
                {
                    throw new UserFacingException($"Jira's {what} changed while it was being read, so entries could be missing. {nothingKept} Run the command again.");
                }

                return all;
            }

            if (page.Values.Count == 0)
            {
                throw new UserFacingException($"Jira's {what} gave an empty page and says more follow, so entries could be missing. {nothingKept}");
            }

            // A site that answers every position with the same page would otherwise be asked forever.
            if (fresh.Count == 0)
            {
                throw new UserFacingException($"Jira's {what} gave a page holding only entries it had already given, so its pages would never end. {nothingKept}");
            }
        }
    }

    /// <summary>Jira's <c>isLast</c>. One that is not a boolean is an unknown shape where doubt stops the run, and is passed over where it does not.</summary>
    private static bool? IsLast(JsonElement page, bool strict) => Optional(page, "isLast")?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        null => null,
        _ => strict ? throw new FormatException("isLast is not a boolean.") : null,
    };
}
