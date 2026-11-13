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

    /// <summary>
    /// Throws <see cref="FormatException"/> when a value is not a project key. The message does not repeat the
    /// value: the keys come from the configuration file, and what is not a key could be a secret pasted into
    /// the wrong place (owner decision D43).
    /// </summary>
    public static void RequireValid(IEnumerable<string?> keys)
    {
        if (!keys.All(IsValid))
        {
            throw new FormatException("every entry must be a Jira project key (an upper-case letter followed by upper-case letters, digits or underscores), and one is not.");
        }
    }

    // \z, not $: $ would accept a trailing newline.
    [GeneratedRegex(@"\A[A-Z][A-Z0-9_]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Form();
}
