using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A sprint whose pull requests are on two hosts (PR timing, ticket 08): each host has its own rows and the
/// two together have a combined row, whose figures are taken of the pooled waits and not of the hosts'
/// figures. With one host configured there is no combined row: the host's rows are all there is. The synthetic
/// hosts are <see cref="FakeGitHub"/> and <see cref="FakeAdo"/>; sprint 42 runs from Monday 19 January 10:30
/// to Monday 2 February 2026 09:30, and there are no holidays.
/// </summary>
public class PullRequestHostScopeTests
{
    private const string MergeFiguresOfSprint42 =
        "SELECT host_scope, coverage, sample_count, median_elapsed_days, p85_elapsed_days, median_business_days, p85_business_days "
        + "FROM pr_timing_per_sprint WHERE sprint_id = '42' AND measure = 'merge' ORDER BY host_scope_position;";

    private const string TableHeadWithHosts =
        "| Wait from opening to | Host | Pull requests | Median (elapsed days) | P85 (elapsed days) | Median (business days) | P85 (business days) |\n"
        + "| --- | --- | ---: | ---: | ---: | ---: | ---: |\n";

    /// <summary>
    /// Two merges on GitHub and five on Azure DevOps, all in sprint 42, every time at noon:
    ///
    ///   host    PR  opened   merged   elapsed  business
    ///   GitHub  1   Tue 20   Wed 21   1        1
    ///   GitHub  2   Mon 19   Wed 28   9        7    (one weekend)
    ///   ADO     1   Tue 20   Thu 22   2        2
    ///   ADO     2   Fri 23   Sun 25   2        0.5  (Friday's second half; the weekend counts nothing)
    ///   ADO     3   Tue 20   Fri 23   3        3
    ///   ADO     4   Fri 23   Mon 26   3        1    (half of Friday and half of Monday)
    ///   ADO     5   Mon 26   Fri 30   4        4
    ///
    /// The numbers 1 and 2 are on both hosts, and are four pull requests.
    /// </summary>
    private static void AddTwoMergesOnGitHubAndFiveOnAzureDevOps(PipelineHarness pipeline)
    {
        pipeline.Jira.AddSprints41And42();
        var github = FakeGitHub.For(pipeline);
        github.AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));
        github.AddPullRequest(2, Accounts.AuthorOne, Local(2026, 1, 19, 12)).Merged(Local(2026, 1, 28, 12));
        var ado = FakeAdo.For(pipeline);
        ado.AddPullRequest(1, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Completed(Local(2026, 1, 22, 12));
        ado.AddPullRequest(2, AdoIdentities.AuthorOne, Local(2026, 1, 23, 12)).Completed(Local(2026, 1, 25, 12));
        ado.AddPullRequest(3, AdoIdentities.AuthorOne, Local(2026, 1, 20, 12)).Completed(Local(2026, 1, 23, 12));
        ado.AddPullRequest(4, AdoIdentities.AuthorOne, Local(2026, 1, 23, 12)).Completed(Local(2026, 1, 26, 12));
        ado.AddPullRequest(5, AdoIdentities.AuthorOne, Local(2026, 1, 26, 12)).Completed(Local(2026, 1, 30, 12));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task The_combined_figures_are_of_the_pooled_waits_and_not_the_mean_of_the_hosts_figures_whatever_the_minimum_group_size(bool checksOff)
    {
        // Elapsed days. GitHub: 1, 9: median 5, P85 (rank ceil(0.85 x 2) = 2) 9. Azure DevOps: 2, 2, 3, 3, 4:
        // median 3, P85 (rank ceil(4.25) = 5) 4. Pooled: 1, 2, 2, 3, 3, 4, 9: median the 4th, 3; P85 (rank
        // ceil(5.95) = 6) 4. The mean of the hosts' medians would be 4 and of their P85s 6.5.
        // Business days. GitHub: 1, 7: median 4, P85 7. Azure DevOps: 0.5, 1, 2, 3, 4: median 2, P85 4.
        // Pooled: 0.5, 1, 1, 2, 3, 4, 7: median 2, P85 (the 6th) 4. The means would be 3 and 5.5.
        // One author on each host, under the default numbers (five and three) and with both checks off:
        // pull request figures are shown either way (owner decision D114).
        await using var pipeline = checksOff ? SuppressionOff() : new PipelineHarness();
        AddTwoMergesOnGitHubAndFiveOnAzureDevOps(pipeline);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, MergeFiguresOfSprint42)).IsEqualTo(
            """
            | host_scope | coverage | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | github | read | 2 | 5 | 9 | 4 | 7 |
            | ado | read | 5 | 3 | 4 | 2 | 4 |
            | combined | complete | 7 | 3 | 4 | 2 | 4 |

            """.ReplaceLineEndings("\n"));
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains(
            "\n## Pull request timing\n\n" + TableHeadWithHosts
            + "| First code review | GitHub | 0 | none | none | none | none |\n"
            + "| First code review | Azure DevOps | 0 | none | none | none | none |\n"
            + "| First code review | Combined | 0 | none | none | none | none |\n"
            + "| First code approval | GitHub | 0 | none | none | none | none |\n"
            + "| First code approval | Azure DevOps | 0 | none | none | none | none |\n"
            + "| First code approval | Combined | 0 | none | none | none | none |\n"
            + "| Merge | GitHub | 2 | 5.0 | 9.0 | 4.0 | 7.0 |\n"
            + "| Merge | Azure DevOps | 5 | 3.0 | 4.0 | 2.0 | 4.0 |\n"
            + "| Merge | Combined | 7 | 3.0 | 4.0 | 2.0 | 4.0 |\n"
            + "\n- Each host has its own rows, and a combined row holds the waits of every host together: its median and P85 are taken of those waits, never of the hosts' medians or P85s.\n"
            + "- These are the pull requests of the configured repositories: every one of them, linked to a Jira issue or not, whoever opened it. They are not necessarily this team's pull requests, and no team is inferred from them.\n");
        await Assert.That(report).Contains("- Pull request figures are not subject to the minimum group size: none is withheld for the number of people behind it, and none is reported per person.\n");

        // Nothing either host says of a person or a pull request's text is in the report.
        foreach (var forbidden in FakeGitHubText().Concat(FakeAdoText()))
        {
            await Assert.That(report).DoesNotContain(forbidden);
        }
    }

    [Test]
    public async Task With_one_host_configured_its_rows_are_the_only_ones_and_the_report_has_no_host_column()
    {
        await using var pipeline = SuppressionOff();
        pipeline.Jira.AddSprints41And42();
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 20, 12)).Merged(Local(2026, 1, 21, 12));

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        await Assert.That(await HarnessSteps.QueryAsync(pipeline, MergeFiguresOfSprint42)).IsEqualTo(
            """
            | host_scope | coverage | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- |
            | github | read | 1 | 1 | 1 | 1 | 1 |

            """.ReplaceLineEndings("\n"));
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("| Merge | 1 | 1.0 | 1.0 | 1.0 | 1.0 |\n\n- These are");
        await Assert.That(report).DoesNotContain("| Host |");
        await Assert.That(report).DoesNotContain("Combined");
    }

    /// <summary>The logins and account IDs the synthetic GitHub serves: none may be in a report.</summary>
    private static IEnumerable<string> FakeGitHubText() =>
        Accounts.All.SelectMany(account => new[] { account.Login, account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });

    /// <summary>The names and IDs of the synthetic Azure DevOps identities: none may be in a report.</summary>
    private static IEnumerable<string> FakeAdoText() =>
        AdoIdentities.All.SelectMany(identity => new[] { identity.Id, identity.DisplayName, identity.UniqueName });
}
