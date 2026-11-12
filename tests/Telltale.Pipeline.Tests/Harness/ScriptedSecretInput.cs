using Telltale.Cli;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// Stands in for the keyboard when <c>secrets set</c> asks for a value: answers come from
/// <see cref="Answers"/> in order, and every question asked is kept in <see cref="Prompts"/>.
/// When the answers run out, the input has ended, as a closed pipe would.
/// </summary>
public sealed class ScriptedSecretInput : ISecretInput
{
    public Queue<string> Answers { get; } = new();

    public List<string> Prompts { get; } = [];

    public string? Read(string prompt)
    {
        Prompts.Add(prompt);
        return Answers.TryDequeue(out var answer) ? answer : null;
    }
}
