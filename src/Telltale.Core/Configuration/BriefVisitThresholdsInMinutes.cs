namespace Telltale.Core.Configuration;

/// <summary>
/// <c>briefVisitThresholds</c> as the configuration file has it: each listed status ID with its threshold in
/// minutes, not yet checked (<see cref="TelltaleSettings.CheckedBriefVisitThresholds"/> checks it). The setting
/// has a type of its own, as <see cref="MinimumGroupSize"/> has, so that the file's reader can be given one
/// reader for it by type, and no other setting that happens to be a dictionary is read by that one.
/// </summary>
public sealed class BriefVisitThresholdsInMinutes(IReadOnlyDictionary<string, double> byStatusId)
{
    /// <summary>The setting left out, or empty: no status is checked.</summary>
    public static BriefVisitThresholdsInMinutes None { get; } = new(new Dictionary<string, double>());

    /// <summary>The threshold in minutes of each listed status, by status ID.</summary>
    public IReadOnlyDictionary<string, double> ByStatusId { get; } = byStatusId;

    /// <summary>A configuration built in code writes the setting as the dictionary it is.</summary>
    public static implicit operator BriefVisitThresholdsInMinutes(Dictionary<string, double> byStatusId) => new(byStatusId);
}
