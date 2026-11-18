using System.Globalization;
using System.Text;
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
        // briefVisitThresholds is too, so that what is refused is named without the status ID it stands under.
        // briefVisitTreatment is too, so that a part or a value the tool does not know is refused without being repeated.
        // Each setting has a type of its own, and its reader is found by that type.
        Converters = { new MinimumGroupSizeConverter(), new BriefVisitThresholdsConverter(), new BriefVisitTreatmentConverter() },

        // The reader the runtime would choose anyway, named so that the settings it maps can be asked for (KnownSettings).
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
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
    internal static void RequireMinimumGroupSize(JsonNode? written, string path) =>
        RequireSetting<MinimumGroupSize, MinimumGroupSize>(TextOf(written), numbers => TelltaleSettings.CheckedMinimumGroupSize(numbers), path);

    /// <summary>
    /// Reads and checks the <c>briefVisitThresholds</c> that the file at <paramref name="path"/> holds as
    /// <paramref name="written"/> (null for a JSON <c>null</c>), on its own: by the converter and the rule
    /// <see cref="ReadAsync"/> applies to it, and with the same message for whatever they refuse.
    /// <paramref name="roles"/> are the file's status roles, or null when it has none yet: then only the
    /// thresholds are checked, and the statuses wait for the check of the text about to be written.
    /// <c>discover</c> asks this of an existing file before it calls Jira (owner decision D70). One thing
    /// never reaches it: a status listed twice stops <c>discover</c> earlier, where it reads the file, and
    /// <c>discover</c> then says that a key is written twice under the setting and not what this says
    /// (owner decision D108). Returns the thresholds, checked, for the check of the setting that is applied
    /// with them (<see cref="RequireBriefVisitTreatment"/>).
    /// </summary>
    internal static IReadOnlyDictionary<string, TimeSpan> RequireBriefVisitThresholds(JsonNode? written, StatusRoles? roles, string path) =>
        RequireSetting<BriefVisitThresholdsInMinutes, IReadOnlyDictionary<string, TimeSpan>>(TextOf(written), thresholds => TelltaleSettings.CheckedBriefVisitThresholds(thresholds, roles), path);

    /// <summary>
    /// Reads and checks the <c>briefVisitTreatment</c> that the file at <paramref name="path"/> holds, by the
    /// converter and the rule <see cref="ReadAsync"/> applies to it, and with the same message for whatever
    /// they refuse. <paramref name="written"/> is the setting's own text, cut from the file as it was typed
    /// and not written out again from what was parsed: the setting's reader then sees what <c>run</c>'s
    /// reader sees, a value that is no text at all included.
    /// <paramref name="thresholds"/> are the file's thresholds, checked, and empty when it lists none: the
    /// setting is refused when it leaves brief visits out and there are none to leave out. <paramref name="roles"/>
    /// are the file's status roles, or null when it has none yet: then whether a listed status has the work
    /// role waits for the check of the text about to be written, as the listed statuses do (owner decision
    /// D73). <c>discover</c> asks this of an existing file before it calls Jira (owner decision D70).
    /// </summary>
    internal static void RequireBriefVisitTreatment(string written, IReadOnlyDictionary<string, TimeSpan> thresholds, StatusRoles? roles, string path) =>
        RequireSetting<BriefVisitTreatmentSetting, BriefVisitTreatmentSetting>(written, treatment => TelltaleSettings.CheckedBriefVisitTreatment(treatment, thresholds, roles), path);

    /// <summary>
    /// Reads the <c>briefVisitTreatment</c> that the file at <paramref name="path"/> holds as the text
    /// <paramref name="written"/>, by the converter <see cref="ReadAsync"/> reads it with, and applies no
    /// rule. <c>discover</c> asks this of a file in which a key is written twice: when that key, or another
    /// fault, is directly under this setting, the setting's reader names it, and repeats nothing written
    /// there (owner decision D105).
    /// </summary>
    internal static void RequireReadableBriefVisitTreatment(string written, string path) =>
        RequireSetting<BriefVisitTreatmentSetting, BriefVisitTreatmentSetting>(written, treatment => treatment, path);

    /// <summary>What the file holds for a setting, as JSON text. A JSON <c>null</c> is a null node.</summary>
    private static string TextOf(JsonNode? written) => written?.ToJsonString() ?? "null";

    /// <summary>One setting, read from the JSON text the file holds for it and then checked by its rule, which gives the setting as checked. A null in the file reaches the setting's converter as the JSON null it is, and is refused there.</summary>
    private static TChecked RequireSetting<T, TChecked>(string written, Func<T, TChecked> check, string path)
    {
        try
        {
            return check(JsonSerializer.Deserialize<T>(written, Options)!);
        }
        catch (Exception exception) when (IsRefusal(exception))
        {
            throw NotValid(path, exception);
        }
    }

    /// <summary>
    /// What a walk over a configuration file found, before any setting is read: whether it is JSON, and what
    /// is wrong with a key at any depth.
    /// </summary>
    /// <param name="NotJsonAt">
    /// The file is not JSON the tool can read, and where the reader stopped: the line and the position in
    /// that line, each counted from one, the position in bytes. Null for a file that is JSON. Nothing else
    /// is known of a file that is not.
    /// </param>
    /// <param name="KeyIsNotText">
    /// A key is spelled with an escape for half of a character pair (<c>"\uD800"</c>). No text can hold it, so
    /// no reader can give its name, and the reader's own failure on it is not one a command reports plainly.
    /// </param>
    /// <param name="KeyIsNotUtf8">
    /// A key holds bytes that are not UTF-8, as a file saved in another encoding does for a letter outside
    /// ASCII. The reader fails on it in the same way, and it is told apart so that what is said is true.
    /// </param>
    /// <param name="KeyIsWrittenTwice">A key is written twice in one object, in whatever spelling.</param>
    /// <param name="SettingOfKeyWrittenTwice">
    /// The setting the first such key is under, when it is under one whose name the tool knows; null at the
    /// top level of the file, in a file whose root is not an object, and under a name the tool does not know.
    /// It is one of the tool's own names, never text taken from the file.
    /// </param>
    internal readonly record struct KeyFaults((long Line, long Position)? NotJsonAt, bool KeyIsNotText, bool KeyIsNotUtf8, bool KeyIsWrittenTwice, string? SettingOfKeyWrittenTwice);

    /// <summary>The names of the settings the reader maps, as the file spells them.</summary>
    private static readonly Lazy<IReadOnlyList<string>> KnownSettings = new(() => [.. Options.GetTypeInfo(typeof(TelltaleConfig)).Properties.Select(property => property.Name)]);

    /// <summary>
    /// Walks every key of <paramref name="utf8"/>, the file's content without a byte order mark, as leniently
    /// as the file is read (comments, trailing commas). It gives back facts and no text of the file, so that
    /// what a command says about a key never repeats it: what was typed as a key could be a secret pasted
    /// into the wrong place (owner decisions D43, D108). Of content that is not JSON it gives where the
    /// reader stopped and nothing else: the reader's own message for such content can quote the file from
    /// the fault to its end, so no command passes it on (owner decision D110).
    /// </summary>
    internal static KeyFaults FindKeyFaults(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith(Encoding.UTF8.Preamble))
        {
            utf8 = utf8[Encoding.UTF8.Preamble.Length..];
        }

        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        // The keys seen so far in each object that is open; a list has none.
        var open = new Stack<HashSet<string>?>();
        var faults = default(KeyFaults);
        string? topLevelKey = null;
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        open.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.StartArray:
                        open.Push(null);
                        break;
                    case JsonTokenType.EndObject or JsonTokenType.EndArray:
                        open.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        string key;
                        try
                        {
                            key = reader.GetString()!;
                        }
                        catch (InvalidOperationException)
                        {
                            // The reader fails alike on half of a character pair and on bytes that are not
                            // UTF-8. Only the second fails when the key's bytes are decoded as they were typed.
                            faults = IsUtf8(reader.ValueSpan) ? faults with { KeyIsNotText = true } : faults with { KeyIsNotUtf8 = true };
                            break;
                        }

                        var atTopLevel = open.Count == 1;
                        if (!open.Peek()!.Add(key) && !faults.KeyIsWrittenTwice)
                        {
                            var setting = atTopLevel ? null : KnownSettings.Value.FirstOrDefault(known => known == topLevelKey);
                            faults = faults with { KeyIsWrittenTwice = true, SettingOfKeyWrittenTwice = setting };
                        }

                        if (atTopLevel)
                        {
                            topLevelKey = key;
                        }

                        break;
                }
            }
        }
        catch (JsonException exception)
        {
            // The reader counts lines and bytes from zero. Only the two numbers are taken from the failure.
            return new KeyFaults { NotJsonAt = ((exception.LineNumber ?? 0) + 1, (exception.BytePositionInLine ?? 0) + 1) };
        }

        return faults;
    }

    private static bool IsUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// What is said of a file that is not JSON (<see cref="KeyFaults.NotJsonAt"/>), as the end of a sentence
    /// about the file: the line and the position of the fault, and no text from the file (owner decision D110).
    /// </summary>
    internal static string NotJson((long Line, long Position) at) =>
        string.Create(CultureInfo.InvariantCulture, $"it is not JSON the tool can read, and the fault is at line {at.Line}, position {at.Position}");

    /// <summary>
    /// What every command says of a file the walk over its keys refuses (<see cref="FindKeyFaults"/>), or null
    /// when the walk refuses nothing a command stops for: a file that is not JSON, a key that is no text, a
    /// key that is not UTF-8. It repeats nothing of the file. A key written twice is not refused here: the
    /// reader of each setting refuses it, and <c>discover</c> says its own (owner decision D108).
    /// </summary>
    internal static UserFacingException? RefusalOf(KeyFaults faults, string path) =>
        faults.NotJsonAt is { } at ? new($"The configuration file {path} is not valid: {NotJson(at)}.")
        : faults.KeyIsNotText ? new($"The configuration file {path} is not valid: a key is written with an escape for half of a character pair, which no text can hold.")
        : faults.KeyIsNotUtf8 ? new($"The configuration file {path} is not valid: a key holds bytes that are not UTF-8, so it is not text. Save the file as UTF-8.")
        : null;

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
        using var content = new MemoryStream();
        await stream.CopyToAsync(content, cancellationToken);
        if (RefusalOf(FindKeyFaults(content.ToArray()), path) is { } refusal)
        {
            throw refusal;
        }

        content.Position = 0;
        try
        {
            var config = await JsonSerializer.DeserializeAsync<TelltaleConfig>(content, Options, cancellationToken)
                ?? throw new UserFacingException($"The configuration file {path} is empty.");
            return TelltaleSettings.From(config, requireDone);
        }
        catch (Exception exception) when (IsRefusal(exception))
        {
            throw NotValid(path, exception);
        }
    }
}
