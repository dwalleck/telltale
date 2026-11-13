using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A report and the console state the minimum group size's numbers in force (owner decision D67): a line under
/// the report's heading when either number is below its default, the method note, the message that names a
/// baseline sprint, and the help of <c>query</c>. With the default numbers every word is what it was before
/// the numbers became a setting. Sprint 41 runs from 5 to 19 January 2026 and has a team of five here.
/// </summary>
public class NumbersInForceTests
{
    private const string Heading = "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n";

    // The method note's sentences that hold no number, as they are under the default numbers.
    private const string TheTeamOfASprintIs = " The team of a sprint is the distinct assignees of every issue that was a member of the sprint or was delivered in it.";

    private const string AndThenTheCycleTimeTable =
        " and the cycle time table then reads \"suppressed\" as well, with no rows, because the number of its rows and their counts (the delivered issues that have a cycle time) would each give a lower bound of the withheld count."
        + " The time in status table of such a sprint keeps its counts: they count every issue that spent time in a status, delivered or not, so the count of a late status can be close to the withheld delivered count.";

    private const string WhoCountsAsAnAssignee =
        "- An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. An unassigned issue counts toward nobody.\n";

    // The two things the line under the heading says follow from a number below its default.
    private const string ASmallTeam = "A sprint whose team is smaller than the default allows is not withheld as a whole";

    private const string FewPeople = "figure may rest on the work of fewer people than the default allows";

    private const string TheBaselineColumnsPool =
        "- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints 41), each a completed sprint.";

    private const string ABaselineFigureReadsSuppressedOrNone =
        " A baseline figure reads \"suppressed\" when any baseline sprint has that row suppressed in its own report, so that a pooled figure cannot give back a withheld one."
        + " It reads \"none\" when no issue of the baseline sprints is in the row.";

    /// <summary>The whole report of <see cref="AddSprint41WithATeamOfFive"/> with sprint 41 as the baseline, as it was written before the numbers became a setting (the method note is the text of 1ad30cb).</summary>
    private static readonly string ReportUnderTheDefaultNumbers =
        """
        # Fake Sprint 41 (2026-01-05 to 2026-01-19)

        Delivered issues: 5

        ## Cycle time by point bucket

        | Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) | Baseline median (business days) | Baseline P85 (business days) |
        | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
        | 3 | 5 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 | 2.0 |

        ## Time in status

        | Status | Issues | Median (business days) | Total (business days) | Baseline median (business days) |
        | --- | ---: | ---: | ---: | ---: |
        | Fake New | 5 | 1.0 | 5.0 | 1.0 |
        | Fake Development | 5 | 2.0 | 10.0 | 2.0 |

        ## Method

        - Durations are in business days: days that are neither a weekend nor a company holiday, each counted in full (24 hours) in the team's time zone (America/Chicago). Calendar days are shown alongside: elapsed time divided by 24 hours.
        - Cycle time runs from an issue's first entry into a work status to its last entry into a done status, for delivered issues only. It belongs to the sprint in which the issue was delivered.
        - The median is the middle value, or the mean of the two middle values. The P85 is the smallest value with at least 85% of the values at or below it.
        - Time in status includes delivered and unfinished issues in statuses before done, clipped to the sprint window and each issue's membership intervals. An issue counts toward a status only if it spent more than zero business days there. Total business days sum that time across all contributing issues, including repeated visits; this is accumulated issue time, not effort or the sprint's elapsed duration. Totals and medians use the same privacy rules.
        - A figure is shown only where it cannot show one person's work. The team of a sprint is the distinct assignees of every issue that was a member of the sprint or was delivered in it. When the team is smaller than five, the delivered count and both tables read "suppressed", with no rows. With a team of five or more, a row with fewer than three distinct assignees behind it keeps its issue count and reads "suppressed" for its figures, and the delivered count reads "suppressed" when fewer than three distinct assignees are among the delivered issues, and the cycle time table then reads "suppressed" as well, with no rows, because the number of its rows and their counts (the delivered issues that have a cycle time) would each give a lower bound of the withheld count. The time in status table of such a sprint keeps its counts: they count every issue that spent time in a status, delivered or not, so the count of a late status can be close to the withheld delivered count. The footer's two counts read "suppressed" when the issues in the whole store have fewer than five distinct assignees, or fewer than three are among the issues counted.
        - An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. An unassigned issue counts toward nobody.
        - The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints 41), each a completed sprint. A baseline figure reads "suppressed" when any baseline sprint has that row suppressed in its own report, so that a pooled figure cannot give back a withheld one. It reads "none" when no issue of the baseline sprints is in the row. Every baseline figure reads "suppressed" when a baseline sprint has its delivered count withheld: its team is smaller than five, or fewer than three distinct assignees are among its delivered issues.
        - An interval that is still open (an issue still in a status, a sprint not yet completed) ends at the as-of time below.
        - Work that has since been removed from Jira stays in the figures until the next backfill.

        ---

        - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
        - Issues with changelog anomalies, in the whole store: suppressed
        - Issues delivered between sprints, in the whole store: suppressed

        """.ReplaceLineEndings("\n");

