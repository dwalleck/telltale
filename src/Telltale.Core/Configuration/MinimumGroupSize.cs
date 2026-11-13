namespace Telltale.Core.Configuration;

/// <summary>
/// The minimum group size's two numbers (GLOSSARY: Minimum group size; ADR-0003 as amended by ADR-0008): the
/// <c>minimumGroupSize</c> setting of the configuration file. A number of zero turns that check off (owner
/// decision D65). The default numbers are stated here and nowhere else: a rebuild records the numbers in
/// force, and the metric views read them from that record.
/// </summary>
public sealed record MinimumGroupSize
{
    /// <summary>The numbers in force when the configuration file has no <c>minimumGroupSize</c>: five and three (owner decision D64).</summary>
    public static MinimumGroupSize Default { get; } = new() { Team = 5, AssigneesBehindAFigure = 3 };

    /// <summary>The fewest distinct assignees a sprint, or the whole store for the footer's counts, needs for anything to be shown.</summary>
    public required int Team { get; init; }

    /// <summary>The fewest distinct assignees among the issues one figure is computed from.</summary>
    public required int AssigneesBehindAFigure { get; init; }
}
