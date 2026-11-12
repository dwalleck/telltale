using System.Runtime.InteropServices;

namespace Telltale.Cli;

/// <summary>
/// The two Windows calls <see cref="CredentialManagerSecretStore"/> is built on, so that everything
/// around them can be tested where Windows is not.
/// </summary>
public interface ICredentialVault
{
    /// <summary>Reads a generic credential's value. Returns 0 and the bytes, or a Windows error code and null.</summary>
    int Read(string targetName, out byte[]? blob);

    /// <summary>Writes a generic credential for the signed-in user. Returns 0, or a Windows error code.</summary>
    int Write(string targetName, byte[] blob);
}

/// <summary>Windows Credential Manager through <c>advapi32.dll</c>. Windows only: it cannot run, or be tested, anywhere else.</summary>
public sealed class WindowsCredentialVault : ICredentialVault
{
    private const uint CredTypeGeneric = 1;

    /// <summary>Kept across sign-ins on this machine, and not copied to other machines by a roaming profile.</summary>
    private const uint CredPersistLocalMachine = 2;

    public int Read(string targetName, out byte[]? blob)
    {
        blob = null;
        if (!CredRead(targetName, CredTypeGeneric, 0, out var pointer))
        {
            return Marshal.GetLastPInvokeError();
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            blob = new byte[credential.CredentialBlob == 0 ? 0 : (int)credential.CredentialBlobSize];
            if (blob.Length > 0)
            {
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            }

            return 0;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public int Write(string targetName, byte[] blob)
    {
        // Allocated inside the try, so whatever was allocated is wiped and released even when a later allocation fails.
        var credential = new Credential { Type = CredTypeGeneric, CredentialBlobSize = (uint)blob.Length, Persist = CredPersistLocalMachine };
        try
        {
            credential.TargetName = Marshal.StringToCoTaskMemUni(targetName);
            credential.UserName = Marshal.StringToCoTaskMemUni("telltale");
            credential.CredentialBlob = Marshal.AllocCoTaskMem(blob.Length);
            Marshal.Copy(blob, 0, credential.CredentialBlob, blob.Length);

            return CredWrite(in credential, 0) ? 0 : Marshal.GetLastPInvokeError();
        }
        finally
        {
            if (credential.CredentialBlob != 0)
            {
                // Every byte of the unmanaged copy of the value is overwritten with zero before its memory is released.
                Marshal.Copy(new byte[blob.Length], 0, credential.CredentialBlob, blob.Length);
                Marshal.FreeCoTaskMem(credential.CredentialBlob);
            }

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
