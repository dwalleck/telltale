using System.Globalization;

namespace Telltale.Core.Storage;

/// <summary>
/// How times are stored, and the one place that knows it: the store writes and reads through it,
/// and so does anything that reads a time column from a metric view. They are UTC text of fixed
/// width with millisecond precision, which sorts as text the same way it sorts as time. The original offset is dropped on purpose; day boundaries come from
/// the configured team time zone.
/// </summary>
public static class StoredTime
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string ToStored(DateTimeOffset value) => value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static string? ToStored(DateTimeOffset? value) => value is { } time ? ToStored(time) : null;

    public static DateTimeOffset FromStored(string value) =>
        DateTimeOffset.ParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static DateTimeOffset? FromStoredOrNull(string? value) => value is null ? null : FromStored(value);
}
