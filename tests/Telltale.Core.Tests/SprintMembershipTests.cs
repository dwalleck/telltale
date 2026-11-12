using Telltale.Core.Intervals;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

public class SprintMembershipTests
{
    private static readonly DateTimeOffset Created = Local(2026, 10, 28, 9);

    [Fact]
    public void Sprint_ids_are_parsed_from_the_raw_field_value()
    {
        Assert.Equal(new[] { "41", "42" }, SprintMembership.ParseSprintIds("41, 42").Order());
        Assert.Empty(SprintMembership.ParseSprintIds(""));
        Assert.Empty(SprintMembership.ParseSprintIds(null));
    }

    [Fact]
    public void An_issue_with_no_sprint_changes_belongs_to_its_current_sprints_since_creation()
    {
        var membership = SprintMembership.Build(Created, new HashSet<string> { "41" }, []);

        var only = Assert.Single(membership.Intervals);
        Assert.Equal(new MembershipInterval("41", Created, null), only);
        Assert.Empty(membership.Anomalies);
    }

    [Fact]
    public void A_carried_over_issue_stays_listed_in_the_closed_sprint()
    {
        var addedTo41 = Local(2026, 11, 2, 9);
        var carriedTo42 = Local(2026, 11, 16, 9);

        var membership = SprintMembership.Build(Created, new HashSet<string> { "41", "42" },
        [
            SprintChange.FromRaw("1", addedTo41, "", "41"),
            SprintChange.FromRaw("2", carriedTo42, "41", "41, 42"),
        ]);

        Assert.Equal(new MembershipInterval("41", addedTo41, null), Assert.Single(membership.ForSprint("41")));
        Assert.Equal(new MembershipInterval("42", carriedTo42, null), Assert.Single(membership.ForSprint("42")));
        Assert.Empty(membership.Anomalies);
    }

    [Fact]
    public void Removing_an_issue_mid_sprint_ends_its_membership()
    {
        var added = Local(2026, 11, 2, 9);
        var removed = Local(2026, 11, 5, 14);

        var membership = SprintMembership.Build(Created, new HashSet<string>(),
        [
            SprintChange.FromRaw("1", added, null, "41"),
            SprintChange.FromRaw("2", removed, "41", null),
        ]);

        Assert.Equal(new MembershipInterval("41", added, removed), Assert.Single(membership.Intervals));
        Assert.Empty(membership.Anomalies);
    }

    [Fact]
    public void Ending_in_different_sprints_than_the_issue_is_reported()
    {
        var membership = SprintMembership.Build(Created, new HashSet<string> { "42" },
        [
            SprintChange.FromRaw("1", Local(2026, 11, 2, 9), "", "41"),
        ]);

        Assert.Contains("Sprint field is [42]", Assert.Single(membership.Anomalies));
    }
}
