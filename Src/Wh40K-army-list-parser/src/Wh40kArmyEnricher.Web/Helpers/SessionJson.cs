using System.Text.Json;
using System.Text.Json.Serialization;
using Wh40kArmyEnricher.Contracts;

namespace Wh40kArmyEnricher.Web.Helpers;

/// <summary>
/// Shared JsonSerializerOptions used for reading/writing army data to session.
/// ScalarValue must be handled explicitly because its backing fields are private
/// and System.Text.Json cannot serialise them by reflection.
/// </summary>
public static class SessionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new ScalarValueJsonConverter() }
    };
}

/// <summary>
/// Serialises <see cref="ScalarValue"/> as a JSON number (when integer) or string (when dice expression).
/// </summary>
public sealed class ScalarValueJsonConverter : JsonConverter<ScalarValue>
{
    public override ScalarValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => new ScalarValue(reader.GetInt32()),
            JsonTokenType.String => new ScalarValue(reader.GetString()!),
            _ => throw new JsonException($"Expected number or string for ScalarValue, got {reader.TokenType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ScalarValue value, JsonSerializerOptions options)
    {
        if (value.IsInt)
            writer.WriteNumberValue(value.IntValue);
        else
            writer.WriteStringValue(value.StringValue);
    }
}
