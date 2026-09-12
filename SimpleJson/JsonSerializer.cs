using System;
using SimpleJson.Internal;

namespace SimpleJson
{
    /// <summary>
    /// Converts .NET objects to and from JSON text using reflection only -
    /// no System.Text.Json, no Newtonsoft.Json, no third-party JSON library.
    ///
    /// This class is intentionally tiny: it just wires together a tokenizer,
    /// a parser, a writer, and a reflection-based value converter. See
    /// README.md for supported types and design decisions.
    /// </summary>
    public static class JsonSerializer
    {
        /// <summary>Serializes any object graph to a JSON string.</summary>
        public static string Serialize(object? value) => new JsonWriter().Write(value);

        /// <summary>Serializes a value of a known compile-time type to a JSON string.</summary>
        public static string Serialize<T>(T value) => Serialize((object?)value);

        /// <summary>Parses JSON text and converts it into an instance of <typeparamref name="T"/>.</summary>
        public static T? Deserialize<T>(string json) => (T?)Deserialize(json, typeof(T));

        /// <summary>Parses JSON text and converts it into an instance of <paramref name="targetType"/>.</summary>
        public static object? Deserialize(string json, Type targetType)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));
            if (targetType is null) throw new ArgumentNullException(nameof(targetType));

            JsonNode root = JsonParser.ParseText(json);
            return JsonValueConverter.Convert(root, targetType, "$");
        }
    }
}
