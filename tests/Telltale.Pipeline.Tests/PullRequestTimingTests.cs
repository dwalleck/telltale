using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

public class PullRequestTimingTests
{
    [Test]
    public async Task UnlinkedPullRequestCountsReviewBeforeMergeInSeparateSprints()
    {
        await using var pipeline = new PipelineHarness(Local(2026, 2, 2, 12));
        pipeline.Jira.AddSprints41And42(); // No Jira issues or development links.
        pipeline.Jira.AddSprint(43, "Fake Sprint 43", Local(2026, 2, 2, 10), Local(2026, 2, 16, 9));
        pipeline.Config["minimumGroupSize"] = new JsonObject { ["team"] = 0, ["assigneesBehindAFigure"] = 0 };
        pipeline.Config["github"] = JsonNode.Parse("""{"repositories":["fake/sample"],"opsTeam":"fake/ops"}""");

        var merged = false;
        var author = new { id = 1, login = "fake-author", type = "User" };
        var ops = new { id = 2, login = "fake-ops", type = "User" };
        var reviewer = new { id = 3, login = "fake-reviewer", type = "User" };
        const string pulls = "/repos/fake/sample/pulls";
        var host = pipeline.Jira.Handler;
        host.On(HttpMethod.Get, pulls, request => StubHttpHandler.Json(JsonSerializer.Serialize(new[] { new
        {
            number = 1, user = author, title = "Synthetic change", head = new { @ref = "feature" },
            created_at = "2026-01-16T18:00:00Z", merged_at = merged ? "2026-02-03T18:00:00Z" : null,
            state = merged ? "closed" : "open", draft = false,
        } }))(request));
        host.On(HttpMethod.Get, "/orgs/fake/teams/ops/members", StubHttpHandler.Json(JsonSerializer.Serialize(new[] { ops })));
        host.On(HttpMethod.Get, pulls + "/1/reviews", StubHttpHandler.Json(JsonSerializer.Serialize(new[]
        {
            new { id = 10, user = ops, state = "APPROVED", submitted_at = "2026-01-16T19:00:00Z" },
            new { id = 11, user = reviewer, state = "COMMENTED", submitted_at = "2026-01-20T18:00:00Z" },
        })));

        // Public metric rows with an observed wait: sprint, measure, PR count, median elapsed days.
        const string figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 ORDER BY sprint_id, measure";
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var before = await pipeline.QueryAsync<(string, string, long, double)>(figures);
        await Assert.That(before.ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 4d) });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("First code review");

        merged = true;
        pipeline.Clock.Advance(TimeSpan.FromDays(2));
        await pipeline.RunAllAsync(["run", "--sprint", "43"]);
        var after = await pipeline.QueryAsync<(string, string, long, double)>(figures);
        await Assert.That(after.ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 4d), ("43", "merge", 1L, 18d) });
    }
}
