using System.Text.Json;

namespace Telltale.Collectors.Jira;

/// <summary>How every reader of Jira's answers takes a value out of JSON.</summary>
internal static class JiraJson
{
    /// <summary>A text value Jira must give. Absent, null or not text is an answer in an unknown shape.</summary>
    public static string Text(JsonElement parent, string name) =>
        parent.GetProperty(name).GetString() ?? throw new FormatException($"{name} is null.");

    /// <summary>A property's value, or null when it is absent or JSON null. Jira uses both for "not set".</summary>
    public static JsonElement? Optional(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
}
