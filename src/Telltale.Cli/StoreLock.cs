using Telltale.Core;

namespace Telltale.Cli;

/// <summary>
/// One command at a time on a store. A command reads the watermark, fetches, and writes later; a second
/// one in between could put older rows and an older watermark over newer ones, or report from tables
/// that are being rebuilt. The lock is a file beside the database, held open without sharing for as long
/// as the command runs. The operating system releases it when the process ends, however it ends; the
/// empty file stays.
/// </summary>
internal static class StoreLock
{
    public static IDisposable Acquire(string databasePath)
    {
        var path = Path.GetFullPath(databasePath) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when (exception is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException))
        {
            throw new UserFacingException($"Another Telltale command is using the database {databasePath}. Wait for it to finish, then run this one again.", exception);
        }
    }
}
