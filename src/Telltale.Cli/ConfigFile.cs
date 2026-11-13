using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Telltale.Core;
using Telltale.Core.Configuration;
using Telltale.Core.Workflow;

namespace Telltale.Cli;

/// <summary>Reads the configuration file: JSON with camelCase names. Comments and trailing commas are allowed.</summary>
public static class ConfigFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // A setting the tool does not know is refused, not ignored. That catches typos, and it means
        // a secret pasted into the file stops the run instead of sitting there unnoticed.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

        // A setting written as null is refused where the setting is required, with its name in the message.
        RespectNullableAnnotations = true,

        // A setting written twice is refused: the reader of the file could not tell which value is in force.
        AllowDuplicateProperties = false,

        // minimumGroupSize is read by hand, so that null, a quoted number or a missing number is refused by name.
        Converters = { new MinimumGroupSizeConverter() },

        // briefVisitThresholds is too, so that what is refused is named without the status ID it stands under.
        // Its type is a plain dictionary, so its converter is bound to the setting and not to the type.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ReadBriefVisitThresholdsByHand } },
    };

    /// <summary>For reading <c>briefVisitThresholds</c> on its own, outside a configuration: nothing else is read with these.</summary>
    private static readonly JsonSerializerOptions BriefVisitThresholdsAlone = new() { Converters = { new BriefVisitThresholdsConverter() } };

    private static void ReadBriefVisitThresholdsByHand(JsonTypeInfo type)
    {
        if (type.Type == typeof(TelltaleConfig))
        {
            // Found by the property, not by the name in the file, so a renamed setting cannot lose its reader unseen.
            type.Properties.Single(property => property.AttributeProvider is PropertyInfo { Name: nameof(TelltaleConfig.BriefVisitThresholds) }).CustomConverter = new BriefVisitThresholdsConverter();
        }
    }

    /// <summary>
    /// The minimum group size as the file holds it, with the names the file is read by: what <c>discover</c>
    /// writes stays in step with what the run reads. It takes this one setting and no other, so nothing that
    /// could be null reaches the converter's <c>Write</c>.
    /// </summary>
    internal static JsonNode Written(MinimumGroupSize numbers) => JsonSerializer.SerializeToNode(numbers, Options)!;

    /// <summary>
    /// Reads and checks the <c>minimumGroupSize</c> that the file at <paramref name="path"/> holds as
    /// <paramref name="written"/> (null for a JSON <c>null</c>), on its own: by the converter and the rule
    /// <see cref="ReadAsync"/> applies to it, and with the same message. <c>discover</c> asks this of an existing
    /// file before it calls Jira (owner decision D70).
    /// </summary>
    internal static void RequireMinimumGroupSize(JsonNode? written, string path) =>
        RequireSetting<MinimumGroupSize>(written, Options, numbers => TelltaleSettings.CheckedMinimumGroupSize(numbers), path);

    /// <summary>
    /// Reads and checks the <c>briefVisitThresholds</c> that the file at <paramref name="path"/> holds as
    /// <paramref name="written"/> (null for a JSON <c>null</c>), on its own: by the converter and the rule
    /// <see cref="ReadAsync"/> applies to it, and with the same message for whatever they refuse.
    /// <paramref name="roles"/> are the file's status roles, or null when it has none yet: then only the
    /// thresholds are checked, and the statuses wait for the check of the text about to be written.
    /// <c>discover</c> asks this of an existing file before it calls Jira (owner decision D70). One thing
    /// never reaches it: a status listed twice stops <c>discover</c> earlier, where it parses the file, with
    /// that reader's own message and not this one's.
    /// </summary>
    internal static void RequireBriefVisitThresholds(JsonNode? written, StatusRoles? roles, string path) =>
        RequireSetting<IReadOnlyDictionary<string, double>>(written, BriefVisitThresholdsAlone, thresholds => TelltaleSettings.CheckedBriefVisitThresholds(thresholds, roles), path);

    /// <summary>One setting, read from what the file holds for it and then checked by its rule. A null in the file reaches the setting's converter as the JSON null it is, and is refused there.</summary>
    private static void RequireSetting<T>(JsonNode? written, JsonSerializerOptions options, Action<T> check, string path)
    {
        try
        {
            check(JsonSerializer.Deserialize<T>(written?.ToJsonString() ?? "null", options)!);
        }
        catch (Exception exception) when (IsRefusal(exception))
        {
            throw NotValid(path, exception);
        }
    }

    /// <summary>What refuses a setting: the reader, or the setting's rule.</summary>
    private static bool IsRefusal(Exception exception) => exception is JsonException or SettingRefusedException;

    private static UserFacingException NotValid(string path, Exception refusal) =>
        new($"The configuration file {path} is not valid: {refusal.Message}", refusal);

    /// <summary>Reads the file and checks every setting. Throws <see cref="UserFacingException"/> naming the file and the setting that is refused.</summary>
    public static async Task<TelltaleSettings> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new UserFacingException($"The configuration file {path} does not exist.");
        }

        await using var stream = File.OpenRead(path);
        return await ReadAsync(stream, path, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads and checks the content of the file at <paramref name="path"/>, or content about to be written
    /// there: <c>discover</c> passes what it is about to write, so it never writes a file the run would refuse.
    /// The rules are <see cref="TelltaleSettings.From"/>'s, for both.
    /// </summary>
    public static async Task<TelltaleSettings> ReadAsync(Stream stream, string path, bool requireDone = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await JsonSerializer.DeserializeAsync<TelltaleConfig>(stream, Options, cancellationToken)
                ?? throw new UserFacingException($"The configuration file {path} is empty.");
            return TelltaleSettings.From(config, requireDone);
        }
        catch (Exception exception) when (IsRefusal(exception))
        {
            throw NotValid(path, exception);
        }
    }
}
