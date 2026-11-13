using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// The privacy scenario and the scan of what a run over it wrote. One copy for the test of the rule under the
/// default numbers (PrivacyTests) and the test of the same rule under suppression off (SuppressionOffTests;
/// owner decision D68), so the two cannot drift apart.
/// </summary>
public static class PrivacyScan
{
    /// <summary>
    /// Two assigned issues delivered in sprint 41 and one unassigned issue still in development. FAKE-1 is
    /// resolved and was moved by someone who is not its assignee; the other moves are the board admin's.
    /// </summary>
    public static void AddScenario(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).AssignedTo(People.One)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development, by: People.Three)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done, by: People.Three)
            .Resolved("300001");
        jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).AssignedTo(People.Two)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 11), Workflow.Done);
        jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 12, 9), Workflow.Development);
    }

    /// <summary>
    /// Scans everything a run over <see cref="AddScenario"/> left behind (every file, standard output and
    /// standard error) for the personal and free text the fake served, and fails the test on any of it.
    /// </summary>
    public static async Task AssertNothingForbiddenWasWrittenAsync(PipelineHarness pipeline)
    {
        var files = await pipeline.ReadAllFilesAsync();

        // The scan is only worth something if it reads the files the data is in: the issue ID is
        // stored and the sprint name is reported, so both must be found.
        await Assert.That(files.Keys).Contains("store.db");
        await Assert.That(files["store.db"]).Contains(pipeline.Jira.IdOf("FAKE-1"));
        await Assert.That(files.Keys).Contains(Path.Combine("reports", "sprint-41.md"));
        await Assert.That(files[Path.Combine("reports", "sprint-41.md")]).Contains("Fake Sprint 41");

        var forbidden = pipeline.Jira.ForbiddenText;
        await Assert.That(forbidden).Contains(People.One.AccountId);
        await Assert.That(forbidden).Contains(People.Two.DisplayName);
        await Assert.That(forbidden).Contains(People.One.Email);
        await Assert.That(forbidden).Contains(pipeline.Jira.Issues[0].Summary);
        await Assert.That(forbidden).Contains(pipeline.Jira.Issues[0].Description);

        // Free text and people the fake serves beyond the assignee: the changelog authors, the sprint goal,
        // and the resolution and status descriptions.
        await Assert.That(forbidden).Contains(People.Three.DisplayName);
        await Assert.That(forbidden).Contains(People.BoardAdmin.AccountId);
        await Assert.That(forbidden).Contains("Synthetic goal of sprint 41 that must never be stored");
        await Assert.That(forbidden).Contains(FakeJira.ResolutionDescription);
        await Assert.That(forbidden).Contains(FakeJira.StatusDescription);

        var written = files.Select(file => (Where: file.Key, Text: file.Value))
            .Append((Where: "standard output", Text: pipeline.Out.ToString()))
            .Append((Where: "standard error", Text: pipeline.Error.ToString()));
        var leaks = (from place in written
                     from text in forbidden
                     where place.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                     select $"'{text}' in {place.Where}").ToList();
        await Assert.That(leaks).IsEmpty();
    }
}
