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
    /// <summary>
    /// The secret's value, or null when it has not been set. Throws <see cref="StoredSecretUnusableException"/>
    /// when something is stored under the secret's name that was read and is not a value this tool stores,
    /// and another <see cref="UserFacingException"/> when the store could not be read at all.
    /// </summary>
    string? Get(SecretName name);

    /// <summary>One sentence telling the person running the tool how to set this secret in this store. It ends the message about a missing secret.</summary>
    string HowToSet(SecretName name);
}

/// <summary>
/// Something is stored under a secret's name, it was read, and it is not a value this tool stores (not one
/// line of text). Setting the secret again cures it. A read that failed is not this: it says nothing about
/// what is stored.
/// </summary>
public sealed class StoredSecretUnusableException(string message) : UserFacingException(message);

/// <summary>A secret store <c>secrets set</c> can write to. The environment-variable store is not one.</summary>
public interface IWritableSecretStore : ISecretStore
{
    /// <summary>Stores the value, replacing any earlier one.</summary>
    void Set(SecretName name, string value);
}

public static class SecretStoreExtensions
{
    /// <summary>How every message says that a secret is missing, up to the full stop: what follows differs with what the reader can do about it.</summary>
    public static string NotSet(this ISecretStore secrets, SecretName name) => $"The secret {SecretInfo.Of(name).Label} is not set in the secret store";

    /// <summary>The secret's value, without surrounding white space. Throws <see cref="UserFacingException"/> naming the secret and how to set it when it is missing or blank.</summary>
    public static string Require(this ISecretStore secrets, SecretName name)
    {
        var value = secrets.Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UserFacingException($"{secrets.NotSet(name)}. {secrets.HowToSet(name)}");
        }

        // A value pasted with a trailing newline must behave like the clean one: for the salt, it decides every hash.
        return value.Trim();
    }
}
