using Telltale.Core.Configuration;

namespace Telltale.Core.Tests;

/// <summary>
/// <c>briefVisitThresholds</c> in a configuration built in code, where no file reader has refused anything
/// first: <see cref="TelltaleSettings.From"/> must refuse what the reader would, and also what JSON cannot
/// say (a null dictionary, a value that is not a number, an infinite one).
/// </summary>
public class BriefVisitThresholdsSettingTests
{
    [Test]
    public async Task Thresholds_in_minutes_are_checked_into_lengths_of_time_by_status()
    {
        var settings = TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = 5, ["1"] = 0.5 } });

        await Assert.That(settings.BriefVisitThresholds.Count).IsEqualTo(2);
        await Assert.That(settings.BriefVisitThresholds["4"]).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(settings.BriefVisitThresholds["1"]).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task A_null_in_place_of_the_thresholds_is_refused_and_named()
    {
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitThresholds = null! })).Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo("briefVisitThresholds must not be null.");
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.NegativeInfinity)]
    [Arguments(0d)]
    public async Task A_threshold_that_is_not_a_number_above_zero_is_refused(double minutes)
    {
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = minutes } }))
            .Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo("briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.");
    }

    [Test]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.MaxValue)]
    public async Task A_threshold_longer_than_any_length_of_time_the_tool_can_hold_is_refused(double minutes)
    {
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = minutes } }))
            .Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo("briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too large.");
    }

    [Test]
    public async Task The_shortest_time_the_tool_can_hold_is_accepted_and_half_of_it_is_refused()
    {
        // The smallest step of time is a ten-millionth of a second: in minutes, one six-hundred-millionth.
        const double OneStep = 1.0 / 600_000_000;

        var settings = TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = OneStep } });
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = OneStep / 2 } }))
            .Throws<SettingRefusedException>();

        await Assert.That(settings.BriefVisitThresholds["4"]).IsEqualTo(TimeSpan.FromTicks(1));
        await Assert.That(refusal!.Message).IsEqualTo("briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too small: it rounds to no time at all.");
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
        StatusRoles = new OrderedDictionary<string, string> { ["1"] = "not started", ["4"] = "work", ["8"] = "done" },
        AbandonedResolutionIds = [],
        TimeZone = "America/Chicago",
        Holidays = [],
    };
}
