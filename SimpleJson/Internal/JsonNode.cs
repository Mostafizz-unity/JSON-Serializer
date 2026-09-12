using System.Collections.Generic;

namespace SimpleJson.Internal
{
    internal enum JsonNodeType { Object, Array, String, Number, Boolean, Null }

    /// <summary>
    /// A parsed-but-not-yet-typed piece of JSON. The parser produces a tree of
    /// these from raw text; JsonValueConverter later maps a tree onto whatever
    /// .NET type the caller asked for. Splitting parsing from type-mapping this
    /// way means the parser never needs to know about reflection, and the
    /// converter never needs to know about JSON syntax.
    /// </summary>
    internal abstract class JsonNode
    {
        public abstract JsonNodeType Type { get; }
    }

    internal sealed class JsonObjectNode : JsonNode
    {
        public override JsonNodeType Type => JsonNodeType.Object;

        // A list (not a Dictionary) so that duplicate keys and member order
        // from the source JSON are preserved rather than silently collapsed.
        public List<KeyValuePair<string, JsonNode>> Members { get; } = new List<KeyValuePair<string, JsonNode>>();

        /// <summary>Looks up a member by name, case-insensitively.</summary>
        public bool TryGet(string key, out JsonNode? value)
        {
            foreach (var kv in Members)
            {
                if (string.Equals(kv.Key, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    value = kv.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }
    }

    internal sealed class JsonArrayNode : JsonNode
    {
        public override JsonNodeType Type => JsonNodeType.Array;
        public List<JsonNode> Items { get; } = new List<JsonNode>();
    }

    internal sealed class JsonStringNode : JsonNode
    {
        public override JsonNodeType Type => JsonNodeType.String;
        public string Value { get; }
        public JsonStringNode(string value) { Value = value; }
    }

    internal sealed class JsonNumberNode : JsonNode
    {
        public override JsonNodeType Type => JsonNodeType.Number;

        // Kept as the original text, not parsed here, because the "right"
        // .NET numeric type (int? double? decimal?) depends on the target
        // property - something only the converter knows.
        public string RawText { get; }
        public JsonNumberNode(string rawText) { RawText = rawText; }
    }

    internal sealed class JsonBooleanNode : JsonNode
    {
        public override JsonNodeType Type => JsonNodeType.Boolean;
        public bool Value { get; }
        public JsonBooleanNode(bool value) { Value = value; }
    }

    internal sealed class JsonNullNode : JsonNode
    {
        public override JsonNodeType Type => JsonNodeType.Null;
        public static readonly JsonNullNode Instance = new JsonNullNode();
        private JsonNullNode() { }
    }
}
