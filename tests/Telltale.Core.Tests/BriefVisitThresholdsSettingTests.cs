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
    [Arguments(5, "5")]
    [Arguments(0.5, "0.5")]
    [Arguments(2.5, "2.5")]
    [Arguments(0.1, "0.1")]
    [Arguments(0.7, "0.7")]
    [Arguments(1.1, "1.1")]
    [Arguments(2.2, "2.2")]
    [Arguments(0.05, "0.05")]
    [Arguments(0.29, "0.29")]
    [Arguments(7.3, "7.3")]
    [Arguments(1440, "1440")]
    public async Task A_rebuild_records_a_threshold_as_the_owner_wrote_it(double minutes, string written)
    {
        // The record is what the view reads and the report prints, so this is how the footer states the threshold.
        var recorded = TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = minutes } }).DerivedFrom("synthetic-digest");

        await Assert.That(recorded.BriefVisitThresholds).IsEqualTo($$"""{"4":{{written}}}""");
    }

    [Test]
    public async Task A_rebuild_records_the_length_of_time_that_is_applied_so_two_numbers_that_are_one_length_of_time_are_one_record()
    {
        // The second number differs from five minutes by far less than the smallest step of time the tool measures.
        var five = TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = 5 } });
        var almostFive = TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = 5.0000000000001 } });

        await Assert.That(almostFive.BriefVisitThresholds["4"]).IsEqualTo(five.BriefVisitThresholds["4"]);
        await Assert.That(almostFive.DerivedFrom("synthetic-digest").BriefVisitThresholds).IsEqualTo("""{"4":5}""");
        await Assert.That(almostFive.DerivedFrom("synthetic-digest")).IsEqualTo(five.DerivedFrom("synthetic-digest"));
    }

    [Test]
    public async Task A_threshold_finer_than_the_smallest_step_of_time_is_recorded_as_the_whole_steps_that_are_applied()
    {
        // 1.7 thousand-millionths of a minute is 1.02 steps of a ten-millionth of a second: one step is applied, and one step is recorded.
        var settings = TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = 1.7e-9 } });

        await Assert.That(settings.BriefVisitThresholds["4"]).IsEqualTo(TimeSpan.FromTicks(1));
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitThresholds).IsEqualTo("""{"4":1.6666666666666667E-09}""");
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
    public async Task The_shortest_time_the_tool_can_hold_is_accepted_and_a_quarter_of_it_is_refused()
    {
        // The smallest step of time is a ten-millionth of a second: in minutes, one six-hundred-millionth.
        const double OneStep = 1.0 / 600_000_000;

        var settings = TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = OneStep } });
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitThresholds = new Dictionary<string, double> { ["4"] = OneStep / 4 } }))
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
