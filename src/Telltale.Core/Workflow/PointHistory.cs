using System.Globalization;
using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>One change to an issue's story point estimate. A null value is no estimate.</summary>
public sealed record PointChange(string HistoryId, DateTimeOffset At, double? From, double? To)
{
    /// <summary>Builds a change from the stored values: numbers in invariant form, or null for no estimate.</summary>
    public static PointChange FromRaw(string historyId, DateTimeOffset at, string? from, string? to) =>
        new(historyId, at, Parse(from), Parse(to));

    /// <summary>
    /// The collector stores only finite numbers that are not negative, so anything else was put in the store
    /// by hand. The message does not repeat it: what is not a number could be anything.
    /// </summary>
    private static double? Parse(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var points) && double.IsFinite(points) && points >= 0
            ? points
            : throw new UserFacingException(
                "A stored story point change holds a value that is not a story point number, which the collector never stores. Run backfill to fetch the history again. No figures were derived.");
    }
}

public static class PointHistory
{
    /// <summary>
    /// The story point estimate in effect at <paramref name="moment"/>, rebuilt from the estimate's history
    /// (GLOSSARY: Point bucket). Null means the issue had no estimate then. It is what the last change before
    /// the moment set; before the first change, what that change replaced; and with no history at all, the
    /// current estimate, which then never changed.
    /// </summary>
    /// <remarks>
    /// <paramref name="momentHistoryId"/> is the changelog entry that made the moment (the move into work that
    /// started the cycle), or null when no entry did (the issue was created in a work status). Among changes
    /// with the moment's own timestamp it decides: a change recorded before that entry, or in it, was in effect;
    /// one recorded after it was not, and neither was any when there is no entry, since an issue's creation
    /// comes before every change to it.
    /// </remarks>
    public static double? EstimateAt(DateTimeOffset moment, string? momentHistoryId, double? currentEstimate, IEnumerable<PointChange> changes)
    {
        var ordered = ChangeOrdering.DedupeAndSort(changes, change => change.HistoryId, change => change.At);
        if (ordered.Count == 0)
        {
            return currentEstimate;
        }

        var last = ordered.LastOrDefault(change =>
            change.At < moment
            || (change.At == moment && momentHistoryId is not null && ChangeOrdering.CompareHistoryIds(change.HistoryId, momentHistoryId) <= 0));
        return last is null ? ordered[0].From : last.To;
    }
}
