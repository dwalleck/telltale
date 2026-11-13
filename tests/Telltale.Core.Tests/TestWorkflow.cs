using Telltale.Core.Workflow;

namespace Telltale.Core.Tests;

/// <summary>
/// The workflow the Core tests are set in: its statuses by ID and the role each has. One home, so a test
/// names a status and never repeats the role map.
/// </summary>
internal static class TestWorkflow
{
    public const string New = "1";
    public const string Development = "4";
    public const string Review = "5";
    public const string QE = "6";
    public const string Blocked = "7";
    public const string Done = "8";
    public const string Shipped = "9";

    public static readonly StatusRoles Roles = StatusRoles.Parse(new Dictionary<string, string>
    {
        [New] = "not started",
        [Development] = "work",
        [Review] = "work",
        [QE] = "work",
        [Blocked] = "blocked",
        [Done] = "done",
        [Shipped] = "done",
    });
}
