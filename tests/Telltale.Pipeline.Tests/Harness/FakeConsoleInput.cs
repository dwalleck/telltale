using System.Text;
using Telltale.Cli;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Stands in for the keyboard and standard input when <c>secrets set</c> reads a value.
/// <see cref="TypeLine"/> and <see cref="Press"/> queue key presses; <see cref="Pipe(byte[])"/> makes
/// standard input a pipe holding those bytes. Reading a key when none is queued fails the test,
/// where the real keyboard would wait.
/// </summary>
public sealed class FakeConsoleInput : IConsoleInput
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    private byte[]? _piped;

    /// <summary>How many key presses the command has read.</summary>
    public int KeysRead { get; private set; }

    /// <summary>How many queued key presses the command left unread, as the shell would then receive them.</summary>
    public int KeysLeft => _keys.Count;

    /// <summary>Whether the command opened standard input.</summary>
    public bool StandardInputOpened { get; private set; }

    public bool IsRedirected => _piped is not null;

    public bool KeyAvailable => _keys.Count > 0;

    /// <summary>Queues one key press per UTF-16 code unit, as the console delivers typed and pasted text. A line feed is the Enter key.</summary>
    public FakeConsoleInput Type(string text)
    {
        foreach (var character in text)
        {
            _keys.Enqueue(character == '\n'
                ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false)
                : new ConsoleKeyInfo(character, ConsoleKey.None, shift: false, alt: false, control: false));
        }

        return this;
    }

    /// <summary>Types the text and presses Enter.</summary>
    public FakeConsoleInput TypeLine(string text) => Type(text).Press(ConsoleKey.Enter);

    /// <summary>Queues a key that is not a character: Enter, Backspace, an arrow.</summary>
    public FakeConsoleInput Press(ConsoleKey key)
    {
        var character = key switch { ConsoleKey.Enter => '\r', ConsoleKey.Backspace => '\b', _ => '\0' };
        _keys.Enqueue(new ConsoleKeyInfo(character, key, shift: false, alt: false, control: false));
        return this;
    }

    /// <summary>Makes standard input a pipe holding exactly these bytes.</summary>
    public void Pipe(byte[] bytes) => _piped = bytes;

    /// <summary>Makes standard input a pipe holding the text as UTF-8 without a byte-order mark.</summary>
    public void Pipe(string text) => Pipe(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));

    public ConsoleKeyInfo ReadKey()
    {
        if (IsRedirected)
        {
            throw new InvalidOperationException("The console refuses to read a key when standard input is redirected.");
        }

        if (_keys.Count == 0)
        {
            throw new InvalidOperationException("The command waited for a key the test did not type.");
        }

        KeysRead++;
        return _keys.Dequeue();
    }

    public Stream OpenStandardInput()
    {
        StandardInputOpened = true;
        return new MemoryStream(_piped ?? []);
    }
}
