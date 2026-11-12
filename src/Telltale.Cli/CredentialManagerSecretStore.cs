using System.Runtime.InteropServices;
using Telltale.Cli.Commands;
using Telltale.Core;
using Telltale.Core.Secrets;

namespace Telltale.Cli;

/// <summary>
/// Keeps secrets in Windows Credential Manager, as generic credentials of the signed-in user named
/// <c>Telltale/JiraEmail</c>, <c>Telltale/JiraApiToken</c> and <c>Telltale/HashingSalt</c>. Windows encrypts
/// them for that user. It never reads an environment variable.
/// </summary>
/// <remarks>
/// Windows only: <see cref="Get"/> and <see cref="Set"/> call <c>advapi32.dll</c>, so they cannot run, or be
/// tested, anywhere else. <see cref="SecretStores.ForPlatform"/> selects this store only on Windows.
/// </remarks>
public sealed class CredentialManagerSecretStore : IWritableSecretStore
{
    private const uint CredTypeGeneric = 1;

    /// <summary>Kept across sign-ins on this machine, and not copied to other machines by a roaming profile.</summary>
    private const uint CredPersistLocalMachine = 2;

    private const int ErrorNotFound = 1168;

    /// <summary>Windows' limit on a credential's value (CRED_MAX_CREDENTIAL_BLOB_SIZE), in bytes of UTF-16.</summary>
    private const int MaxBlobBytes = 5 * 512;

    /// <summary>The name of a secret's entry, as Credential Manager lists it under "Generic Credentials".</summary>
    public static string TargetName(SecretName name) => name switch
    {
        SecretName.JiraEmail => "Telltale/JiraEmail",
        SecretName.JiraApiToken => "Telltale/JiraApiToken",
        SecretName.HashingSalt => "Telltale/HashingSalt",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown secret."),
    };

    public string? Get(SecretName name)
    {
        if (!CredRead(TargetName(name), CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound
                ? null
                : throw new UserFacingException($"Windows Credential Manager could not read the secret {name.Describe()}: {Marshal.GetPInvokeErrorMessage(error)} (error {error}).");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlob == 0
                ? null
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / sizeof(char));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public string HowToSet(SecretName name) => name == SecretName.HashingSalt
        ? $"Run 'telltale secrets set {SecretsCommand.ArgumentFor(name)}', adding --generate for a new random salt."
        : $"Run 'telltale secrets set {SecretsCommand.ArgumentFor(name)}'.";

    public void Set(SecretName name, string value)
    {
        var blobBytes = value.Length * sizeof(char);
        if (blobBytes > MaxBlobBytes)
        {
            throw new UserFacingException($"The secret {name.Describe()} is longer than the {MaxBlobBytes / sizeof(char)} characters Windows Credential Manager can hold. Nothing was stored.");
        }

        var credential = new Credential
        {
            Type = CredTypeGeneric,
            TargetName = Marshal.StringToCoTaskMemUni(TargetName(name)),
            CredentialBlobSize = (uint)blobBytes,
            CredentialBlob = Marshal.StringToCoTaskMemUni(value),
            Persist = CredPersistLocalMachine,
            UserName = Marshal.StringToCoTaskMemUni("telltale"),
        };

        try
        {
            if (!CredWrite(in credential, 0))
            {
                var error = Marshal.GetLastPInvokeError();
                throw new UserFacingException($"Windows Credential Manager could not store the secret {name.Describe()}: {Marshal.GetPInvokeErrorMessage(error)} (error {error}).");
            }
        }
        finally
        {
            // The copy of the value is overwritten with zeros before its memory is released.
            Marshal.ZeroFreeCoTaskMemUnicode(credential.CredentialBlob);
            Marshal.FreeCoTaskMem(credential.TargetName);
            Marshal.FreeCoTaskMem(credential.UserName);
        }
    }

    /// <summary>CREDENTIALW from wincred.h. Every field is a number or a pointer, so it is copied as it is.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(in Credential credential, uint flags);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(nint buffer);
}
