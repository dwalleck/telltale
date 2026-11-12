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

    /// <summary>One sentence telling the person running the tool how to set this secret in this store. It ends the message about a missing secret.</summary>
    string HowToSet(SecretName name);
}

/// <summary>A secret store <c>secrets set</c> can write to. The environment-variable store is not one.</summary>
public interface IWritableSecretStore : ISecretStore
{
    /// <summary>Stores the value, replacing any earlier one.</summary>
    void Set(SecretName name, string value);
}

public static class SecretStoreExtensions
{
    /// <summary>The secret's value, without surrounding white space. Throws <see cref="UserFacingException"/> naming the secret and how to set it when it is missing or blank.</summary>
    public static string Require(this ISecretStore secrets, SecretName name)
    {
        var value = secrets.Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UserFacingException($"The secret {SecretInfo.Of(name).Label} is not set in the secret store. {secrets.HowToSet(name)}");
        }

        // A value pasted with a trailing newline must behave like the clean one: for the salt, it decides every hash.
        return value.Trim();
    }
}
