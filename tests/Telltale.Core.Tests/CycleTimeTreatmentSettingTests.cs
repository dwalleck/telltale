using Telltale.Core.Configuration;
using Telltale.Core.Intervals;

namespace Telltale.Core.Tests;

/// <summary>
/// The part <c>cycleTime</c> of <c>briefVisitTreatment</c> in a configuration built in code, where no file
/// reader has refused anything first (GLOSSARY: Treatment; owner decision D83): what
/// <see cref="TelltaleSettings.From"/> accepts and refuses, what derive is given, and what a rebuild records.
/// Status 4 has the work role and status 1 the role not started.
/// </summary>
public class CycleTimeTreatmentSettingTests
{
    private static readonly Dictionary<string, double> DevelopmentListed = new() { ["4"] = 5 };

    private static readonly Dictionary<string, double> NewListed = new() { ["1"] = 5 };

    private static readonly BriefVisitTreatmentSetting CycleTimeExcluded = new() { CycleTime = new ExcludeBriefVisits() };

    [Test]
    public async Task Left_out_the_part_is_count_for_derive_and_in_the_record()
    {
        var settings = TelltaleSettings.From(Config() with
        {
            BriefVisitThresholds = DevelopmentListed,
            BriefVisitTreatment = new BriefVisitTreatmentSetting { TimeInStatus = new ExcludeBriefVisits() },
        });

        await Assert.That(settings.Derivation.CycleTimeTreatment.Value).IsTypeOf<CountBriefVisits>();
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitTreatment).IsEqualTo("""{"cycleTime":"count","timeInStatus":"exclude"}""");
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitsLeftOutOfCycleTime).IsFalse();
    }

    [Test]
    public async Task Exclude_for_cycle_time_is_what_derive_is_given_and_what_a_rebuild_records_and_time_in_status_is_still_counted()
    {
        var settings = TelltaleSettings.From(Config() with { BriefVisitThresholds = DevelopmentListed, BriefVisitTreatment = CycleTimeExcluded });

        await Assert.That(settings.Derivation.CycleTimeTreatment.Value).IsTypeOf<ExcludeBriefVisits>();
        await Assert.That(settings.Derivation.TimeInStatusTreatment.Value).IsTypeOf<CountBriefVisits>();
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitTreatment).IsEqualTo("""{"cycleTime":"exclude","timeInStatus":"count"}""");
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitsLeftOutOfCycleTime).IsTrue();
        await Assert.That(settings.DerivedFrom("synthetic-digest").BriefVisitsLeftOutOfTimeInStatus).IsFalse();
    }

    [Test]
    public async Task A_record_made_without_the_treatment_is_of_a_rebuild_that_counted_every_cycle()
    {
        var record = new Storage.DerivedSource("synthetic-digest", "UTC", "", "[]", "", "", 5, 3, Storage.DerivedSource.NoBriefVisitThresholds);

        await Assert.That(record.BriefVisitTreatment).IsEqualTo("""{"cycleTime":"count","timeInStatus":"count"}""");
        await Assert.That(record.BriefVisitsLeftOutOfCycleTime).IsFalse();
    }

    [Test]
    public async Task Exclude_with_no_status_listed_is_refused()
    {
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitTreatment = CycleTimeExcluded }))
            .Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo(
            "briefVisitTreatment.cycleTime is exclude, and no status is listed in briefVisitThresholds, so no cycle would be left out. List a status with the role work there, or write count.");
    }

    [Test]
    public async Task Exclude_with_no_listed_status_that_has_the_work_role_is_refused()
    {
        var refusal = await Assert.That(() => TelltaleSettings.From(Config() with { BriefVisitThresholds = NewListed, BriefVisitTreatment = CycleTimeExcluded }))
            .Throws<SettingRefusedException>();

        await Assert.That(refusal!.Message).IsEqualTo(
            "briefVisitTreatment.cycleTime is exclude, and no status listed in briefVisitThresholds has the role work, so no cycle could start with a brief visit and none would be left out. List a status with the role work there, or write count.");
    }

    [Test]
    public async Task Exclude_is_accepted_when_one_of_the_listed_statuses_has_the_work_role()
    {
        var settings = TelltaleSettings.From(Config() with
        {
            BriefVisitThresholds = new Dictionary<string, double> { ["1"] = 5, ["4"] = 5 },
            BriefVisitTreatment = CycleTimeExcluded,
        });

        await Assert.That(settings.Derivation.CycleTimeTreatment.Value).IsTypeOf<ExcludeBriefVisits>();
    }

    [Test]
    public async Task Count_is_accepted_with_no_status_listed_and_with_no_listed_status_that_has_the_work_role()
    {
        var nothingListed = TelltaleSettings.From(Config() with { BriefVisitTreatment = new BriefVisitTreatmentSetting { CycleTime = new CountBriefVisits() } });
        var newListed = TelltaleSettings.From(Config() with { BriefVisitThresholds = NewListed, BriefVisitTreatment = new BriefVisitTreatmentSetting { CycleTime = new CountBriefVisits() } });

        await Assert.That(nothingListed.Derivation.CycleTimeTreatment.Value).IsTypeOf<CountBriefVisits>();
        await Assert.That(newListed.Derivation.CycleTimeTreatment.Value).IsTypeOf<CountBriefVisits>();
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
