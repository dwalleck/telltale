using System.Text.Json;
using System.Text.Json.Serialization;
using Telltale.Core.Configuration;

namespace Telltale.Cli;

/// <summary>
/// Reads the configuration file's <c>briefVisitThresholds</c>: an object of status ID to threshold in minutes
/// (owner decisions D58, D59). It is read by hand because the serializer's own message for a value of the wrong
/// type gives the value's place in the file, which is the status ID it stands under, and no message may repeat
/// a status ID: what was written there could be a secret pasted into the wrong place (owner decision D43). A
/// <c>null</c> is refused, not read as the setting being left out. The rules on the statuses and on the
/// numbers themselves are <see cref="TelltaleSettings.CheckedBriefVisitThresholds"/>'s.
/// </summary>
internal sealed class BriefVisitThresholdsConverter : JsonConverter<IReadOnlyDictionary<string, double>>
{
    private const string Setting = "briefVisitThresholds";

    /// <summary>A <c>null</c> is refused here, not read as the setting being left out.</summary>
    public override bool HandleNull => true;

    public override IReadOnlyDictionary<string, double> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{Setting} must be an object of status IDs, each with its threshold in minutes; leave the setting out to check no status.");
        }

        var thresholds = new Dictionary<string, double>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var statusId = reader.GetString()!;
            reader.Read();

            // A quoted number, a boolean, a null, a list and an object are not numbers.
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var minutes))
            {
                throw new JsonException(TelltaleSettings.BriefVisitThresholdRefused);
            }

            // As for every setting: with two values, the reader of the file cannot tell which one is in force.
            if (!thresholds.TryAdd(statusId, minutes))
            {
                throw new JsonException($"{Setting} lists a status twice.");
            }
        }

        return thresholds;
    }

    /// <summary>The setting as the file has it. Nothing writes it today: <c>discover</c> carries the file's own text over.</summary>
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, double> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (statusId, minutes) in value)
        {
            writer.WriteNumber(statusId, minutes);
        }

        writer.WriteEndObject();
    }
}
