namespace Telltale.Core.Workflow;

/// <summary>
/// What a workflow status means for the metrics. Each status ID gets exactly one role in the
/// configuration file; names and Jira's own status categories are never consulted.
/// </summary>
public closed record StatusRole
{
    /// <summary>The role's name as written in the configuration file.</summary>
    public string Label => this switch
    {
        NotStartedRole => "not started",
        WorkRole => "work",
        BlockedRole => "blocked",
        DoneRole => "done",
    };

    /// <summary>Reads a role from its configuration label. Throws <see cref="FormatException"/> for anything else.</summary>
    public static StatusRole Parse(string label) => label switch
    {
        "not started" => new NotStartedRole(),
        "work" => new WorkRole(),
        "blocked" => new BlockedRole(),
        "done" => new DoneRole(),
        _ => throw new FormatException($"'{label}' is not a status role. Use one of: not started, work, blocked, done."),
    };
}

/// <summary>Waiting to be picked up: development has not started.</summary>
public sealed record NotStartedRole : StatusRole;

/// <summary>Being worked on. The first entry into a work status starts the cycle.</summary>
public sealed record WorkRole : StatusRole;

/// <summary>Stuck. Never starts a cycle, but counts once one has started.</summary>
public sealed record BlockedRole : StatusRole;

/// <summary>Finished from the team's point of view (Done, Shipped).</summary>
public sealed record DoneRole : StatusRole;

/// <summary>The configured role of every status, by status ID.</summary>
public sealed class StatusRoles
{
    private readonly Dictionary<string, StatusRole> _byStatusId;

    public StatusRoles(IReadOnlyDictionary<string, StatusRole> byStatusId)
    {
        _byStatusId = new Dictionary<string, StatusRole>(byStatusId, StringComparer.Ordinal);
    }

    /// <summary>Builds the map from configuration labels, e.g. <c>{ "10001": "done" }</c>.</summary>
    public static StatusRoles Parse(IReadOnlyDictionary<string, string> labelsByStatusId) =>
        new(labelsByStatusId.ToDictionary(pair => pair.Key, pair => StatusRole.Parse(pair.Value), StringComparer.Ordinal));

    /// <summary>The status's role, or null when the configuration does not mention the status.</summary>
    public StatusRole? RoleOf(string statusId) => _byStatusId.GetValueOrDefault(statusId);

    /// <summary>True when the status has the done role. A status the configuration does not mention is not done.</summary>
    public bool IsDone(string statusId) => RoleOf(statusId) is DoneRole;
}
