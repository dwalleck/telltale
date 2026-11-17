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
/// typed there could be a secret pasted into the wrong place (owner decision D43). The rules that need the
/// thresholds and the status roles as well are <see cref="TelltaleSettings.CheckedBriefVisitTreatment"/>'s.
/// </summary>
internal sealed class BriefVisitTreatmentConverter : JsonConverter<BriefVisitTreatmentSetting>
{
    private const string Setting = BriefVisitTreatmentSetting.Name;
    private const string TimeInStatusName = BriefVisitTreatmentSetting.TimeInStatusName;
    private const string CycleTimeName = BriefVisitTreatmentSetting.CycleTimeName;

    /// <summary>A <c>null</c> is refused here, not read as the setting being left out.</summary>
    public override bool HandleNull => true;

    public override BriefVisitTreatmentSetting Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{Setting} must be an object with the parts {TimeInStatusName} and {CycleTimeName}; leave the setting out to count brief visits and change no figure.");
        }

        BriefVisitTreatment? timeInStatus = null;
        BriefVisitTreatment? cycleTime = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isTimeInStatus = reader.ValueTextEquals(TimeInStatusName);

            // The name found is not repeated: the message names the parts the tool knows.
            if (!isTimeInStatus && !reader.ValueTextEquals(CycleTimeName))
            {
                throw new JsonException($"{Setting} has a part the tool does not know. The parts are {TimeInStatusName} and {CycleTimeName}.");
            }

            reader.Read();
            if (isTimeInStatus)
            {
                timeInStatus = ReadPart(ref reader, TimeInStatusName, timeInStatus);
            }
            else
            {
                cycleTime = ReadPart(ref reader, CycleTimeName, cycleTime);
            }
        }

        return new BriefVisitTreatmentSetting
        {
            TimeInStatus = timeInStatus ?? BriefVisitTreatmentSetting.None.TimeInStatus,
            CycleTime = cycleTime ?? BriefVisitTreatmentSetting.None.CycleTime,
        };
    }

    /// <summary>The treatment written for <paramref name="part"/>, on whose value the reader stands. <paramref name="written"/> is what an earlier value of the same part gave.</summary>
    private static BriefVisitTreatment ReadPart(ref Utf8JsonReader reader, string part, BriefVisitTreatment? written)
    {
        // As for every setting: with two values, the reader of the file cannot tell which one is in force.
        if (written is not null)
        {
            throw new JsonException($"{Setting}.{part} is written twice.");
        }

        // A number, a boolean, a null, a list and an object are not labels, and neither is any other text.
        return reader.TokenType == JsonTokenType.String && BriefVisitTreatments.TryParse(reader.GetString(), out var treatment)
            ? treatment
            : throw new JsonException($"{Setting}.{part} must be written as \"{BriefVisitTreatments.Count}\" or \"{BriefVisitTreatments.Exclude}\".");
    }

    /// <summary>Nothing writes the setting: <c>discover</c> carries over the text the file has, and no configuration is ever serialised.</summary>
    public override void Write(Utf8JsonWriter writer, BriefVisitTreatmentSetting value, JsonSerializerOptions options) =>
        throw new NotSupportedException($"{Setting} is never written by the tool.");
}
