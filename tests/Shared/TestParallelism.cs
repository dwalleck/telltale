using TUnit.Core.Interfaces;

// How many tests of an assembly run at once. This file is compiled into each test project, so it holds for both.
[assembly: ParallelLimiter<Telltale.Tests.Shared.TestParallelism>]

namespace Telltale.Tests.Shared;

/// <summary>
/// At most four tests at once, so a plain <c>dotnet test</c> leaves a developer's machine usable: without a
/// limit the pipeline tests take every core for the length of the run. <c>TELLTALE_TEST_PARALLELISM</c> sets
/// another number for a machine that has cores to spare.
/// </summary>
public sealed class TestParallelism : IParallelLimit
{
    private const int Default = 4;

    public int Limit { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("TELLTALE_TEST_PARALLELISM"), out var asked) && asked > 0 ? asked : Default;
}
