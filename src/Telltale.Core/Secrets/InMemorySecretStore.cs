namespace Telltale.Core.Secrets;

/// <summary>A secret store over a dictionary, for tests. It reads and writes the dictionary on every call, so edits are seen both ways.</summary>
public sealed class InMemorySecretStore(IDictionary<SecretName, string> secrets) : IWritableSecretStore
{
    public string? Get(SecretName name) => secrets.TryGetValue(name, out var value) ? value : null;

    public string HowToSet(SecretName name) => "Add it to the secrets this in-memory store was built over.";

    public void Set(SecretName name, string value) => secrets[name] = value;
}
