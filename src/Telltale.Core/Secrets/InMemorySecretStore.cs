namespace Telltale.Core.Secrets;

/// <summary>A secret store over a dictionary, for tests. It reads the dictionary on every call, so later edits are seen.</summary>
public sealed class InMemorySecretStore(IReadOnlyDictionary<SecretName, string> secrets) : ISecretStore
{
    public string? Get(SecretName name) => secrets.GetValueOrDefault(name);
}
