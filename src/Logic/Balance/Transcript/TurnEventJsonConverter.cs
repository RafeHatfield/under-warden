using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnderWarden.Logic.Core;

namespace UnderWarden.Logic.Balance.Transcript;

/// <summary>
/// Serializes any <see cref="TurnEvent"/> subtype generically: emits an
/// <c>event_type</c> discriminator (the runtime type name, "Event" suffix
/// stripped) followed by all public readable properties, run through the
/// options' naming policy so keys are snake_case like the rest of the schema.
///
/// EXTENSIBILITY: this is the open seam the rubric thread leans on. New TurnEvent
/// subtypes — including future mechanical trigger events the silent_failure
/// inventory may require — are captured automatically with no change here. There
/// is no per-type registration to maintain.
///
/// Deserialization is not supported: transcripts are append-only output. The
/// Analyst reads events as untyped JSON objects keyed by event_type. Reflection
/// serialization is harness/desktop-only (never the iOS NativeAOT path), so the
/// dynamic property walk is safe.
/// </summary>
public sealed class TurnEventJsonConverter : JsonConverter<TurnEvent>
{
    // Cache property lists per concrete type — events fire in tight loops.
    private static readonly Dictionary<Type, PropertyInfo[]> _propCache = new();

    public override bool CanConvert(Type typeToConvert) => typeof(TurnEvent).IsAssignableFrom(typeToConvert);

    public override TurnEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Enriched transcripts are write-only; events are read as untyped JSON.");

    public override void Write(Utf8JsonWriter writer, TurnEvent value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        writer.WriteStartObject();

        // Discriminator. Strip the conventional "Event" suffix for readability:
        // AttackEvent -> "Attack", VoiceLineEvent -> "VoiceLine".
        var name = type.Name;
        if (name.EndsWith("Event", StringComparison.Ordinal) && name.Length > "Event".Length)
            name = name[..^"Event".Length];
        writer.WriteString("event_type", name);

        foreach (var prop in PropsFor(type))
        {
            object? propValue = prop.GetValue(value);
            string key = options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, propValue, prop.PropertyType, options);
        }

        writer.WriteEndObject();
    }

    private static PropertyInfo[] PropsFor(Type type)
    {
        lock (_propCache)
        {
            if (_propCache.TryGetValue(type, out var cached))
                return cached;

            var props = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray();
            _propCache[type] = props;
            return props;
        }
    }
}
