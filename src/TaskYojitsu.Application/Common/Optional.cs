using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskYojitsu.Application.Common;

/// <summary>
/// 変更の要求（PATCH）で、「送られなかった項目」と「空にする（null）」を区別するための型。
/// 送られなかった項目は HasValue が false になる（送られた項目だけを変える。詳細設計書 5.4.3）。
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public Optional(T? value)
    {
        HasValue = true;
        Value = value;
    }

    public bool HasValue { get; }

    public T? Value { get; }

    public static implicit operator Optional<T>(T? value) => new(value);
}

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter?)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(valueType));
    }

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
