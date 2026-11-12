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
    public async Task A_role_name_outside_the_four_roles_is_refused()
    {
        await Assert.That(() => StatusRoles.Parse(new Dictionary<string, string> { [New] = "finished" }))
            .Throws<FormatException>();
    }

    private static IssueOutcome Outcome(string currentStatusId, params StatusChange[] changes) =>
        Delivery.Of(StatusTimeline.Build(Created, currentStatusId, changes).Intervals, Roles);
}
