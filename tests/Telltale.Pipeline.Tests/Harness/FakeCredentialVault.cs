using Telltale.Cli;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Stands in for Windows Credential Manager: entries by name in <see cref="Entries"/>, as the bytes
/// Windows would hold. Set <see cref="ReadError"/> or <see cref="WriteError"/> to a Windows error code
/// to make that call fail.
/// </summary>
public sealed class FakeCredentialVault : ICredentialVault
{
    private const int ErrorNotFound = 1168;

    public Dictionary<string, byte[]> Entries { get; } = [];

    public int ReadError { get; set; }

    public int WriteError { get; set; }

    public int Read(string targetName, out byte[]? blob)
    {
        blob = null;
        if (ReadError != 0)
        {
            return ReadError;
        }

        if (!Entries.TryGetValue(targetName, out var stored))
        {
            return ErrorNotFound;
        }

        blob = [.. stored];
        return 0;
    }

    public int Write(string targetName, byte[] blob)
    {
        if (WriteError != 0)
        {
            return WriteError;
        }

        // A copy, as Windows keeps one: the caller wipes its own array afterwards.
        Entries[targetName] = [.. blob];
        return 0;
    }
}
