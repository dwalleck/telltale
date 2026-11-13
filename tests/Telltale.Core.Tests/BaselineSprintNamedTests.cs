using Telltale.Core.Configuration;
using Telltale.Core.Derive;

namespace Telltale.Core.Tests;

/// <summary>
/// The console's line that names the baseline sprints withholding the baseline (owner decisions D50, D56), for
/// the numbers no synthetic sprint of the pipeline tests is short of: a number of one, and both numbers zero.
/// </summary>
public class BaselineSprintNamedTests
{
    [Test]
    [Arguments(1, 1, "Baseline sprint 41 has fewer than one assignee, or fewer than one among its delivered issues, so every baseline figure is withheld (baselineSprintIds in the configuration file).")]
    [Arguments(1, 0, "Baseline sprint 41 has fewer than one assignee, so every baseline figure is withheld (baselineSprintIds in the configuration file).")]
    [Arguments(0, 1, "Baseline sprint 41 has fewer than one assignee among its delivered issues, so every baseline figure is withheld (baselineSprintIds in the configuration file).")]
    [Arguments(5, 3, "Baseline sprint 41 has fewer than five assignees, or fewer than three among its delivered issues, so every baseline figure is withheld (baselineSprintIds in the configuration file).")]
    public async Task The_line_gives_the_numbers_in_force_and_writes_a_number_of_one_as_one_assignee(int team, int assigneesBehindAFigure, string line)
    {
        var numbers = new MinimumGroupSize { Team = team, AssigneesBehindAFigure = assigneesBehindAFigure };

        await Assert.That(BaselineSprintRules.WithheldOnTheConsole(["41"], numbers)).IsEqualTo(line);
    }

    [Test]
    public async Task With_both_numbers_zero_no_number_is_given_as_the_reason()
    {
        // The views withhold no baseline sprint then. A line written all the same must not say "fewer than zero".
        var off = new MinimumGroupSize { Team = 0, AssigneesBehindAFigure = 0 };

        await Assert.That(BaselineSprintRules.WithheldOnTheConsole(["41", "42"], off)).IsEqualTo(
            "Baseline sprints 41 and 42 each have their delivered count withheld, so every baseline figure is withheld (baselineSprintIds in the configuration file).");
    }
}
