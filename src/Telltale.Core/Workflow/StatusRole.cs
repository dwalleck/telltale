using System.Diagnostics.CodeAnalysis;

namespace Telltale.Core.Workflow;

/// <summary>
/// What a workflow status means for the metrics. Each status ID gets exactly one role in the
/// configuration file; names and Jira's own status categories are never consulted.
/// </summary>
public closed record StatusRole
{
    /// <summary>
    /// Reads a role from its configuration label. Throws <see cref="FormatException"/> for anything else. The
    /// message does not repeat the label: it comes from the configuration file, and what is not a role could
    /// be a secret pasted into the wrong place (owner decision D43).
    /// </summary>
    public static StatusRole Parse(string label) =>
        TryParse(label, out var role) ? role : throw new FormatException("The label is not a status role. Use one of: not started, work, blocked, done.");

    /// <summary>Reads a role from its configuration label. False for anything that is not one of the four labels.</summary>
    public static bool TryParse(string? label, [NotNullWhen(true)] out StatusRole? role)
    {
        role = label switch
        {
            "not started" => new NotStartedRole(),
            "work" => new WorkRole(),
            "blocked" => new BlockedRole(),
            "done" => new DoneRole(),
            _ => null,
        };
        return role is not null;
    }
}

/// <summary>Waiting to be picked up: development has not started.</summary>
public sealed record NotStartedRole : StatusRole;

/// <summary>Being worked on. The first entry into a work status starts the cycle.</summary>
public sealed record WorkRole : StatusRole;

/// <summary>Stuck. Never starts a cycle, but counts once one has started.</summary>
public sealed record BlockedRole : StatusRole;

/// <summary>Finished from the team's point of view (Done, Shipped).</summary>
public sealed record DoneRole : StatusRole;

/// <summary>The configured role of every status, by status ID, and the order the configuration lists the statuses in.</summary>
public sealed class StatusRoles
{
    private readonly Dictionary<string, StatusRole> _byStatusId;

    /// <param name="inBoardOrder">Each status and its role, in board order. A status listed twice is an <see cref="ArgumentException"/>.</param>
    public StatusRoles(IEnumerable<KeyValuePair<string, StatusRole>> inBoardOrder)
    {
        var pairs = inBoardOrder.ToList();
        _byStatusId = pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        StatusIds = [.. pairs.Select(pair => pair.Key)];
    }

    /// <summary>
    /// Every status that has a role, in board order: the order they were given in, which is the order the
    /// configuration lists them. It is how reports order statuses.
    /// </summary>
    public IReadOnlyList<string> StatusIds { get; }

    /// <summary>
    /// Builds the map from configuration labels, e.g. <c>{ "10001": "done" }</c>. The order of
    /// <paramref name="labelsInBoardOrder"/> is kept as board order, so pass a collection whose order is defined.
    /// A label that is not a role is a <see cref="FormatException"/> that names the status, which is how the
    /// owner finds it, and never the label (owner decision D43).
    /// </summary>
    public static StatusRoles Parse(IEnumerable<KeyValuePair<string, string>> labelsInBoardOrder) =>
        new(labelsInBoardOrder.Select(pair => KeyValuePair.Create(pair.Key, StatusRole.TryParse(pair.Value, out var role)
            ? role
            : throw new FormatException($"the role of status {pair.Key} is not one of: not started, work, blocked, done."))));

    /// <summary>True when at least one status has the done role. Without one, nothing could ever be delivered.</summary>
    public bool HasDone => _byStatusId.Values.Any(role => role is DoneRole);

    /// <summary>The status's role, or null when the configuration does not mention the status.</summary>
    public StatusRole? RoleOf(string statusId) => _byStatusId.GetValueOrDefault(statusId);

    /// <summary>True when the status has the work role.</summary>
    public bool IsWork(string statusId) => RoleOf(statusId) is WorkRole;

    /// <summary>True when the status has the done role. Derive stops the run before a status with no role can matter.</summary>
    public bool IsDone(string statusId) => RoleOf(statusId) is DoneRole;
}
