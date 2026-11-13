using Telltale.Core.Configuration;

namespace Telltale.Core.Tests;

/// <summary>
/// <see cref="TelltaleSettings.From"/> is the one way to get checked settings, also for a host that builds the
/// configuration in code and not from the file, where the JSON reader refuses a null before the rules run.
/// </summary>
public class TelltaleSettingsTests
{
    [Test]
    public async Task A_complete_configuration_built_in_code_is_accepted()
    {
        var settings = TelltaleSettings.From(Config());

        await Assert.That(settings.StatusRoles.StatusIds).IsEquivalentTo(new[] { "1", "8" });
        await Assert.That(settings.Jira.SiteUrl.AbsoluteUri).IsEqualTo("https://example.invalid/");
    }

    [Test]
    [Arguments("jira", "jira must not be null.")]
    [Arguments("statusRoles", "statusRoles must not be null.")]
    [Arguments("abandonedResolutionIds", "abandonedResolutionIds must not be null.")]
    [Arguments("baselineSprintIds", "baselineSprintIds must not be null.")]
    [Arguments("holidays", "holidays must not be null.")]
    [Arguments("timeZone", "timeZone must not be null.")]
    [Arguments("jira.projectKeys", "jira.projectKeys must list at least one project key.")]
    [Arguments("jira.sprintFieldId", "jira.sprintFieldId must not be empty.")]
    [Arguments("jira.storyPointFieldId", "jira.storyPointFieldId must not be empty.")]
    [Arguments("jira.siteUrl", "jira.siteUrl must be an absolute https:// address.")]
    public async Task A_setting_that_is_null_is_refused_and_named(string setting, string message)
    {
        var config = Config();
        var withNull = setting switch
        {
            "jira" => config with { Jira = null! },
            "statusRoles" => config with { StatusRoles = null! },
            "abandonedResolutionIds" => config with { AbandonedResolutionIds = null! },
            "baselineSprintIds" => config with { BaselineSprintIds = null! },
            "holidays" => config with { Holidays = null! },
            "timeZone" => config with { TimeZone = null! },
            "jira.projectKeys" => config with { Jira = config.Jira with { ProjectKeys = null! } },
            "jira.sprintFieldId" => config with { Jira = config.Jira with { SprintFieldId = null! } },
            "jira.storyPointFieldId" => config with { Jira = config.Jira with { StoryPointFieldId = null! } },
            _ => config with { Jira = config.Jira with { SiteUrl = null! } },
        };

        var refusal = await Assert.That(() => TelltaleSettings.From(withNull)).Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo(message);
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
