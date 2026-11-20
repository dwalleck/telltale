using Telltale.Core.Calendar;
using Telltale.Core.Intervals;
using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// The waits of pull requests: for each pull request of a configured repository, at most one wait per measure,
/// from the moment it was first opened to the event that ended the wait. A wait is counted once, in the sprint
/// whose window holds that end, and is never clipped to the window: one that began three sprints earlier counts
/// in full. A pull request that has no such event yet has no wait for the measure: not a wait of zero, and not
/// one to the current time.
/// </summary>
/// <remarks>
/// What ends a wait for review is a host's own rule, one file per host (<see cref="GitHubReviewRules"/>), found
/// here by the host's name. Which sprint an end belongs to and how long the wait was are decided here, for
/// every host alike. A measure is added by a line in <see cref="Measures"/> and one in <see cref="EndOf"/>:
/// the store's view and the report take whatever measures are listed.
/// </remarks>
public static class PullRequestTiming
{
    /// <summary>The wait from opening to the first code review (GLOSSARY: Time to first code review).</summary>
    public const string FirstCodeReview = "first_code_review";

    /// <summary>The wait from opening to the merge.</summary>
    public const string Merge = "merge";

    /// <summary>The measures, in the order a report lists them.</summary>
    public static IReadOnlyList<string> Measures { get; } = [FirstCodeReview, Merge];

    /// <summary>
    /// Each host's rule for the event that ends the wait for the first code review: null when the pull request
    /// has none that can be relied on. A host is added by its entry here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<RawPullRequest, IReadOnlyList<RawPullRequestEvent>, DateTimeOffset?>> FirstCodeReviewBy =
        new Dictionary<string, Func<RawPullRequest, IReadOnlyList<RawPullRequestEvent>, DateTimeOffset?>>(StringComparer.Ordinal)
        {
            [PullRequestHosts.GitHub] = GitHubReviewRules.FirstCodeReview,
        };

    /// <summary>
    /// The waits of the pull requests in <paramref name="raw"/> that belong to a repository listed in
    /// <paramref name="repositoriesByHost"/>, as of <paramref name="asOf"/>: nothing dated at or after it is
    /// counted in this rebuild, and a later one picks it up.
    /// </summary>
    public static IReadOnlyList<PullRequestWait> Waits(
        RawData raw,
        IReadOnlyDictionary<string, IReadOnlyList<string>> repositoriesByHost,
        IReadOnlyList<SprintWindow> windows,
        BusinessCalendar calendar,
        DateTimeOffset asOf)
    {
        var events = raw.PullRequestEvents
            .Where(happened => happened.At < asOf)
            .ToLookup(happened => (happened.Host, happened.Repository, happened.Number));
        var waits = new List<PullRequestWait>();
        foreach (var pullRequest in raw.PullRequests)
        {
            if (!repositoriesByHost.TryGetValue(pullRequest.Host, out var repositories)
                || !repositories.Contains(pullRequest.Repository, StringComparer.Ordinal)
                || pullRequest.Opened >= asOf)
            {
                continue;
            }

            // What the pull request is as of the as-of time: a merge or a close dated later has not happened yet.
            var seen = pullRequest with
            {
                Merged = pullRequest.Merged < asOf ? pullRequest.Merged : null,
                Closed = pullRequest.Closed < asOf ? pullRequest.Closed : null,
            };
            var itsEvents = events[(pullRequest.Host, pullRequest.Repository, pullRequest.Number)]
                .OrderBy(happened => happened.At)
                .ThenBy(happened => happened.EventId, StringComparer.Ordinal)
                .ToList();
            foreach (var measure in Measures)
            {
                // An end before the opening is no wait the tool can state: the measure has none.
                if (EndOf(measure, seen, itsEvents) is { } ended && ended >= seen.Opened)
                {
                    var wait = new Interval(seen.Opened, ended);
                    waits.Add(new PullRequestWait(
                        seen.Host, seen.Repository, seen.Number, measure, seen.Opened, ended,
                        ElapsedDays: wait.Duration / TimeSpan.FromDays(1),
                        BusinessDays: calendar.BusinessDays(wait),
                        SprintId: SprintOf(ended, windows, asOf)));
                }
            }
        }

        return waits;
    }

    /// <summary>The moment that ends the pull request's wait for <paramref name="measure"/>, or null when it has none.</summary>
    private static DateTimeOffset? EndOf(string measure, RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events) => measure switch
    {
        FirstCodeReview => FirstCodeReviewBy.TryGetValue(pullRequest.Host, out var rule) ? rule(pullRequest, events) : null,

        // The host's collector says what a merge is: a pull request closed without one has no merge time.
        Merge => pullRequest.Merged,
        _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, "Unknown measure."),
    };

    /// <summary>
    /// The sprint an end is counted in: the one whose window holds it, start included and end excluded. An end
    /// between windows, or in more than one window, is in none: it is kept unassigned and not guessed at. This
    /// is not the rule for an issue's delivery, which goes to the earliest-started of overlapping sprints.
    /// </summary>
    private static string? SprintOf(DateTimeOffset ended, IReadOnlyList<SprintWindow> windows, DateTimeOffset asOf) =>
        windows.Where(window => window.Resolve(asOf).Contains(ended)).Take(2).ToList() is [var only] ? only.SprintId : null;
}
