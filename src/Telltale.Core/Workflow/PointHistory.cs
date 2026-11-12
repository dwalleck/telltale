using System.Globalization;
using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>One change to an issue's story point estimate. A null value is no estimate.</summary>
public sealed record PointChange(string HistoryId, DateTimeOffset At, double? From, double? To)
{
    /// <summary>Builds a change from the stored values: numbers in invariant form, or null for no estimate.</summary>
    public static PointChange FromRaw(string historyId, DateTimeOffset at, string? from, string? to) =>
        new(historyId, at, Parse(from), Parse(to));

    private static double? Parse(string? raw) =>
        raw is null ? null : double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
}

public static class PointHistory
{
    /// <summary>
    /// The story point estimate in effect at <paramref name="moment"/>, rebuilt from the estimate's history
    /// (GLOSSARY: Point bucket). Null means the issue had no estimate then. It is what the last change at or
    /// before the moment set; before the first change, what that change replaced; and with no history at all,
    /// the current estimate, which then never changed.
    /// </summary>
    public static double? EstimateAt(DateTimeOffset moment, double? currentEstimate, IEnumerable<PointChange> changes)
    {
        var ordered = ChangeOrdering.DedupeAndSort(changes, change => change.HistoryId, change => change.At);
        if (ordered.Count == 0)
        {
            return currentEstimate;
        }

        var last = ordered.LastOrDefault(change => change.At <= moment);
        return last is null ? ordered[0].From : last.To;
    }
}
