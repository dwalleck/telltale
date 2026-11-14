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
/// numbers themselves are <see cref="TelltaleSettings.CheckedBriefVisitThresholds"/>'s. The setting has a type
/// of its own, and <see cref="ConfigFile"/> registers this reader for that type, as it does
/// <see cref="MinimumGroupSizeConverter"/>.
/// </summary>
internal sealed class BriefVisitThresholdsConverter : JsonConverter<BriefVisitThresholdsInMinutes>
{
    private const string Setting = "briefVisitThresholds";

    /// <summary>A <c>null</c> is refused here, not read as the setting being left out.</summary>
    public override bool HandleNull => true;

    public override BriefVisitThresholdsInMinutes Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

        return new BriefVisitThresholdsInMinutes(thresholds);
    }

    /// <summary>Nothing writes the setting: <c>discover</c> carries over the text the file has, and no configuration is ever serialised.</summary>
    public override void Write(Utf8JsonWriter writer, BriefVisitThresholdsInMinutes value, JsonSerializerOptions options) =>
        throw new NotSupportedException($"{Setting} is never written by the tool.");
}
