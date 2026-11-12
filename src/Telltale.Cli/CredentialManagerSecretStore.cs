using System.Runtime.InteropServices;
using System.Text;
using Telltale.Core;
using Telltale.Core.Secrets;

namespace Telltale.Cli;

/// <summary>
/// Keeps secrets in Windows Credential Manager, as generic credentials of the signed-in user (the entry
/// names are in <see cref="SecretInfo"/>), each value as UTF-16 text. Windows encrypts them for that
/// user. It never reads an environment variable.
/// </summary>
/// <param name="vault">The Windows calls. <see cref="SecretStores.ForPlatform"/> gives the real ones, on Windows only.</param>
public sealed class CredentialManagerSecretStore(ICredentialVault vault) : IWritableSecretStore
{
    private const int ErrorNotFound = 1168;

    /// <summary>What a write returns when policy forbids storing credentials (ERROR_NO_SUCH_LOGON_SESSION).</summary>
    private const int ErrorNoSuchLogonSession = 1312;

    /// <summary>Windows' limit on a credential's value (CRED_MAX_CREDENTIAL_BLOB_SIZE), in bytes.</summary>
    private const int MaxBlobBytes = 5 * 512;

    public string? Get(SecretName name)
    {
        var secret = SecretInfo.Of(name);
        var error = vault.Read(secret.CredentialTarget, out var blob);
        if (error == ErrorNotFound)
        {
            return null;
        }

        if (error != 0 || blob is null)
        {
            throw new UserFacingException($"Windows Credential Manager could not read the secret {secret.Label}: {Marshal.GetPInvokeErrorMessage(error)} (error {error}).");
        }

        try
        {
            // Another tool may have written the entry with a terminating NUL, which is not part of the value.
            var text = blob.Length % sizeof(char) == 0 ? Encoding.Unicode.GetString(blob).TrimEnd('\0').Trim() : null;
            if (text is null || text.Any(char.IsControl))
            {
                throw new UserFacingException($"The secret {secret.Label} in Windows Credential Manager is not text this tool stored, so it cannot be used. Set it again. {secret.SetCommand}");
            }

            return text.Length == 0 ? null : text;
        }
        finally
        {
            Array.Clear(blob);
        }
    }

    public string HowToSet(SecretName name) => SecretInfo.Of(name).SetCommand;

    public void Set(SecretName name, string value)
    {
        var secret = SecretInfo.Of(name);
        var blob = Encoding.Unicode.GetBytes(value);
        try
        {
            if (blob.Length > MaxBlobBytes)
            {
                throw new UserFacingException($"The secret {secret.Label} is longer than the {MaxBlobBytes / sizeof(char)} characters Windows Credential Manager can hold. Nothing was stored.");
            }

            var error = vault.Write(secret.CredentialTarget, blob);
            if (error == ErrorNoSuchLogonSession)
            {
                throw new UserFacingException(
                    $"Windows refused to store the secret {secret.Label} (error {error}). This usually means the policy \"Network access: Do not allow storage of passwords and credentials "
                    + "for network authentication\" is on for this machine, and then Credential Manager cannot hold the tool's secrets. Nothing was stored.");
            }

            if (error != 0)
            {
                throw new UserFacingException($"Windows Credential Manager could not store the secret {secret.Label}: {Marshal.GetPInvokeErrorMessage(error)} (error {error}). Nothing was stored.");
            }
        }
        finally
        {
            Array.Clear(blob);
        }
    }
}