    [Test]
    public async Task With_both_numbers_zero_the_line_under_the_heading_says_suppression_is_off_and_what_a_figure_may_rest_on()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithATeamOfFive(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 0, assigneesBehindAFigure: 0);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith(
            Heading
            + "Small-group suppression is off: a figure in this report may rest on the work of one or two people.\n\n"
            + "Delivered issues: 5\n");
    }

    [Test]
    [Arguments(4, 2, "a minimum team of four (below the default of five) and a minimum of two assignees behind a figure (below the default of three). " + ASmallTeam + ", and a " + FewPeople)]
    [Arguments(5, 1, "a minimum team of five (the default) and a minimum of one assignee behind a figure (below the default of three). A " + FewPeople)]
    [Arguments(5, 0, "a minimum team of five (the default) and no minimum of assignees behind a figure (below the default of three). A " + FewPeople)]
    [Arguments(13, 2, "a minimum team of 13 (above the default of five) and a minimum of two assignees behind a figure (below the default of three). A " + FewPeople)]
    [Arguments(0, 3, "no minimum team (below the default of five) and a minimum of three assignees behind a figure (the default). " + ASmallTeam)]
    [Arguments(0, 6, "no minimum team (below the default of five) and a minimum of six assignees behind a figure (above the default of three). " + ASmallTeam)]
    [Arguments(4, 4, "a minimum team of four (below the default of five) and a minimum of four assignees behind a figure (above the default of three). " + ASmallTeam)]
    public async Task With_either_number_below_its_default_and_not_both_zero_the_line_under_the_heading_gives_the_two_numbers_and_what_follows_from_each_one_that_is_below(int team, int assigneesBehindAFigure, string line)
    {
        // Only a number of assignees behind a figure below three lets a figure rest on fewer people than the
        // default allows; a team number below five lets a smaller sprint through, and no more than that.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithATeamOfFive(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith(
            Heading
            + $"Minimum group size in this report: {line}.\n\n"
            + "Delivered issues: ");
    }

    [Test]
    [Arguments(5, 3)]
    [Arguments(6, 3)]
    [Arguments(5, 4)]
    [Arguments(8, 5)]
    public async Task With_both_numbers_at_or_above_the_defaults_the_delivered_count_follows_the_heading_and_no_line_stands_between(int team, int assigneesBehindAFigure)
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithATeamOfFive(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith(Heading + "Delivered issues: ");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Without_the_setting_and_with_it_written_as_five_and_three_the_report_is_word_for_word_what_it_was_before_the_setting(bool written)
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithATeamOfFive(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        if (written)
        {
            pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 5, assigneesBehindAFigure: 3);
        }

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(41)).IsEqualTo(ReportUnderTheDefaultNumbers);
    }

    [Test]
    public async Task With_both_numbers_zero_the_method_note_says_no_figure_was_withheld_for_group_size_and_that_person_fields_are_still_hashed()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithATeamOfFive(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 0, assigneesBehindAFigure: 0);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        // In place of the two paragraphs on the minimum group size and on who counts as an assignee (D42, D55),
        // and of the baseline paragraph's sentences on a withheld baseline figure (D44, D50, D56).
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains(
            "Totals and medians use the same privacy rules.\n"
            + "- No figure in this report was withheld for group size: small-group suppression is off (minimumGroupSize in the configuration file). Person fields are still hashed before they are stored.\n"
            + "- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints 41), each a completed sprint. "
            + "A baseline figure reads \"none\" when no issue of the baseline sprints is in the row.\n"
            + "- An interval that is still open");
        await Assert.That(report).DoesNotContain("suppressed");
    }

    [Test]
    [Arguments(4, 2, "four", "two", "")]
    [Arguments(8, 4, "eight", "four", "- Baseline sprint 41 has fewer than eight assignees, or fewer than four among its delivered issues, so every baseline figure in this report reads \"suppressed\".\n")]
    [Arguments(13, 12, "13", "twelve", "- Baseline sprint 41 has fewer than 13 assignees, or fewer than twelve among its delivered issues, so every baseline figure in this report reads \"suppressed\".\n")]
    public async Task With_other_numbers_the_method_note_states_them_wherever_it_states_five_and_three_under_the_defaults(int team, int assigneesBehindAFigure, string teamInWords, string figureInWords, string baselineSprintNamed)
    {
        // Sprint 41 has a team of five: under a team number above five it is itself withheld, and the note names it as a baseline sprint.
        var report = await ReportOfSprint41AsItsOwnBaselineAsync(team, assigneesBehindAFigure);

        await Assert.That(report).Contains(
            "Totals and medians use the same privacy rules.\n"
            + "- A figure is shown only where it cannot show one person's work." + TheTeamOfASprintIs
            + $" When the team is smaller than {teamInWords}, the delivered count and both tables read \"suppressed\", with no rows."
            + $" With a team of {teamInWords} or more, a row with fewer than {figureInWords} distinct assignees behind it keeps its issue count and reads \"suppressed\" for its figures,"
            + $" and the delivered count reads \"suppressed\" when fewer than {figureInWords} distinct assignees are among the delivered issues," + AndThenTheCycleTimeTable
            + $" The footer's two counts read \"suppressed\" when the issues in the whole store have fewer than {teamInWords} distinct assignees, or fewer than {figureInWords} are among the issues counted.\n"
            + WhoCountsAsAnAssignee
            + TheBaselineColumnsPool + ABaselineFigureReadsSuppressedOrNone
            + $" Every baseline figure reads \"suppressed\" when a baseline sprint has its delivered count withheld: its team is smaller than {teamInWords}, or fewer than {figureInWords} distinct assignees are among its delivered issues.\n"
            + baselineSprintNamed
            + "- An interval that is still open");
    }

    [Test]
    public async Task With_only_the_team_number_above_zero_the_method_note_describes_the_team_check_and_not_the_other()
    {
        var report = await ReportOfSprint41AsItsOwnBaselineAsync(team: 4, assigneesBehindAFigure: 0);

        // No assignee need be behind a figure, so a figure can be one person's work: the opening sentence says the
        // rule and not that. And no row is suppressed in a sprint's own report, so the baseline paragraph does not speak of one.
        await Assert.That(report).Contains(
            "Totals and medians use the same privacy rules.\n"
            + "- A figure is shown only for a group of at least the minimum group size." + TheTeamOfASprintIs
            + " When the team is smaller than four, the delivered count and both tables read \"suppressed\", with no rows."
            + " With a team of four or more, every figure of the sprint itself is shown, whatever the number of assignees behind it."
            + " The footer's two counts read \"suppressed\" when the issues in the whole store have fewer than four distinct assignees.\n"
            + WhoCountsAsAnAssignee
            + TheBaselineColumnsPool + " A baseline figure reads \"none\" when no issue of the baseline sprints is in the row."
            + " Every baseline figure reads \"suppressed\" when a baseline sprint has its delivered count withheld: its team is smaller than four.\n"
            + "- An interval that is still open");
    }

    [Test]
    public async Task With_only_the_number_of_assignees_behind_a_figure_above_zero_the_method_note_describes_that_check_and_not_the_team_check()
    {
        var report = await ReportOfSprint41AsItsOwnBaselineAsync(team: 0, assigneesBehindAFigure: 2);

        await Assert.That(report).Contains(
            "Totals and medians use the same privacy rules.\n"
            + "- A figure is shown only where it cannot show one person's work."
            + " A row with fewer than two distinct assignees behind it keeps its issue count and reads \"suppressed\" for its figures,"
            + " and the delivered count reads \"suppressed\" when fewer than two distinct assignees are among the delivered issues," + AndThenTheCycleTimeTable
            + " The footer's two counts read \"suppressed\" when fewer than two distinct assignees are among the issues counted."
            + " No figure is withheld for the size of a sprint's team.\n"
            + WhoCountsAsAnAssignee
            + TheBaselineColumnsPool + ABaselineFigureReadsSuppressedOrNone
            + " Every baseline figure reads \"suppressed\" when a baseline sprint has its delivered count withheld: fewer than two distinct assignees are among its delivered issues.\n"
            + "- An interval that is still open");
    }

    [Test]
    public async Task With_a_number_of_one_the_method_note_speaks_of_one_assignee_and_does_not_say_a_figure_cannot_show_one_persons_work()
    {
        var report = await ReportOfSprint41AsItsOwnBaselineAsync(team: 1, assigneesBehindAFigure: 1);

        await Assert.That(report).Contains(
            "Totals and medians use the same privacy rules.\n"
            + "- A figure is shown only for a group of at least the minimum group size." + TheTeamOfASprintIs
            + " When the team is smaller than one, the delivered count and both tables read \"suppressed\", with no rows."
            + " With a team of one or more, a row with fewer than one distinct assignee behind it keeps its issue count and reads \"suppressed\" for its figures,"
            + " and the delivered count reads \"suppressed\" when fewer than one distinct assignee is among the delivered issues," + AndThenTheCycleTimeTable
            + " The footer's two counts read \"suppressed\" when the issues in the whole store have fewer than one distinct assignee, or fewer than one is among the issues counted.\n"
            + WhoCountsAsAnAssignee
            + TheBaselineColumnsPool + ABaselineFigureReadsSuppressedOrNone
            + " Every baseline figure reads \"suppressed\" when a baseline sprint has its delivered count withheld: its team is smaller than one, or fewer than one distinct assignee is among its delivered issues.\n"
            + "- An interval that is still open");
    }

    [Test]
    [Arguments(8, 4, "41", "Baseline sprint 41 has fewer than eight assignees, or fewer than four among its delivered issues")]
    [Arguments(6, 0, "41", "Baseline sprint 41 has fewer than six assignees")]
    [Arguments(0, 6, "41", "Baseline sprint 41 has fewer than six assignees among its delivered issues")]
    [Arguments(6, 0, "41,42", "Baseline sprints 41 and 42 each have fewer than six assignees")]
    [Arguments(0, 6, "41,42", "Baseline sprints 41 and 42 each have fewer than six assignees among their delivered issues")]
    public async Task The_console_and_the_report_name_a_baseline_sprint_that_withholds_the_baseline_with_the_configured_numbers_and_only_the_checks_that_apply(int team, int assigneesBehindAFigure, string baseline, string named)
    {
        // Each sprint has five people, and the five deliver: short of every number here that is not zero.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithATeamOfFive(pipeline.Jira);
        pipeline.Jira.AddSprint42();
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-2{number}", Local(2026, 1, 20, 9)).AssignedTo(person).Estimated(3).InSprints(42)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 23, 9), Workflow.Done);
        }

        pipeline.Config["baselineSprintIds"] = new JsonArray([.. baseline.Split(',').Select(id => (JsonNode)id)]);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team, assigneesBehindAFigure);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"{named}, so every baseline figure is withheld (baselineSprintIds in the configuration file).\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            $".\n- {named}, so every baseline figure in this report reads \"suppressed\".\n- An interval that is still open");
    }

    [Test]
    public async Task The_help_of_query_says_the_metric_views_withhold_small_group_figures_according_to_the_setting()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunAsync("query", "--help");

        // The help is wrapped to the console's width, so line ends are read as spaces.
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(string.Join(' ', pipeline.Out.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).Contains(
            "Run a SQL query over the metric views and print its rows as a markdown table. Jira is not called. "
            + "The query reads the metric views and nothing else in the store, and the metric views withhold small-group figures according to the minimumGroupSize setting of the configuration file. "
            + "Opening the database file in another program bypasses the suppression rule: that program reads every table.");
    }

    /// <summary>The report of sprint 41 (<see cref="AddSprint41WithATeamOfFive"/>), which is also the one baseline sprint, written under the two numbers.</summary>
    private static async Task<string> ReportOfSprint41AsItsOwnBaselineAsync(int team, int assigneesBehindAFigure)
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithATeamOfFive(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        return await pipeline.ReadReportAsync(41);
    }

    private static JsonObject MinimumGroupSize(int team, int assigneesBehindAFigure) =>
        new() { ["team"] = team, ["assigneesBehindAFigure"] = assigneesBehindAFigure };

    /// <summary>
    /// Sprint 41 with five people, one delivered 3-point issue each: created as the sprint starts (Monday
    /// 5 January 09:00), in development from Tuesday 09:00, done Thursday 09:00. So one business day in New and
    /// a cycle of two days, each.
    /// </summary>
    private static void AddSprint41WithATeamOfFive(FakeJira jira)
    {
        jira.AddSprint41();
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).Estimated(3).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        }
    }
}
