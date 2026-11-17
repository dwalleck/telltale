using System.Text.Json;
using System.Text.Json.Serialization;
using Telltale.Core.Configuration;
using Telltale.Core.Intervals;

namespace Telltale.Cli;

/// <summary>
/// Reads the configuration file's <c>briefVisitTreatment</c>: an object whose parts each say what is done with
/// brief visits for one metric, <c>count</c> or <c>exclude</c> (owner decision D83). A part left out means
/// <c>count</c>. It is read by hand, as <see cref="MinimumGroupSizeConverter"/> reads its setting, so that
/// whatever is refused is named by the setting and nothing written in the file is repeated: a name or a value
/// typed there could be a secret pasted into the wrong place (owner decision D43). The rule that needs the
/// thresholds as well is <see cref="TelltaleSettings.CheckedBriefVisitTreatment"/>'s.
/// </summary>
internal sealed class BriefVisitTreatmentConverter : JsonConverter<BriefVisitTreatmentSetting>
{
    private const string Setting = BriefVisitTreatmentSetting.Name;
    private const string TimeInStatusName = BriefVisitTreatmentSetting.TimeInStatusName;

    /// <summary>A <c>null</c> is refused here, not read as the setting being left out.</summary>
    public override bool HandleNull => true;

    public override BriefVisitTreatmentSetting Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{Setting} must be an object with the part {TimeInStatusName}; leave the setting out to count brief visits and change no figure.");
        }

        BriefVisitTreatment? timeInStatus = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isTimeInStatus = reader.ValueTextEquals(TimeInStatusName);
            reader.Read();

            // The name found is not repeated: the message names the part the tool knows.
            if (!isTimeInStatus)
            {
                throw new JsonException($"{Setting} has a part the tool does not know. The only part is {TimeInStatusName}.");
            }

            // As for every setting: with two values, the reader of the file cannot tell which one is in force.
            if (timeInStatus is not null)
            {
                throw new JsonException($"{Setting}.{TimeInStatusName} is written twice.");
            }

            // A number, a boolean, a null, a list and an object are not labels, and neither is any other text.
            timeInStatus = reader.TokenType == JsonTokenType.String && BriefVisitTreatments.TryParse(reader.GetString(), out var treatment)
                ? treatment
                : throw new JsonException($"{Setting}.{TimeInStatusName} must be written as \"{BriefVisitTreatments.Count}\" or \"{BriefVisitTreatments.Exclude}\".");
        }

        return timeInStatus is { } written ? new BriefVisitTreatmentSetting { TimeInStatus = written } : BriefVisitTreatmentSetting.None;
    }

    /// <summary>Nothing writes the setting: <c>discover</c> carries over the text the file has, and no configuration is ever serialised.</summary>
    public override void Write(Utf8JsonWriter writer, BriefVisitTreatmentSetting value, JsonSerializerOptions options) =>
        throw new NotSupportedException($"{Setting} is never written by the tool.");
}
