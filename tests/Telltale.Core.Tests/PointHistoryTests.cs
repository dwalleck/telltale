using Telltale.Core.Workflow;
using static Telltale.Core.Tests.TestTime;

namespace Telltale.Core.Tests;

/// <summary>The estimate in effect at a moment, rebuilt from the estimate's history. The moment is Wednesday 4 November 2026, 09:00.</summary>
public class PointHistoryTests
{
    private static readonly DateTimeOffset CycleStart = Local(2026, 11, 4, 9);

    [Test]
    public async Task A_re_estimate_before_the_moment_is_the_estimate_in_effect()
    {
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 5,
            [new PointChange("1", Local(2026, 11, 3, 9), From: 3, To: 5)]);

        await Assert.That(estimate).IsEqualTo(5.0);
    }

    [Test]
    public async Task A_re_estimate_after_the_moment_is_ignored()
    {
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 8,
            [new PointChange("1", Local(2026, 11, 5, 9), From: 3, To: 8)]);

        await Assert.That(estimate).IsEqualTo(3.0);
    }

    [Test]
    public async Task With_several_re_estimates_the_last_one_before_the_moment_is_in_effect()
    {
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 13,
        [
            new PointChange("1", Local(2026, 11, 2, 9), From: 1, To: 2),
            new PointChange("2", Local(2026, 11, 3, 9), From: 2, To: 5),
            new PointChange("3", Local(2026, 11, 5, 9), From: 5, To: 8),
            new PointChange("4", Local(2026, 11, 6, 9), From: 8, To: 13),
        ]);

        await Assert.That(estimate).IsEqualTo(5.0);
    }

    [Test]
    public async Task An_issue_first_estimated_after_the_moment_had_no_estimate()
    {
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 5,
            [new PointChange("1", Local(2026, 11, 5, 9), From: null, To: 5)]);

        await Assert.That(estimate).IsNull();
    }

    [Test]
    public async Task An_issue_whose_estimate_was_cleared_before_the_moment_had_no_estimate()
    {
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 5,
        [
            new PointChange("1", Local(2026, 11, 3, 9), From: 3, To: null),
            new PointChange("2", Local(2026, 11, 5, 9), From: null, To: 5),
        ]);

        await Assert.That(estimate).IsNull();
    }

    [Test]
    public async Task With_no_history_the_current_estimate_is_in_effect()
    {
        await Assert.That(PointHistory.EstimateAt(CycleStart, currentEstimate: 3, [])).IsEqualTo(3.0);
    }

    [Test]
    public async Task With_no_history_and_no_current_estimate_there_is_no_estimate()
    {
        await Assert.That(PointHistory.EstimateAt(CycleStart, currentEstimate: null, [])).IsNull();
    }

    [Test]
    public async Task A_re_estimate_at_the_moment_itself_is_in_effect()
    {
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 5,
            [new PointChange("1", CycleStart, From: 3, To: 5)]);

        await Assert.That(estimate).IsEqualTo(5.0);
    }

    [Test]
    public async Task Changes_are_read_in_time_order_whatever_order_they_arrive_in()
    {
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 8,
        [
            new PointChange("3", Local(2026, 11, 5, 9), From: 5, To: 8),
            new PointChange("2", Local(2026, 11, 3, 9), From: 2, To: 5),
            new PointChange("1", Local(2026, 11, 2, 9), From: 1, To: 2),
        ]);

        await Assert.That(estimate).IsEqualTo(5.0);
    }

    [Test]
    public async Task Two_re_estimates_at_one_instant_are_read_in_history_order()
    {
        // History IDs are numbers: 10 comes after 9.
        var estimate = PointHistory.EstimateAt(CycleStart, currentEstimate: 8,
        [
            new PointChange("10", Local(2026, 11, 3, 9), From: 5, To: 8),
            new PointChange("9", Local(2026, 11, 3, 9), From: 3, To: 5),
        ]);

        await Assert.That(estimate).IsEqualTo(8.0);
    }

    [Test]
    public async Task Stored_values_are_read_as_numbers_and_a_missing_value_as_no_estimate()
    {
        var change = PointChange.FromRaw("1", CycleStart, from: null, to: "0.5");

        await Assert.That(change.From).IsNull();
        await Assert.That(change.To).IsEqualTo(0.5);
    }
}
