using System.Text.RegularExpressions;

namespace Telltale.Core.Configuration;

/// <summary>
/// The form of a Jira project key: an upper-case letter followed by upper-case letters, digits or
/// underscores. Project keys are written into JQL, so anything else is refused; a value in this
/// form cannot close a quote or add a clause.
/// </summary>
public static partial class JiraProjectKey
{
    public static bool IsValid(string? key) => key is not null && Form().IsMatch(key);

    /// <summary>Throws <see cref="FormatException"/> naming the first value that is not a project key.</summary>
    public static void RequireValid(IEnumerable<string?> keys)
    {
        foreach (var key in keys)
        {
            if (!IsValid(key))
            {
                throw new FormatException($"'{key}' is not a Jira project key (an upper-case letter followed by upper-case letters, digits or underscores).");
            }
        }
    }

    // \z, not $: $ would accept a trailing newline.
    [GeneratedRegex(@"\A[A-Z][A-Z0-9_]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Form();
}
