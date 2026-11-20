using TUnit.Core.Interfaces;

// How many tests of an assembly run at once. This file is compiled into each test project, so each has its own
// limit: while both run, twice as many tests run at once.
[assembly: ParallelLimiter<Telltale.Tests.Shared.TestParallelism>]

namespace Telltale.Tests.Shared;

/// <summary>
/// At most four tests of a test project at once, so a plain <c>dotnet test</c> leaves a developer's machine usable:
/// without a limit the pipeline tests take every core for the length of the run. <c>TELLTALE_TEST_PARALLELISM</c>
/// sets another whole number for a machine that has cores to spare, and zero turns the limit off. Any other value
/// stops the run, so a mistyped value is not taken for the default.
/// </summary>
public sealed class TestParallelism : IParallelLimit
{
    private const string Variable = "TELLTALE_TEST_PARALLELISM";
    private const int Default = 4;

    public int Limit { get; } = LimitOf(Environment.GetEnvironmentVariable(Variable));

    private static int LimitOf(string? asked) =>
        string.IsNullOrWhiteSpace(asked) ? Default
        : int.TryParse(asked, out var limit) && limit >= 0 ? (limit == 0 ? int.MaxValue : limit)
        : throw new InvalidOperationException($"{Variable} is '{asked}': it must be a whole number of zero or more, and zero turns the limit off.");
}
