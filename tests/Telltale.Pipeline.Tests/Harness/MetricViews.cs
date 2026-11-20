namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// The metric views: what <c>query</c> may read. The one list for every test that speaks of all of them, so a
/// view added later is added here and nowhere else in the tests.
/// </summary>
public static class MetricViews
{
    /// <summary>Every metric view's name, in the order <c>query</c> names them when it refuses: by name.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "baseline_cycle_time_per_bucket",
        "baseline_sprint",
        "baseline_time_in_status",
        "brief_visit_treatment",
        "brief_visits_per_sprint",
        "cycle_starts_with_brief_visit_per_sprint",
        "cycle_time_per_sprint_and_bucket",
        "data_quality",
        "delivered_per_sprint",
        "minimum_group_size",
        "pr_timing_per_sprint",
        "pr_without_review_per_sprint",
        "sprint_team",
        "time_in_status_per_sprint",
    ];
}
