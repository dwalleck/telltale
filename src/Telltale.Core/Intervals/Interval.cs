namespace Telltale.Core.Intervals;

/// <summary>A half-open span of absolute time: [Start, End).</summary>
public readonly record struct Interval
{
    public Interval(DateTimeOffset start, DateTimeOffset end)
    {
        if (end < start)
        {
            throw new ArgumentException($"Interval end {end:o} is before start {start:o}.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;

    public bool IsEmpty => End == Start;

    /// <summary>The overlap of two intervals, or null when they don't overlap.</summary>
    public Interval? Intersect(Interval other)
    {
        var start = Start > other.Start ? Start : other.Start;
        var end = End < other.End ? End : other.End;
        return end > start ? new Interval(start, end) : null;
    }

    public override string ToString() => $"[{Start:o}, {End:o})";
}
