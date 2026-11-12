namespace Telltale.Core.Derive;

/// <summary>What is refused of the baseline sprint list beyond what <see cref="Derivation"/> can tell from the raw data.</summary>
public static class BaselineSprintRules
{
    /// <summary>
    /// The refusal of a list that holds sprints with fewer than five distinct assignees (owner decision D47).
    /// Such a sprint's own report withholds everything (GLOSSARY: Minimum group size), and a baseline pooled
    /// from it would give some of that back, so it cannot be a baseline sprint. The message names the setting
    /// and the sprints and says nothing else about them. The IDs are digits, so it can quote them.
    /// </summary>
    public static UserFacingException ShortOfFiveAssignees(IReadOnlyList<string> sprintIds) =>
        new($"baselineSprintIds in the configuration file lists {string.Join(", ", sprintIds)}, which {(sprintIds.Count == 1 ? "has" : "each have")} fewer than five assignees. "
            + $"A sprint with fewer than five assignees cannot be a baseline sprint: take {(sprintIds.Count == 1 ? "it" : "them")} out of the list and run again.");
}
