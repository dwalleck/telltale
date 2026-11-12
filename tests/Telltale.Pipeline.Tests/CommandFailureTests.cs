using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>Failures of the machine, not of Jira or the configuration: each ends in one plain line, never a stack trace.</summary>
public class CommandFailureTests
{
    [Test]
    public async Task A_database_that_cannot_be_opened_is_reported_plainly()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        Directory.CreateDirectory(pipeline.DatabasePath);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        var error = pipeline.Error.ToString();
        await Assert.That(error).StartsWith($"The database {pipeline.DatabasePath} could not be used: ");
        await Assert.That(error).Contains("unable to open database file");
        await Assert.That(error).DoesNotContain("   at ");
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_report_that_cannot_be_written_is_reported_plainly()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await File.WriteAllTextAsync(pipeline.ReportDirectory, "a file where the report directory should be");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        var error = pipeline.Error.ToString();
        await Assert.That(error).StartsWith("A file could not be read or written: ");
        await Assert.That(error).Contains(pipeline.ReportDirectory);
        await Assert.That(error).DoesNotContain("   at ");
    }

    [Test]
    public async Task Cancelling_during_the_sync_ends_the_run_without_a_stack_trace_and_stores_nothing()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.BeforeResponse = pipeline.Cancellation.Cancel;

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        // 130 is the conventional exit code for a run ended by Ctrl+C.
        await Assert.That(exit).IsEqualTo(130);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Cancelled.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();

        // The store exists by now, but the batch is written only after the whole fetch: the issue's key is not in it.
        var files = await pipeline.ReadAllFilesAsync();
        await Assert.That(files.Keys).Contains("store.db");
        await Assert.That(string.Concat(files.Values)).DoesNotContain(pipeline.Jira.IdOf("FAKE-1"));
    }

    private static void AddSprintAndIssue(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
    }
}
