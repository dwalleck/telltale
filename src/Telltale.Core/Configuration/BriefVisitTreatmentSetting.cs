using Telltale.Core.Intervals;

namespace Telltale.Core.Configuration;

/// <summary>
/// <c>briefVisitTreatment</c> as the configuration file has it (GLOSSARY: Treatment; owner decision D83): what
/// is done with brief visits, one part for each metric a treatment can change. A part left out means
/// <see cref="CountBriefVisits"/>, and so does the setting left out. The setting has a type of its own, as
/// <see cref="MinimumGroupSize"/> has, so that the file's reader can be given one reader for it by type.
/// A part is added by the change that first acts on it, so the file never accepts a treatment that has no effect.
/// </summary>
public sealed record BriefVisitTreatmentSetting
{
    /// <summary>The setting's name in the configuration file.</summary>
    public const string Name = "briefVisitTreatment";

    /// <summary>The name of the part for time in status.</summary>
    public const string TimeInStatusName = "timeInStatus";

    /// <summary>The name of the part for cycle time.</summary>
    public const string CycleTimeName = "cycleTime";

    /// <summary>The setting left out: brief visits are counted and no figure is changed.</summary>
    public static BriefVisitTreatmentSetting None { get; } = new();

    /// <summary>What is done with brief visits when time in status is measured.</summary>
    public BriefVisitTreatment TimeInStatus { get; init; } = new CountBriefVisits();

    /// <summary>What is done with a delivered issue's cycle that starts with a brief visit when cycle time is measured.</summary>
    public BriefVisitTreatment CycleTime { get; init; } = new CountBriefVisits();
}
