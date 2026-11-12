using Telltale.Core.Secrets;

namespace Telltale.Cli;

/// <summary>Which secret store the executable uses.</summary>
public static class SecretStores
{
    /// <summary>
    /// Windows Credential Manager on Windows, where the tool runs against real data; environment variables
    /// anywhere else, for development. On Windows no environment variable is ever read for a secret.
    /// </summary>
    public static ISecretStore ForPlatform(bool isWindows) =>
        isWindows ? new CredentialManagerSecretStore(new WindowsCredentialVault()) : new EnvironmentSecretStore();
}
