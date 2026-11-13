using System.Text.Json;
using System.Text.Json.Serialization;
using Telltale.Core.Configuration;

namespace Telltale.Cli;

/// <summary>
/// Reads the configuration file's <c>minimumGroupSize</c>: an object with exactly the two whole numbers
/// <c>team</c> and <c>assigneesBehindAFigure</c> (owner decision D64). Anything else is refused with a message
/// that names the setting, because a value that loaded as something else would leave suppression in a state
/// the owner did not choose: <c>null</c> or <c>false</c> read as the default, a missing number read as zero.
/// The rule on the numbers themselves (zero or more) is <see cref="TelltaleSettings.From"/>'s.
/// </summary>
internal sealed class MinimumGroupSizeConverter : JsonConverter<MinimumGroupSize>
{
    private const string Setting = "minimumGroupSize";
    private const string TeamName = "team";
    private const string AssigneesBehindAFigureName = "assigneesBehindAFigure";

    /// <summary>A <c>null</c> is refused here, not read as the setting being left out.</summary>
    public override bool HandleNull => true;

    public override MinimumGroupSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"{Setting} must be an object with the two numbers team and assigneesBehindAFigure; leave the setting out for five and three.");
        }

        int? team = null;
        int? assigneesBehindAFigure = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case TeamName:
                    team = WholeNumber(ref reader, name, written: team is not null);
                    break;
                case AssigneesBehindAFigureName:
                    assigneesBehindAFigure = WholeNumber(ref reader, name, written: assigneesBehindAFigure is not null);
                    break;
                default:
                    throw new JsonException($"{Setting}.{name} is not a setting the tool knows.");
            }
        }

        return new MinimumGroupSize
        {
            Team = team ?? throw Missing(TeamName),
            AssigneesBehindAFigure = assigneesBehindAFigure ?? throw Missing(AssigneesBehindAFigureName),
        };
    }

    /// <summary>The setting as the file has it, for <c>discover</c> to write into a new file: the two numbers under the names <see cref="Read"/> reads.</summary>
    public override void Write(Utf8JsonWriter writer, MinimumGroupSize value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(TeamName, value.Team);
        writer.WriteNumber(AssigneesBehindAFigureName, value.AssigneesBehindAFigure);
        writer.WriteEndObject();
    }

    /// <summary>
    /// A JSON number written without a fraction or an exponent that fits an integer: 5, not 5.0, 1e1 or
    /// 3000000000. A quoted number, a boolean and a null are not numbers.
    /// </summary>
    private static int WholeNumber(ref Utf8JsonReader reader, string name, bool written)
    {
        if (written)
        {
            throw new JsonException($"{Setting}.{name} is written twice.");
        }

        return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number)
            ? number
            : throw new JsonException(MinimumGroupSize.NumberRefused(name));
    }

    private static JsonException Missing(string name) =>
        new($"{Setting}.{name} is missing: when {Setting} is present, both of its numbers are required.");
}
