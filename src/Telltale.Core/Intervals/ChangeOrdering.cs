namespace Telltale.Core.Intervals;

/// <summary>
/// Puts changelog entries into a reliable order. Jira's bulk changelog endpoint can
/// return the same history entry more than once, and entries can share a timestamp.
/// </summary>
internal static class ChangeOrdering
{
    public static List<T> DedupeAndSort<T>(IEnumerable<T> changes, Func<T, string> historyId, Func<T, DateTimeOffset> at)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return changes
            .Where(change => seen.Add(historyId(change)))
            .OrderBy(at)
            .ThenBy(historyId, HistoryIdComparer.Instance)
            .ToList();
    }

    /// <summary>Jira history IDs are numeric strings, so "10" sorts after "9".</summary>
    private sealed class HistoryIdComparer : IComparer<string>
    {
        public static readonly HistoryIdComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (long.TryParse(x, out var left) && long.TryParse(y, out var right))
            {
                return left.CompareTo(right);
            }

            return string.CompareOrdinal(x, y);
        }
    }
}
