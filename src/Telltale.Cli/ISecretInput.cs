using System.Text;

namespace Telltale.Cli;

/// <summary>Where <c>secrets set</c> gets a secret's value from. What is typed is never shown.</summary>
public interface ISecretInput
{
    /// <summary>Asks for one value and returns it, or null when the input has ended.</summary>
    /// <param name="prompt">What to ask. It holds the secret's name, never a value.</param>
    string? Read(string prompt);
}

/// <summary>
/// Reads a secret from the keyboard without echoing it. When standard input is redirected (a pipe or a
/// file), it reads one line from it and shows no prompt.
/// </summary>
public sealed class ConsoleSecretInput : ISecretInput
{
    public string? Read(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine();
        }

        // The prompt goes to standard error so that redirecting standard output does not hide it.
        Console.Error.Write(prompt);
        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                typed.Length = Math.Max(0, typed.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return typed.ToString();
    }
}
