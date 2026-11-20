using System.Text.Json.Nodes;
using Telltale.Core.Secrets;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The <c>github</c> setting and what the store's pull request rows mean for the commands around a sync
/// (PR timing, ticket 03): what the setting refuses, that <c>discover</c> keeps it, that a changed repository
/// list needs a derive, and that pull request authors are people the hashing salt is bound to, as Jira
/// assignees are.
/// </summary>
public class PullRequestSettingsTests
{
    private const string Figures = "SELECT sprint_id, measure, sample_count, median_elapsed_days FROM pr_timing_per_sprint WHERE sample_count > 0 ORDER BY sprint_id, measure_position";

    /// <summary>Stands for a secret pasted into the wrong place: nothing printed may hold any part of it.</summary>
    private const string Pasted = "synthetic-pasted";

    /// <summary>Sprints 41 and 42 and one pull request of one author, reviewed in sprint 42 after two days. No Jira issue.</summary>
    private static FakeGitHub AddOneReviewedPullRequest(PipelineHarness pipeline)
    {
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 22, 12));
        return github;
    }

    [Test]
    [Arguments("""{"repositories":[]}""", "github.repositories must list at least one repository as owner/name.")]
    [Arguments("""{"repositories":["synthetic-pasted"]}""", "github.repositories: every entry must be a repository written as owner/name, and one is not.")]
    [Arguments("""{"repositories":["fake-org/fake-sample/synthetic-pasted"]}""", "github.repositories: every entry must be a repository written as owner/name, and one is not.")]
    [Arguments("""{"repositories":["fake-org/fake sample?synthetic-pasted"]}""", "github.repositories: every entry must be a repository written as owner/name, and one is not.")]
    [Arguments("""{"repositories":["fake-org/fake-sample"],"opsTeam":"synthetic-pasted"}""", "github.opsTeam must be a team written as organization/team-slug.")]
    [Arguments("""{"repositories":["fake-org/fake-sample"],"apiUrl":"http://synthetic-pasted.example.invalid/"}""", "github.apiUrl must be an absolute https:// address.")]
    [Arguments("""{"repositories":["fake-org/fake-sample"],"apiUrl":"https://owner:synthetic-pasted@github.example.invalid/"}""", "github.apiUrl must not carry a user name or password.")]
    public async Task A_github_setting_the_tool_cannot_use_is_refused_by_name_before_anything_is_asked_and_is_not_repeated(string github, string refusal)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Config["github"] = JsonNode.Parse(github);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(pipeline.Error.ToString()).DoesNotContain(Pasted);
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_token_written_into_the_github_setting_is_refused_and_its_value_is_not_shown()
    {
        // Secrets never live in the configuration file: the setting has no place for one.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Config["github"] = JsonNode.Parse($$"""{"repositories":["fake-org/fake-sample"],"token":"{{Pasted}}"}""");

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("telltale.json is not valid: ").And.DoesNotContain(Pasted);
        await Assert.That(pipeline.Jira.Handler.Requests).IsEmpty();
    }

    [Test]
    public async Task A_rerun_of_discover_keeps_the_github_setting_as_the_file_has_it()
    {
        await using var pipeline = new PipelineHarness();
        await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);
        var github = JsonNode.Parse("""{"apiUrl":"https://github.example.invalid/api/v3/","repositories":["fake-org/fake-sample"],"opsTeam":"fake-org/fake-ops"}""")!;
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["github"] = github.DeepClone();
            config["timeZone"] = "Europe/London";
        });

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That((await ReadWrittenConfigAsync(pipeline))["github"]!.ToJsonString()).IsEqualTo(github.ToJsonString());

        // discover asks nothing of a pull request host.
        await Assert.That(pipeline.Jira.Handler.Requests.Where(request => !request.Path.StartsWith("/rest/", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task A_changed_repository_list_needs_a_derive_and_then_only_the_listed_repositories_count()
    {
        await using var pipeline = SuppressionOff();
        AddOneReviewedPullRequest(pipeline);
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 2d) });

        pipeline.Config["github"]!["repositories"] = new JsonArray("fake-org/fake-other");
        var exit = await pipeline.RunAsync("report", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The pull request repositories in the configuration file changed since the derived data was built. Run derive first.\n");

        // The stored pull request is of a repository that is no longer listed: it stays in the store and is in no figure.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);
        await Assert.That(await pipeline.QueryAsync<(string, string, long, double)>(Figures)).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("| First code review | 0 | none | none | none | none |\n| First code approval | 0 | none | none | none | none |\n| Merge | 0 | none | none | none | none |\n");
    }

    [Test]
    public async Task Removing_the_github_setting_takes_the_pull_request_section_out_of_the_report_after_a_derive()
    {
        await using var pipeline = SuppressionOff();
        AddOneReviewedPullRequest(pipeline);
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("## Pull request timing");

        pipeline.Config.Remove("github");
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).DoesNotContain("Pull request");
        await Assert.That(await pipeline.QueryAsync<long>("SELECT COUNT(*) FROM pr_timing_per_sprint")).IsEquivalentTo(new[] { 0L });
    }

    [Test]
    public async Task A_store_whose_only_people_are_pull_request_authors_is_bound_to_its_salt()
    {
        // No Jira issue has an assignee here, so the author of the pull request is the one hashed person.
        await using var pipeline = SuppressionOff();
        AddOneReviewedPullRequest(pipeline);
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        // Without the salt: it cannot be generated anew, because someone is hashed with the one that is gone.
        pipeline.Secrets.Remove(SecretName.HashingSalt);
        var withoutSalt = await pipeline.RunAsync("sync");
        await Assert.That(withoutSalt).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("the database already holds people hashed with the salt that was there before");

        // And a store that holds such a person without saying which salt hashed it is under no salt the tool has.
        pipeline.Secrets[SecretName.HashingSalt] = PipelineHarness.HashingSalt;
        await pipeline.ExecuteAsync("DELETE FROM person_hashing;");
        pipeline.Error.GetStringBuilder().Clear();
        var unknownSalt = await pipeline.RunAsync("sync");
        await Assert.That(unknownSalt).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).StartsWith("The hashing salt in the secret store is not the one the people in the database were hashed with");
    }

    [Test]
    public async Task A_backfill_keeps_the_pull_requests_and_under_a_new_salt_hashes_their_authors_again()
    {
        await using var pipeline = SuppressionOff();
        var github = AddOneReviewedPullRequest(pipeline);
        await pipeline.RunAllAsync(["run", "--sprint", "42"]);
        var before = (await pipeline.QueryAsync<string>("SELECT author_hash FROM pull_request")).Single();

        // The reviewer joins the Ops team: were the backfill to drop the stored review and read it anew, it would no longer count.
        github.OpsMembers.Add(Accounts.Reviewer);
        pipeline.Secrets[SecretName.HashingSalt] = "synthetic-salt-two";
        await pipeline.RunAllAsync(["backfill", "--allow-empty"], ["derive"]);

        var after = (await pipeline.QueryAsync<string>("SELECT author_hash FROM pull_request")).Single();
        await Assert.That(after).IsNotEqualTo(before);
        await Assert.That(await pipeline.ReadDatabaseBytesAsync()).DoesNotContain(before);
        await Assert.That((await pipeline.QueryAsync<(string, string, long, double)>(Figures)).ToArray()).IsEquivalentTo(new[] { ("42", "first_code_review", 1L, 2d) });
    }
}
