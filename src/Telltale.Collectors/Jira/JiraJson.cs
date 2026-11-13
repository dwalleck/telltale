using System.Text.Json;
using Telltale.Core;

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

    /// <summary>
    /// Reads Jira's answer. An answer in a shape the tool does not know (a missing field, a value of another
    /// type, a timestamp in an unknown form) stops the command with a plain message. The message names only
    /// the kind of failure, never a value from the answer. <paramref name="nothingWas"/> ends it with what
    /// the command left undone: "Nothing was stored." or "Nothing was written.".
    /// </summary>
    public static T Read<T>(string what, string nothingWas, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UserFacingException($"Jira's {what} answered in a shape this tool does not understand ({exception.GetType().Name}). {nothingWas}", exception);
        }
    }

    /// <summary>The statuses of the site's status list (<c>rest/api/3/status</c>): each one's ID, its name and its category's key.</summary>
    public static IEnumerable<(string Id, string Name, string CategoryKey)> Statuses(JsonElement statusList) =>
        statusList.EnumerateArray().Select(status => (Text(status, "id"), Text(status, "name"), Text(status.GetProperty("statusCategory"), "key")));
}
