using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Reads an id that a feed sends as either a string or a number, so a vendor switching
/// between the two does not make the whole feed unreadable.
/// </summary>
public sealed class StringOrNumberConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
        JsonTokenType.Null => null,
        _ => throw new JsonException($"Expected a string or a number, got {reader.TokenType}."),
    };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
