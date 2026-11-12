using Telltale.Core.Intervals;
using Telltale.Core.Workflow;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

public class DeliveryTests
{
    private const string New = "1";
    private const string Development = "4";
    private const string Done = "8";
    private const string Shipped = "9";

    private static readonly StatusRoles Roles = StatusRoles.Parse(new Dictionary<string, string>
    {
        [New] = "not started",
        [Development] = "work",
        [Done] = "done",
        [Shipped] = "done",
    });

    private const string WontDo = "32";

    private static readonly HashSet<string> AbandonedResolutions = [WontDo];

    private static readonly DateTimeOffset Created = Local(2026, 11, 2, 9);

    [Test]
    public async Task An_issue_in_a_done_status_is_delivered_when_it_entered_done()
    {
        var outcome = Outcome(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(outcome.Value).IsEqualTo(new Delivered(Local(2026, 11, 5, 9)));
    }

    [Test]
    public async Task An_issue_not_in_a_done_status_is_not_delivered()
    {
        var outcome = Outcome(Development,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    [Test]
    public async Task A_move_between_done_statuses_does_not_move_the_delivery()
    {
        var outcome = Outcome(Shipped,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 20, 9), Done, Shipped));

        await Assert.That(outcome.Value).IsEqualTo(new Delivered(Local(2026, 11, 5, 9)));
    }

    [Test]
    public async Task A_reopened_issue_is_delivered_at_its_last_entry_into_done()
    {
        var outcome = Outcome(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 6, 9), Done, Development),
            new StatusChange("4", Local(2026, 11, 12, 9), Development, Done));

        await Assert.That(outcome.Value).IsEqualTo(new Delivered(Local(2026, 11, 12, 9)));
    }

    [Test]
    public async Task A_reopen_closed_again_at_the_same_instant_moves_the_delivery_to_that_instant()
    {
        // The visit to Development took no time, so it has no status interval; it is still a move out of done
        // and back in, and the delivery moment is the last move into done.
        var outcome = Outcome(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 12, 9), Done, Development),
            new StatusChange("4", Local(2026, 11, 12, 9), Development, Done));

        await Assert.That(outcome.Value).IsEqualTo(new Delivered(Local(2026, 11, 12, 9)));
    }

    [Test]
    public async Task An_issue_moved_into_done_at_the_instant_it_was_created_is_delivered_then()
    {
        var outcome = Outcome(Done,
            new StatusChange("1", Created, New, Done));

        await Assert.That(outcome.Value).IsEqualTo(new Delivered(Created));
    }

    [Test]
    public async Task A_reopened_issue_that_is_not_done_again_is_not_delivered()
    {
        var outcome = Outcome(Development,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 6, 9), Done, Development));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    [Test]
    public async Task A_status_with_no_configured_role_is_not_done()
    {
        var outcome = Outcome("unmapped",
            new StatusChange("1", Local(2026, 11, 3, 9), New, "unmapped"));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    [Test]
    public async Task A_role_name_outside_the_four_roles_is_refused_and_the_status_is_named_not_the_role()
    {
        await Assert.That(() => StatusRoles.Parse(new Dictionary<string, string> { [New] = "finished" }))
            .Throws<FormatException>().WithMessage($"the role of status {New} is not one of: not started, work, blocked, done.");
    }

    [Test]
    public async Task The_current_status_decides_when_the_history_ends_in_done_but_the_issue_is_elsewhere()
    {
        var outcome = Outcome(Development,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    [Test]
    public async Task An_issue_in_done_whose_history_ends_elsewhere_has_no_delivery_moment()
    {
        var outcome = Outcome(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    [Test]
    public async Task An_earlier_move_into_done_does_not_date_an_issue_whose_history_ends_elsewhere()
    {
        // Decision D8: the history says the issue left Done; only its current status says it is back.
        var outcome = Outcome(Done,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done),
            new StatusChange("3", Local(2026, 11, 6, 9), Done, Development));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    [Test]
    public async Task An_issue_that_has_only_ever_been_in_done_statuses_has_no_delivery_moment()
    {
        var outcome = Outcome(Shipped,
            new StatusChange("1", Local(2026, 11, 3, 9), Done, Shipped));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    [Test]
    public async Task An_issue_in_done_with_an_abandoned_resolution_is_abandoned_at_its_move_into_done()
    {
        var outcome = Outcome(Done, WontDo,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(outcome.Value).IsEqualTo(new Abandoned(Local(2026, 11, 5, 9)));
    }

    [Test]
    public async Task An_issue_in_done_with_a_resolution_that_is_not_in_the_abandoned_list_is_delivered()
    {
        var outcome = Outcome(Done, "31",
            new StatusChange("1", Local(2026, 11, 3, 9), New, Development),
            new StatusChange("2", Local(2026, 11, 5, 9), Development, Done));

        await Assert.That(outcome.Value).IsEqualTo(new Delivered(Local(2026, 11, 5, 9)));
    }

    [Test]
    public async Task An_abandoned_resolution_on_an_issue_that_is_not_done_changes_nothing()
    {
        // Reopened after being closed as won't-do, with the resolution left behind.
        var outcome = Outcome(Development, WontDo,
            new StatusChange("1", Local(2026, 11, 3, 9), New, Done),
            new StatusChange("2", Local(2026, 11, 5, 9), Done, Development));

        await Assert.That(outcome.Value).IsEqualTo(new NotDelivered());
    }

    private static IssueOutcome Outcome(string currentStatusId, params StatusChange[] changes) =>
        Outcome(currentStatusId, resolutionId: null, changes);

    private static IssueOutcome Outcome(string currentStatusId, string? resolutionId, params StatusChange[] changes) =>
        Delivery.Of(currentStatusId, resolutionId, StatusTimeline.Build(Created, currentStatusId, changes).Entries, Roles, AbandonedResolutions);
}
