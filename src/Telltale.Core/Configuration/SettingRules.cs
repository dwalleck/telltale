namespace Telltale.Core.Configuration;

/// <summary>
/// What is refused of one setting's value, each rule written once: the run applies them when it loads the
/// configuration file (<see cref="TelltaleSettings.From"/>), and <c>discover</c> applies the same ones to a
/// value before it writes it. A rule gives the end of a sentence that starts with the setting's name
/// (" must be a positive number."), or null for a value that is accepted. No sentence repeats the value:
/// what is not a valid setting could be a secret pasted into the wrong place.
/// </summary>
public static class SettingRules
{
    /// <summary>
    /// <c>jira.siteUrl</c>: https only, because basic authentication sends the Jira email and the API token
    /// with every request, and no user name or password in the address, which is printed in messages.
    /// </summary>
    public static string? SiteUrlProblem(Uri? siteUrl) =>
        siteUrl is not { IsAbsoluteUri: true } || siteUrl.Scheme != Uri.UriSchemeHttps ? " must be an absolute https:// address."
        : siteUrl.UserInfo.Length > 0 ? " must not carry a user name or password."
        : null;

    /// <summary>
    /// The site address ending in a slash. Relative addresses are resolved against it, and a base address
    /// without a trailing slash loses its last path segment: https://host/tenant + rest/api would become
    /// https://host/rest/api.
    /// </summary>
    public static Uri WithTrailingSlash(Uri siteUrl) =>
        siteUrl.AbsolutePath.EndsWith('/') ? siteUrl : new UriBuilder(siteUrl) { Path = siteUrl.AbsolutePath + "/" }.Uri;

    /// <summary><c>jira.boardId</c>.</summary>
    public static string? BoardIdProblem(long boardId) => boardId <= 0 ? " must be a positive number." : null;

    /// <summary>
    /// <c>jira.sprintFieldId</c> and <c>jira.storyPointFieldId</c>: a custom field ID. The search asks Jira for
    /// these fields by name and the collector stores their history, so a system field such as "description"
    /// would bring free text over the wire. <paramref name="example"/> is the ID the sentence shows.
    /// </summary>
    public static string? CustomFieldIdProblem(string fieldId, string example) =>
        IsCustomFieldId(fieldId) ? null : $" must be a custom field ID such as {example}.";

    /// <summary>True for <c>customfield_</c> followed by digits only, the form of every Jira custom field ID.</summary>
    public static bool IsCustomFieldId(string fieldId)
    {
        const string prefix = "customfield_";
        return fieldId.Length > prefix.Length
            && fieldId.StartsWith(prefix, StringComparison.Ordinal)
            && fieldId.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    /// <summary>True for digits only, the form of Jira's status, resolution and sprint IDs. Anything else could match nothing in Jira.</summary>
    public static bool IsJiraId(string? id) => id is { Length: > 0 } && id.All(char.IsAsciiDigit);

    /// <summary><c>timeZone</c>: the zone for an IANA ID this system knows, or null.</summary>
    public static TimeZoneInfo? FindTimeZone(string timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) ? timeZone : null;
}
