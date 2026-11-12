namespace Telltale.Core.Secrets;

/// <summary>The secrets the tool needs. None of them is ever written to a file, a log or the database.</summary>
public enum SecretName
{
    /// <summary>The email of the Jira account the API token belongs to.</summary>
    JiraEmail,

    /// <summary>The Jira API token.</summary>
    JiraApiToken,

    /// <summary>The key for hashing person identities (ADR-0003).</summary>
    HashingSalt,
}

/// <summary>Where secrets are kept: Windows Credential Manager on the work laptop, environment variables in development, memory in tests.</summary>
public interface ISecretStore
{
    /// <summary>The secret's value, or null when it has not been set.</summary>
    string? Get(SecretName name);
}

public static class SecretStoreExtensions
{
    /// <summary>The secret's value, without surrounding white space. Throws <see cref="UserFacingException"/> naming the secret when it is missing or blank.</summary>
    public static string Require(this ISecretStore secrets, SecretName name)
    {
        var value = secrets.Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UserFacingException($"The secret {Describe(name)} is not set in the secret store.");
        }

        // A value pasted with a trailing newline must behave like the clean one: for the salt, it decides every hash.
        return value.Trim();
    }

    private static string Describe(SecretName name) => name switch
    {
        SecretName.JiraEmail => "'Jira email'",
        SecretName.JiraApiToken => "'Jira API token'",
        SecretName.HashingSalt => "'hashing salt'",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown secret."),
    };
}
