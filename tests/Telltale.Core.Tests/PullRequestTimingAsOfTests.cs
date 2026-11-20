using Telltale.Core.Derive;
using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

/// <summary>
/// The as-of time of the pull request waits is half-open: nothing dated at or after it is counted. A run of
/// the tool no longer meets that edge, because its as-of time is set just past the newest thing a host
/// reported (review 2, finding 13 of the PR timing spec); the derivation still keeps the rule for whatever
/// as-of time it is given, and this is where it can be seen.
/// </summary>
public class PullRequestTimingAsOfTests
{
    private const string Repository = "fake-org/fake-sample";

    private static readonly DateTimeOffset Opened = Local(2026, 1, 20, 12);
    private static readonly DateTimeOffset Reviewed = Local(2026, 1, 21, 12);
    private static readonly DateTimeOffset Merged = Local(2026, 1, 21, 18);

    /// <summary>One pull request, opened on Tuesday, reviewed by a person a day later and merged six hours after that.</summary>
    private static readonly RawData Raw = new([], [], [], [])
    {
        PullRequests =
        [
            new RawPullRequest { Host = PullRequestHosts.GitHub, Repository = Repository, Number = 1, Opened = Opened, Merged = Merged, Closed = Merged, Updated = Merged, AuthorHash = null },
        ],
        PullRequestEvents =
        [
            new RawPullRequestEvent
            {
                Host = PullRequestHosts.GitHub, Repository = Repository, Number = 1, Kind = PullRequestEventKinds.Review, EventId = "1",
                Outcome = PullRequestEventOutcomes.Commented, Actor = PullRequestEventActors.Human, At = Reviewed,
            },
        ],
        PullRequestHosts = [new RawPullRequestHost(PullRequestHosts.GitHub, PullRequestHostStates.Read, PullRequestRosterStates.NotConfigured)],
    };

    private static string[] WaitsAsOf(DateTimeOffset asOf) =>
    [
        .. PullRequestTiming.Waits(
                Raw,
                new Dictionary<string, IReadOnlyList<string>> { [PullRequestHosts.GitHub] = [Repository] },
                [new SprintWindow("42", Local(2026, 1, 19, 10, 30), Completed: null)],
                TestTime.TeamCalendar(),
                asOf)
            .Select(wait => $"{wait.Measure} in {wait.SprintId}"),
    ];

    [Test]
    public async Task An_event_dated_at_exactly_the_as_of_time_is_not_counted()
    {
        await Assert.That(WaitsAsOf(Reviewed)).IsEmpty();
        await Assert.That(WaitsAsOf(Merged)).IsEquivalentTo(new[] { "first_code_review in 42" });
    }

    [Test]
    public async Task An_event_dated_a_millisecond_before_the_as_of_time_is_counted()
    {
        await Assert.That(WaitsAsOf(Reviewed.AddMilliseconds(1))).IsEquivalentTo(new[] { "first_code_review in 42" });
        await Assert.That(WaitsAsOf(Merged.AddMilliseconds(1))).IsEquivalentTo(new[] { "first_code_review in 42", "merge in 42" });
    }

    [Test]
    public async Task A_pull_request_opened_at_exactly_the_as_of_time_is_not_looked_at()
    {
        await Assert.That(WaitsAsOf(Opened)).IsEmpty();
    }
}
