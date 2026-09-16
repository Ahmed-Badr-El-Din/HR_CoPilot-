using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Domain.Common;

namespace HR.API.Http;

/// <summary>
/// Serializes the Domain's Guid-backed strong IDs (DocumentId, ChunkId, RunId,
/// ApprovalId, SessionId) as plain UUID strings for the API surface.
/// </summary>
public sealed class StrongIdJsonConverterFactory : JsonConverterFactory
{
    private static readonly HashSet<Type> Supported = new()
    {
        typeof(DocumentId),
        typeof(ChunkId),
        typeof(RunId),
        typeof(ApprovalId),
        typeof(SessionId),
    };

    public override bool CanConvert(Type typeToConvert) => Supported.Contains(typeToConvert);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter?)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert));

    private sealed class Converter<T> : JsonConverter<T> where T : struct
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = reader.GetString();
            if (!Guid.TryParse(raw, out var value))
                throw new JsonException($"Expected a Guid string for {typeToConvert.Name}.");
            return (T)Activator.CreateInstance(typeof(T), value)!;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString() ?? string.Empty);
    }
}
