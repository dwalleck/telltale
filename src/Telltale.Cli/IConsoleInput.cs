namespace Telltale.Cli;

/// <summary>The keyboard and standard input, as <c>secrets set</c> reads a secret's value from them.</summary>
public interface IConsoleInput
{
    /// <summary>True when standard input is not a console: a pipe, a file, or a terminal Windows does not treat as one.</summary>
    bool IsRedirected { get; }

    /// <summary>True when a key press is waiting to be read.</summary>
    bool KeyAvailable { get; }

    /// <summary>Waits for the next key press and returns it without showing it.</summary>
    ConsoleKeyInfo ReadKey();

    /// <summary>Standard input as bytes, for a value that is piped in.</summary>
    Stream OpenStandardInput();
}

/// <summary>The real console.</summary>
public sealed class SystemConsoleInput : IConsoleInput
{
    public bool IsRedirected => Console.IsInputRedirected;

    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

    public Stream OpenStandardInput() => Console.OpenStandardInput();
}
