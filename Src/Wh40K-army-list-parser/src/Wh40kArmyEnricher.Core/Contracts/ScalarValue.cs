using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wh40kArmyEnricher.Core.Contracts;

[JsonConverter(typeof(ScalarValueJsonConverter))]
public readonly struct ScalarValue
{
    private readonly string? _value;

    public ScalarValue(string? value) => _value = value;

    public static ScalarValue FromInt(int n) => new(n.ToString());

    public override string ToString() => _value ?? "0";

    public static implicit operator ScalarValue(int n) => FromInt(n);
    public static implicit operator ScalarValue(string s) => new(s);
}

public sealed class ScalarValueJsonConverter : JsonConverter<ScalarValue>
{
    public override ScalarValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => ScalarValue.FromInt(reader.GetInt32()),
            JsonTokenType.String => new ScalarValue(reader.GetString()),
            _ => throw new JsonException($"Cannot deserialise ScalarValue from token {reader.TokenType}")
        };

    public override void Write(Utf8JsonWriter writer, ScalarValue value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
