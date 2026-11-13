namespace Telltale.Core.Secrets;

/// <summary>Everything that is said or named differently per secret, in one place. Never a value.</summary>
/// <param name="Name">The secret.</param>
/// <param name="Label">How messages name it, in quotes.</param>
/// <param name="Argument">How the command line names it, as in <c>secrets set jira-api-token</c>.</param>
/// <param name="EnvironmentVariable">The variable the environment-variable store reads it from.</param>
/// <param name="CredentialTarget">The name of its entry in Windows Credential Manager, under "Generic Credentials".</param>
public sealed record SecretInfo(SecretName Name, string Label, string Argument, string EnvironmentVariable, string CredentialTarget)
{
    /// <summary>Every secret the tool has, in the order they are checked.</summary>
    public static IReadOnlyList<SecretInfo> All { get; } =
    [
        new(SecretName.JiraEmail, "'Jira email'", "jira-email", "TELLTALE_JIRA_EMAIL", "Telltale/JiraEmail"),
        new(SecretName.JiraApiToken, "'Jira API token'", "jira-api-token", "TELLTALE_JIRA_API_TOKEN", "Telltale/JiraApiToken"),
        new(SecretName.HashingSalt, "'hashing salt'", "hashing-salt", "TELLTALE_HASHING_SALT", "Telltale/HashingSalt"),
    ];

    public static SecretInfo Of(SecretName name) =>
        All.FirstOrDefault(info => info.Name == name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown secret.");

    /// <summary>The sentence that tells the person to run <c>secrets set</c> for this secret.</summary>
    public string SetCommand => $"Run 'telltale secrets set {Argument}'.";
}
