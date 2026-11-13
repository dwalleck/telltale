using System.Globalization;
using System.Text.Json;

namespace Telltale.Collectors.Jira;

/// <summary>
/// Reads a Jira timestamp. Atlassian's documentation shows several shapes and does not say which
/// field uses which, so every one is accepted: an ISO string with a <c>+0000</c>, <c>+00:00</c> or
/// <c>Z</c> offset, with or without a fraction, and a number of milliseconds since the Unix epoch.
/// </summary>
internal static class JiraTime
{
    private static readonly string[] Formats = ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    public static DateTimeOffset Parse(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => DateTimeOffset.FromUnixTimeMilliseconds(value.GetInt64()),
        JsonValueKind.String => DateTimeOffset.ParseExact(value.GetString()!, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        _ => throw new FormatException($"Not a Jira timestamp: a JSON {value.ValueKind}."),
    };
}
