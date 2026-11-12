using Telltale.Core.Secrets;

namespace Telltale.Cli;

/// <summary>
/// Reads secrets from environment variables, for development off the work laptop:
/// <c>TELLTALE_JIRA_EMAIL</c>, <c>TELLTALE_JIRA_API_TOKEN</c> and <c>TELLTALE_HASHING_SALT</c>.
/// It is never used on Windows, and <c>secrets set</c> cannot write to it.
/// </summary>
public sealed class EnvironmentSecretStore : ISecretStore
{
    private readonly Func<string, string?> _read;

    public EnvironmentSecretStore()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    /// <param name="read">Looks up a variable by name. Tests pass their own, because the process environment is shared.</param>
    public EnvironmentSecretStore(Func<string, string?> read)
    {
        _read = read;
    }

    public string? Get(SecretName name) => _read(SecretInfo.Of(name).EnvironmentVariable);

    public string HowToSet(SecretName name) => $"Set the environment variable {SecretInfo.Of(name).EnvironmentVariable}.";
}
