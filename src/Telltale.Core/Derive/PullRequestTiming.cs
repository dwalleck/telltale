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

    /// <summary>The wait from opening to the first code approval (GLOSSARY: Time to first code approval).</summary>
    public const string FirstCodeApproval = "first_code_approval";

    /// <summary>The wait from opening to the merge.</summary>
    public const string Merge = "merge";

    /// <summary>The measures, in the order a report lists them.</summary>
    public static IReadOnlyList<string> Measures { get; } = [FirstCodeReview, FirstCodeApproval, Merge];

    /// <summary>
    /// Each host's rule for the event that ends the wait for the first code approval: null when the pull
    /// request has none that can be relied on. A host is added by its entry here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<RawPullRequest, IReadOnlyList<RawPullRequestEvent>, DateTimeOffset?>> FirstCodeApprovalBy =
        new Dictionary<string, Func<RawPullRequest, IReadOnlyList<RawPullRequestEvent>, DateTimeOffset?>>(StringComparer.Ordinal)
        {
            [PullRequestHosts.GitHub] = GitHubReviewRules.FirstCodeApproval,
            [PullRequestHosts.Ado] = AdoReviewRules.FirstCodeApproval,
        };

    /// <summary>
    /// Each host's rule for the event that ends the wait for the first code review: null when the pull request
    /// has none that can be relied on. A host is added by its entry here.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<RawPullRequest, IReadOnlyList<RawPullRequestEvent>, DateTimeOffset?>> FirstCodeReviewBy =
        new Dictionary<string, Func<RawPullRequest, IReadOnlyList<RawPullRequestEvent>, DateTimeOffset?>>(StringComparer.Ordinal)
        {
            [PullRequestHosts.GitHub] = GitHubReviewRules.FirstCodeReview,
            [PullRequestHosts.Ado] = AdoReviewRules.FirstCodeReview,
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
        var withoutReviews = HostsWithoutTheirReviews(raw, repositoriesByHost);
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
                if (RestsOnReviews(measure) && withoutReviews.Contains(seen.Host))
                {
                    continue;
                }

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

    /// <summary>
    /// The measures a rebuild gives rows for, in the order a report lists them: none when no host is
    /// configured, and otherwise all of them. Whether a host's rows of a measure can be relied on is not said
    /// by leaving the measure out but by <see cref="Coverage"/>.
    /// </summary>
    public static IReadOnlyList<string> MeasuresGiven(IReadOnlyDictionary<string, IReadOnlyList<string>> repositoriesByHost) =>
        repositoriesByHost.Count == 0 ? [] : Measures;

    /// <summary>
    /// What the store holds of each configured host for each measure, one row per host and measure, the hosts
    /// in the order they are configured: one of <see cref="PullRequestCoverageStates"/>. It is what lets a figure
    /// say that a host is missing from it, and what keeps a host that was not read from being read as a host
    /// with no pull requests. A host's last collection decides first (never read, or failed); for a host that
    /// was read, a measure that rests on reviews is not covered while the host is without them
    /// (<see cref="HostsWithoutTheirReviews"/>). A host that is not configured has no row: nothing is said of it.
    /// </summary>
    public static IReadOnlyList<PullRequestHostCoverage> Coverage(RawData raw, IReadOnlyDictionary<string, IReadOnlyList<string>> repositoriesByHost)
    {
        var states = raw.PullRequestHosts.ToDictionary(host => host.Host, host => host.State, StringComparer.Ordinal);
        var withoutReviews = HostsWithoutTheirReviews(raw, repositoriesByHost);
        return
        [
            .. repositoriesByHost.Keys.SelectMany((host, index) => Measures.Select(measure => new PullRequestHostCoverage(
                host,
                index + 1,
                measure,
                states.GetValueOrDefault(host) switch
                {
                    null => PullRequestCoverageStates.NotRead,
                    PullRequestHostStates.Read when RestsOnReviews(measure) && withoutReviews.Contains(host) => PullRequestCoverageStates.ReviewsNotRead,
                    PullRequestHostStates.Read => PullRequestCoverageStates.Read,
                    _ => PullRequestCoverageStates.FailedRead,
                }))),
        ];
    }

    /// <summary>
    /// The configured hosts whose reviews the store does not hold as far as its pull requests go: the last
    /// collection could not read the Ops group's members (<see cref="PullRequestRosterStates.Failed"/>), or a
    /// counted pull request's row was written without its events and they have not been read since
    /// (<see cref="PullRequestHistoryStates.NotRead"/>). For such a host no wait that rests on reviews is
    /// stated, in any sprint: a review that was not read can be the first of its pull request, so a figure
    /// from the reviews that were read would count some pull requests and silently leave out others. A merge
    /// is known from the pull request itself and is still counted.
    /// </summary>
    public static IReadOnlySet<string> HostsWithoutTheirReviews(RawData raw, IReadOnlyDictionary<string, IReadOnlyList<string>> repositoriesByHost) =>
        raw.PullRequestHosts.Where(host => host.Roster == PullRequestRosterStates.Failed).Select(host => host.Host)
            .Concat(raw.PullRequests
                .Where(pullRequest => pullRequest.History != PullRequestHistoryStates.Read
                    && repositoriesByHost.TryGetValue(pullRequest.Host, out var repositories)
                    && repositories.Contains(pullRequest.Repository, StringComparer.Ordinal))
                .Select(pullRequest => pullRequest.Host))
            .Where(repositoriesByHost.ContainsKey)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a measure's end is a review, and so needs the host's reviews to have been read and classified.</summary>
    private static bool RestsOnReviews(string measure) => measure is FirstCodeReview or FirstCodeApproval;

    /// <summary>The moment that ends the pull request's wait for <paramref name="measure"/>, or null when it has none.</summary>
    private static DateTimeOffset? EndOf(string measure, RawPullRequest pullRequest, IReadOnlyList<RawPullRequestEvent> events) => measure switch
    {
        FirstCodeReview => FirstCodeReviewBy.TryGetValue(pullRequest.Host, out var rule) ? rule(pullRequest, events) : null,
        FirstCodeApproval => FirstCodeApprovalBy.TryGetValue(pullRequest.Host, out var rule) ? rule(pullRequest, events) : null,

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
