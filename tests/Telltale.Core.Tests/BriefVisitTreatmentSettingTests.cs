using Telltale.Core.Configuration;
using Telltale.Core.Intervals;

namespace Telltale.Core.Tests;

/// <summary>
/// <c>briefVisitTreatment</c> in a configuration built in code, where no file reader has refused anything
/// first (GLOSSARY: Treatment; owner decision D83): what <see cref="TelltaleSettings.From"/> accepts and
/// refuses, what derive is given, and what a rebuild records.
/// </summary>
public class BriefVisitTreatmentSettingTests
{
    private static readonly Dictionary<string, double> DevelopmentListed = new() { ["4"] = 5 };

    [Test]
    public async Task Left_out_the_treatment_is_count_for_derive_and_in_the_record()
    {
        var settings = TelltaleSettings.From(Config() with { BriefVisitThresholds = DevelopmentListed });

        await Assert.That(settings.Derivation.TimeInStatusTreatment.Value).IsTypeOf<CountBriefVisits>();
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitTreatment).IsEqualTo("""{"timeInStatus":"count"}""");
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitsLeftOutOfTimeInStatus).IsFalse();
    }

    [Test]
    public async Task Exclude_for_time_in_status_is_what_derive_is_given_and_what_a_rebuild_records()
    {
        var settings = TelltaleSettings.From(Config() with
        {
            BriefVisitThresholds = DevelopmentListed,
            BriefVisitTreatment = new BriefVisitTreatmentSetting { TimeInStatus = new ExcludeBriefVisits() },
        });

        await Assert.That(settings.Derivation.TimeInStatusTreatment.Value).IsTypeOf<ExcludeBriefVisits>();
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitTreatment).IsEqualTo("""{"timeInStatus":"exclude"}""");
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitsLeftOutOfTimeInStatus).IsTrue();
    }

    [Test]
    public async Task A_rebuild_records_the_treatment_derive_applies_also_for_settings_copied_with_another_treatment()
    {
        // The record is made from the treatment that is applied, so the two cannot differ: a report never
        // passes for current under another treatment, and never states one that was not applied.
        var settings = TelltaleSettings.From(Config() with { BriefVisitThresholds = DevelopmentListed });

        var copied = settings with { BriefVisitTreatment = new BriefVisitTreatmentSetting { TimeInStatus = new ExcludeBriefVisits() } };

        await Assert.That(copied.Derivation.TimeInStatusTreatment.Value).IsTypeOf<ExcludeBriefVisits>();
        await Assert.That(copied.DerivedFrom("synthetic-digest").BriefVisitTreatment).IsEqualTo("""{"timeInStatus":"exclude"}""");
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitTreatment).IsEqualTo("""{"timeInStatus":"count"}""");
    }

    [Test]
    public async Task Exclude_with_no_status_listed_is_refused()
    {
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with
        {
            BriefVisitTreatment = new BriefVisitTreatmentSetting { TimeInStatus = new ExcludeBriefVisits() },
        })).Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo(
            "briefVisitTreatment.timeInStatus is exclude, and no status is listed in briefVisitThresholds, so no visit would be left out. List a status there, or write count.");
    }

    [Test]
    public async Task Count_with_no_status_listed_is_accepted()
    {
        var settings = TelltaleSettings.From(Config() with
        {
            BriefVisitTreatment = new BriefVisitTreatmentSetting { TimeInStatus = new CountBriefVisits() },
        });

        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitTreatment).IsEqualTo("""{"timeInStatus":"count"}""");
    }

    [Test]
    public async Task A_null_setting_is_refused_by_name()
    {
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitTreatment = null! }))
            .Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo("briefVisitTreatment must not be null.");
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
