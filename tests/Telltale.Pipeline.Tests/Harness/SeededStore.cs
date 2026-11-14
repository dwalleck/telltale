using System.Text.Json;
using System.Text.Json.Nodes;
using Telltale.Cli;
using Telltale.Core.Secrets;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// A synthetic store on disk, laid out as the <c>telltale</c> executable lays its files out, for trying a
/// published executable without a Jira site: <c>config/telltale.json</c> and <c>data/telltale.db</c> under a
/// working directory. The store is filled by the real <c>backfill</c> command from a <see cref="FakeJira"/>,
/// so it holds what collection would have stored. The developer tool <c>eng/SeedStore</c> is a command line
/// over <see cref="WriteAsync"/>; it lives here so that a test can hold it to what the pipeline accepts.
/// </summary>
public static class SeededStore
{
    /// <summary>The earlier sprint, listed under <c>baselineSprintIds</c>.</summary>
    public const long BaselineSprintId = 41;

    /// <summary>The later sprint: the most recently completed one, which <c>report</c> picks without <c>--sprint</c>.</summary>
    public const long ReportedSprintId = 42;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// The synthetic history: sprints 41 and 42, both completed, and a team of six.
    /// <list type="bullet">
    /// <item>Each sprint delivers three 3-point issues of three people and three 5-point issues of the other
    /// three, with cycle times of two to six business days.</item>
    /// <item>Each sprint has four unestimated catch-up histories of four people: in New for two days, then
    /// twenty seconds in Development and two days in Review.</item>
    /// <item>Sprint 41 leaves one issue in Development. Sprint 42 leaves one in Development and one in Blocked.</item>
    /// <item>Sprint 42 has a reopen: FAKE-211 was done on Thursday, moved back to Development on Friday and
    /// done again the next Tuesday.</item>
    /// </list>
    /// </summary>
    public static void AddHistory(FakeJira jira)
    {
        jira.AddSprints41And42();
        var (one, two, three, four, five, six) = (People.One, People.Two, People.Three, People.Four, People.Five, People.Six);

        Delivered(jira, 41, "FAKE-101", points: 3, one, days: 1, reviewDays: 1);
        Delivered(jira, 41, "FAKE-102", points: 3, two, days: 1, reviewDays: 2);
        Delivered(jira, 41, "FAKE-103", points: 3, three, days: 2, reviewDays: 2);
        Delivered(jira, 41, "FAKE-104", points: 5, four, days: 3, reviewDays: 1);
        Delivered(jira, 41, "FAKE-105", points: 5, five, days: 3, reviewDays: 2);
        Delivered(jira, 41, "FAKE-106", points: 5, six, days: 4, reviewDays: 2);
        CaughtUp(jira, "FAKE-107", one, 41);
        CaughtUp(jira, "FAKE-108", two, 41);
        CaughtUp(jira, "FAKE-109", three, 41);
        CaughtUp(jira, "FAKE-110", four, 41);
        StillInDevelopment(jira, 41, "FAKE-111", five);

        Delivered(jira, 42, "FAKE-201", points: 3, one, days: 1, reviewDays: 1);
        Delivered(jira, 42, "FAKE-202", points: 3, two, days: 2);
        Delivered(jira, 42, "FAKE-203", points: 3, three, days: 2, reviewDays: 1);
        Delivered(jira, 42, "FAKE-204", points: 5, four, days: 3);
        Delivered(jira, 42, "FAKE-205", points: 5, five, days: 2, reviewDays: 2);
        Delivered(jira, 42, "FAKE-206", points: 5, six, days: 3, reviewDays: 1);
        CaughtUp(jira, "FAKE-207", four, 42);
        CaughtUp(jira, "FAKE-208", five, 42);
        CaughtUp(jira, "FAKE-209", six, 42);
        CaughtUp(jira, "FAKE-210", one, 42);

        // The reopen: two days to the first Done, a day there, and two more days of work after it.
        Delivered(jira, 42, "FAKE-211", points: 3, two, days: 2)
            .Moved(Local(2026, 1, 23, 9), Workflow.Development)
            .Moved(Local(2026, 1, 27, 9), Workflow.Done);

        StillInDevelopment(jira, 42, "FAKE-212", three);
        StillInDevelopment(jira, 42, "FAKE-213", six).Moved(Local(2026, 1, 28, 9), Workflow.Blocked);
    }

    /// <summary>
    /// Writes the configuration file and fills the store under <paramref name="directory"/>, and returns the
    /// exit code of the <c>backfill</c> that filled it: 0, or what the tool answers to a setting it refuses,
    /// with its message on <paramref name="error"/>. Nothing is derived: that is the executable's to do.
    /// </summary>
    /// <param name="directory">The working directory the executable will be run from.</param>
    /// <param name="minimumGroupSize">The <c>minimumGroupSize</c> setting, or null to leave it out.</param>
    /// <param name="briefVisitThresholds">The <c>briefVisitThresholds</c> setting, or null to leave it out.</param>
    /// <param name="output">Where <c>backfill</c> writes what it would write to standard output.</param>
    /// <param name="error">Where <c>backfill</c> writes what it would write to standard error.</param>
    public static async Task<int> WriteAsync(string directory, JsonObject? minimumGroupSize, JsonObject? briefVisitThresholds, TextWriter output, TextWriter error)
    {
        var jira = new FakeJira();
        AddHistory(jira);

        var config = new JsonObject
        {
            ["jira"] = new JsonObject
            {
                ["siteUrl"] = FakeJira.SiteUrl,
                ["boardId"] = FakeJira.BoardId,
                ["projectKeys"] = new JsonArray(FakeJira.ProjectKey),
                ["sprintFieldId"] = FakeJira.SprintFieldId,
                ["storyPointFieldId"] = FakeJira.StoryPointFieldId,
                ["backfillStart"] = "2025-01-01",
            },
            ["statusRoles"] = new JsonObject(Workflow.BoardOrder.Select(status => KeyValuePair.Create(status.Id, (JsonNode?)status.Role))),
            ["abandonedResolutionIds"] = new JsonArray(),
            ["timeZone"] = ZoneId,
            ["holidays"] = new JsonArray(),
            ["baselineSprintIds"] = new JsonArray(BaselineSprintId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (minimumGroupSize is not null)
        {
            config["minimumGroupSize"] = minimumGroupSize;
        }

        if (briefVisitThresholds is not null)
        {
            config["briefVisitThresholds"] = briefVisitThresholds;
        }

        var configPath = Path.Combine(directory, "config", "telltale.json");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        System.IO.Directory.CreateDirectory(Path.Combine(directory, "data"));
        await File.WriteAllTextAsync(configPath, config.ToJsonString(Indented) + "\n");

        var services = new AppServices(
            jira.Handler,
            new SkippingClock(Local(2026, 2, 3, 12)),
            new InMemorySecretStore(new Dictionary<SecretName, string>
            {
                [SecretName.JiraEmail] = PipelineHarness.JiraEmail,
                [SecretName.JiraApiToken] = PipelineHarness.JiraApiToken,
                [SecretName.HashingSalt] = PipelineHarness.HashingSalt,
            }),
            output,
            error,
            configPath,
            Path.Combine(directory, "data", "telltale.db"),
            Path.Combine(directory, "reports"));
        return await CliApp.RunAsync(["backfill"], services);
    }
}
