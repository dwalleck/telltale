using Telltale.Core;
using Telltale.Core.Storage;

namespace Telltale.Core.Tests;

/// <summary>How the treatment a rebuild recorded is read back: never by guessing <c>count</c> for what it cannot read.</summary>
public class RecordedBriefVisitTreatmentTests
{
    [Test]
    public async Task The_recorded_treatment_is_read_back_part_by_part()
    {
        var source = Source("""{"cycleTime":"count","timeInStatus":"exclude"}""");

        await Assert.That(source.BriefVisitsLeftOutOfTimeInStatus).IsTrue();
        await Assert.That(source.BriefVisitsLeftOutOfCycleTime).IsFalse();
        await Assert.That(Source(DerivedSource.BriefVisitsCounted).BriefVisitsLeftOutOfTimeInStatus).IsFalse();
    }

    [Test]
    [Arguments("""{"cycleTime":"count","timeInStatus":"synthetic-unknown"}""")]
    [Arguments("""{"cycleTime":"count"}""")]
    [Arguments("""{"cycleTime":"count","timeInStatus":1}""")]
    [Arguments("""[]""")]
    public async Task A_recorded_treatment_this_version_cannot_read_asks_for_derive_and_is_not_taken_for_count(string recorded)
    {
        var source = Source(recorded);

        await Assert.That(() => source.BriefVisitsLeftOutOfTimeInStatus).Throws<UserFacingException>()
            .WithMessage("The derived data in the store records a treatment of brief visits this version of the tool does not know. Run derive first.");
    }

    private static DerivedSource Source(string treatment) =>
        new("synthetic-digest", "America/Chicago", "", "[]", "", "", 5, 3, DerivedSource.NoBriefVisitThresholds, treatment);
}
