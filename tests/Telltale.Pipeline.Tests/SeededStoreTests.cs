using System.Text.Json.Nodes;
using Telltale.Cli;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The synthetic store the developer tool <c>eng/SeedStore</c> writes (<see cref="SeededStore"/>) is one the
/// pipeline accepts: with the files laid out as the executable lays them out, no Jira to call and no secret
/// set, <c>derive</c> and <c>report</c> run on it and give the figures its history was written to give.
/// </summary>
public class SeededStoreTests
{
    [Test]
    public async Task Derive_and_report_run_on_a_seeded_store_without_jira_or_secrets_and_give_the_figures_of_its_history()
    {
        using var seeded = new SeededDirectory();
        var thresholds = new JsonObject { [Workflow.Development.Id] = 5 };

        await Assert.That(await seeded.SeedAsync(minimumGroupSize: null, thresholds)).IsEqualTo(0);
        await Assert.That(await seeded.RunAsync("derive")).IsEqualTo(0);
        await Assert.That(await seeded.RunAsync("report")).IsEqualTo(0);

        // Without --sprint the report is of sprint 42, the most recently completed one.
        var report = await seeded.ReadReportAsync(SeededStore.ReportedSprintId);
        await Assert.That(report).StartsWith("# Fake Sprint 42 (2026-01-19 to 2026-02-02)\n\nDelivered issues: 11\n");

        // The 3-point issues took 2, 2 and 3 days, and the reopened one 5: Tuesday to the Tuesday after.
        // Sprint 41, the baseline, has 2, 3 and 4.
        await Assert.That(report).Contains(ReportText.CycleTimeHeaderWithBaseline + "| 3 | 4 | 2.5 | 5.0 | 2.5 | 7.0 | 3.0 | 4.0 |\n");
        await Assert.That(report).Contains("\n| Fake Blocked | 1 | suppressed | suppressed | none |\n");
        await Assert.That(report).EndsWith("\n- Fake Development: 4 issues had a visit shorter than 5 minutes\n");
        await Assert.That(seeded.Jira.Requests).IsEmpty();
    }

    [Test]
    public async Task A_seeded_store_takes_the_minimum_group_size_it_is_given_and_a_refused_setting_fails_the_seeding()
    {
        using var seeded = new SeededDirectory();

        // The team is six people: a minimum team of seven withholds the sprint.
        await Assert.That(await seeded.SeedAsync(Scenarios.MinimumGroupSize(team: 7, assigneesBehindAFigure: 3), briefVisitThresholds: null)).IsEqualTo(0);
        await Assert.That(await seeded.RunAsync("derive")).IsEqualTo(0);
        await Assert.That(await seeded.RunAsync("report", "--sprint", "42")).IsEqualTo(0);
        await Assert.That(await seeded.ReadReportAsync(SeededStore.ReportedSprintId)).Contains("\nDelivered issues: suppressed\n");

        // A done status cannot be listed for brief visits: the tool's own rule answers, and the seeding fails.
        var refused = new JsonObject { [Workflow.Done.Id] = 5 };
        await Assert.That(await seeded.SeedAsync(minimumGroupSize: null, refused)).IsNotEqualTo(0);
        await Assert.That(seeded.Error.ToString()).Contains("briefVisitThresholds: a status with the role done cannot be listed, and one is.");
    }

    /// <summary>A temporary working directory, seeded and then used as the executable would use it.</summary>
    private sealed class SeededDirectory : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("telltale-seeded-").FullName;

        /// <summary>Has no route: a command that called Jira would fail, and the request would be listed.</summary>
        public StubHttpHandler Jira { get; } = new();

        public StringWriter Error { get; } = new();

        public Task<int> SeedAsync(JsonObject? minimumGroupSize, JsonObject? briefVisitThresholds) =>
            SeededStore.WriteAsync(_directory, minimumGroupSize, briefVisitThresholds, TextWriter.Null, Error);

        /// <summary>Runs one command line with the paths of <see cref="AppServices.ForProduction"/> under the directory, and no secret.</summary>
        public Task<int> RunAsync(params string[] args) => CliApp.RunAsync(args, new AppServices(
            Jira,
            new SkippingClock(Local(2026, 2, 16, 12)),
            new InMemorySecretStore(new Dictionary<SecretName, string>()),
            TextWriter.Null,
            Error,
            Path.Combine(_directory, "config", "telltale.json"),
            Path.Combine(_directory, "data", "telltale.db"),
            Path.Combine(_directory, "reports")));

        public async Task<string> ReadReportAsync(long sprintId) =>
            (await File.ReadAllTextAsync(Path.Combine(_directory, "reports", $"sprint-{sprintId}.md"))).ReplaceLineEndings("\n");

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
            Error.Dispose();
        }
    }
}
