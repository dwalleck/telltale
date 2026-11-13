using Telltale.Core.Configuration;

namespace Telltale.Core.Tests;

/// <summary>
/// The rule on the minimum group size's two numbers taken together (owner decision D66): a figure cannot need
/// more assignees than a team that is checked has, and with no team check any number is allowed.
/// </summary>
public class MinimumGroupSizeRulesTests
{
    [Test]
    [Arguments(5, 6)]
    [Arguments(1, 2)]
    [Arguments(3, 40)]
    public async Task More_assignees_behind_a_figure_than_a_team_that_is_not_zero_is_refused_by_name(int team, int assigneesBehindAFigure)
    {
        var config = Config() with { MinimumGroupSize = new MinimumGroupSize { Team = team, AssigneesBehindAFigure = assigneesBehindAFigure } };

        var refusal = await Assert.That(() => TelltaleSettings.From(config)).Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo("minimumGroupSize.assigneesBehindAFigure must not be greater than minimumGroupSize.team, unless team is zero.");
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(0, 1)]
    [Arguments(0, 40)]
    [Arguments(1, 0)]
    [Arguments(1, 1)]
    [Arguments(4, 4)]
    [Arguments(40, 3)]
    public async Task A_pair_with_no_more_assignees_behind_a_figure_than_the_team_or_with_a_team_of_zero_is_recorded_as_written(int team, int assigneesBehindAFigure)
    {
        var config = Config() with { MinimumGroupSize = new MinimumGroupSize { Team = team, AssigneesBehindAFigure = assigneesBehindAFigure } };

        var recorded = TelltaleSettings.From(config).DerivedFrom("synthetic-digest");

        await Assert.That((recorded.MinimumTeam, recorded.MinimumAssigneesBehindAFigure)).IsEqualTo(((long)team, (long)assigneesBehindAFigure));
    }

    private static TelltaleConfig Config() => new()
    {
        Jira = new JiraConfig
        {
            SiteUrl = new Uri("https://example.invalid"),
            BoardId = 400001,
            ProjectKeys = ["FAKE"],
            SprintFieldId = "customfield_10020",
            StoryPointFieldId = "customfield_10016",
            BackfillStart = new DateOnly(2025, 1, 1),
        },
        StatusRoles = new OrderedDictionary<string, string> { ["1"] = "not started", ["8"] = "done" },
        AbandonedResolutionIds = [],
        TimeZone = "America/Chicago",
        Holidays = [],
    };
}
