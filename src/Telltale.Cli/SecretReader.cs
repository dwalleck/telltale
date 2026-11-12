using System.Text;
using Telltale.Core;
using Telltale.Core.Secrets;

namespace Telltale.Cli;

/// <summary>
/// Reads one secret's value for <c>secrets set</c>: typed at the keyboard without being shown, or piped in.
/// No message from here holds any part of a value.
/// </summary>
internal static class SecretReader
{
    /// <summary>The most piped input that is read. Far more than any secret, and small enough to refuse the wrong file quickly.</summary>
    private const int MaxPipedBytes = 16 * 1024;

    /// <summary>
    /// Asks on <paramref name="error"/> and reads the keys typed up to Enter, showing none of them. Refuses to
    /// read where input is not a console, because there the terminal itself shows what is typed.
    /// </summary>
    public static string ReadTyped(IConsoleInput console, TextWriter error, SecretInfo secret)
    {
        if (console.IsRedirected)
        {
            throw new UserFacingException(
                "The value cannot be typed here without being shown, because input does not come from a Windows console. "
                + "Run this in PowerShell, cmd or Windows Terminal, or pipe the value in and add --stdin.");
        }

        // The prompt goes to standard error so that redirecting standard output does not hide it.
        error.Write($"Value for the secret {secret.Label} (not shown as you type): ");

        // Collected in an array, not a StringBuilder, so that every copy made here can be overwritten with zeros
        // afterwards. The string that is returned is ordinary managed memory and is not wiped.
        var typed = new char[256];
        var length = 0;
        try
        {
            while (true)
            {
                var key = console.ReadKey();
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    // A character outside the basic plane is two code units, typed as two keys: remove both.
                    var remove = length >= 2 && char.IsSurrogatePair(typed[length - 2], typed[length - 1]) ? 2 : Math.Min(1, length);
                    length -= remove;
                    Array.Clear(typed, length, remove);
                }
                else if (key.KeyChar != '\0')
                {
                    // Control characters are kept, so the caller refuses the value instead of storing one that
                    // silently differs from what was pasted. Keys that type nothing (arrows, F-keys) are ignored.
                    if (length == typed.Length)
                    {
                        var larger = new char[typed.Length * 2];
                        typed.CopyTo(larger, 0);
                        Array.Clear(typed);
                        typed = larger;
                    }

                    typed[length++] = key.KeyChar;
                }
            }

            error.WriteLine();

            // A paste of several lines arrives as keys already waiting. They are read and thrown away so that the
            // shell does not receive them as commands; line endings alone are the end of a one-line paste.
            var moreThanOneLine = false;
            while (console.KeyAvailable)
            {
                moreThanOneLine |= console.ReadKey().Key != ConsoleKey.Enter;
            }

            if (moreThanOneLine)
            {
                throw new UserFacingException($"More than one line was entered for the secret {secret.Label}. A secret is one line of text. Nothing was stored.");
            }

            return new string(typed, 0, length);
        }
        finally
        {
            Array.Clear(typed);
        }
    }

    /// <summary>
    /// Reads the value from piped standard input: UTF-8, or UTF-16 when the input starts with its byte-order
    /// mark. The bytes are decoded here, not through the console's code page.
    /// </summary>
    public static string ReadPiped(IConsoleInput console, SecretInfo secret)
    {
        if (!console.IsRedirected)
        {
            throw new UserFacingException("--stdin reads the value from piped input, and nothing is piped in. Leave --stdin out to type the value.");
        }

        var bytes = new byte[MaxPipedBytes + 1];
        try
        {
            using var input = console.OpenStandardInput();
            var count = input.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > MaxPipedBytes)
            {
                throw new UserFacingException($"The piped input for the secret {secret.Label} is longer than {MaxPipedBytes} bytes, which no secret is. Nothing was stored.");
            }

            var (encoding, markLength) = EncodingOf(bytes.AsSpan(0, count));
            try
            {
                return encoding.GetString(bytes, markLength, count - markLength);
            }
            catch (DecoderFallbackException)
            {
                // The exception's own message can quote the bytes it could not read, so it is not passed on.
                throw new UserFacingException($"The piped value for the secret {secret.Label} is not text in UTF-8, or in UTF-16 with a byte-order mark. Nothing was stored.");
            }
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    /// <summary>The encoding a byte-order mark declares and the mark's length; UTF-8 when there is none. Invalid bytes throw.</summary>
    private static (Encoding Encoding, int MarkLength) EncodingOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 3);
        }

        if (bytes.StartsWith<byte>([0xFF, 0xFE]))
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        }

        if (bytes.StartsWith<byte>([0xFE, 0xFF]))
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        }

        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 0);
    }
}
