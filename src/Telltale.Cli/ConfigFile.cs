using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Telltale.Core;
using Telltale.Core.Configuration;

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
    };

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
    internal static void RequireMinimumGroupSize(JsonNode? written, string path)
    {
        try
        {
            // A null in the file reaches the converter as the JSON null it is, and is refused there.
            var numbers = JsonSerializer.Deserialize<MinimumGroupSize>(written?.ToJsonString() ?? "null", Options);
            TelltaleSettings.CheckedMinimumGroupSize(numbers!);
        }
        catch (Exception exception) when (exception is JsonException or SettingRefusedException)
        {
            throw new UserFacingException($"The configuration file {path} is not valid: {exception.Message}", exception);
        }
    }

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
        catch (Exception exception) when (exception is JsonException or SettingRefusedException)
        {
            throw new UserFacingException($"The configuration file {path} is not valid: {exception.Message}", exception);
        }
    }
}
