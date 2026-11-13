using Telltale.Core.Intervals;
using TUnit.Assertions.Enums;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Core.Tests;

public class SprintMembershipTests
{
    private static readonly DateTimeOffset Created = Local(2026, 10, 28, 9);

    [Test]
    public async Task Sprint_ids_are_parsed_from_the_raw_field_value()
    {
        await Assert.That(SprintMembership.ParseSprintIds("41, 42").Order()).IsEquivalentTo(new[] { "41", "42" }, CollectionOrdering.Matching);
        await Assert.That(SprintMembership.ParseSprintIds("")).IsEmpty();
        await Assert.That(SprintMembership.ParseSprintIds(null)).IsEmpty();
    }

    [Test]
    public async Task An_issue_with_no_sprint_changes_belongs_to_its_current_sprints_since_creation()
    {
        var membership = SprintMembership.Build(Created, new HashSet<string> { "41" }, []);

        await Assert.That(membership.Intervals).HasSingleItem();
        var only = membership.Intervals.Single();
        await Assert.That(only).IsEqualTo(new MembershipInterval("41", Created, null));
        await Assert.That(membership.Anomalies).IsEmpty();
    }

    [Test]
    public async Task A_carried_over_issue_stays_listed_in_the_closed_sprint()
    {
        var addedTo41 = Local(2026, 11, 2, 9);
        var carriedTo42 = Local(2026, 11, 16, 9);

        var membership = SprintMembership.Build(Created, new HashSet<string> { "41", "42" },
        [
            SprintChange.FromRaw("1", addedTo41, "", "41"),
            SprintChange.FromRaw("2", carriedTo42, "41", "41, 42"),
        ]);

        await Assert.That(membership.ForSprint("41")).HasSingleItem();
        await Assert.That(membership.ForSprint("41").Single()).IsEqualTo(new MembershipInterval("41", addedTo41, null));
        await Assert.That(membership.ForSprint("42")).HasSingleItem();
        await Assert.That(membership.ForSprint("42").Single()).IsEqualTo(new MembershipInterval("42", carriedTo42, null));
        await Assert.That(membership.Anomalies).IsEmpty();
    }

    [Test]
    public async Task Removing_an_issue_mid_sprint_ends_its_membership()
    {
        var added = Local(2026, 11, 2, 9);
        var removed = Local(2026, 11, 5, 14);

        var membership = SprintMembership.Build(Created, new HashSet<string>(),
        [
            SprintChange.FromRaw("1", added, null, "41"),
            SprintChange.FromRaw("2", removed, "41", null),
        ]);

        await Assert.That(membership.Intervals).HasSingleItem();
        await Assert.That(membership.Intervals.Single()).IsEqualTo(new MembershipInterval("41", added, removed));
        await Assert.That(membership.Anomalies).IsEmpty();
    }

    [Test]
    public async Task Ending_in_different_sprints_than_the_issue_is_reported()
    {
        var membership = SprintMembership.Build(Created, new HashSet<string> { "42" },
        [
            SprintChange.FromRaw("1", Local(2026, 11, 2, 9), "", "41"),
        ]);

        await Assert.That(membership.Anomalies).HasSingleItem();
        await Assert.That(membership.Anomalies.Single()).Contains("Sprint field is [42]");
    }
}
