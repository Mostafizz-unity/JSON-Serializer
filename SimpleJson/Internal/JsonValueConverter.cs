using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SimpleJson;

namespace SimpleJson.Internal
{
    /// <summary>
    /// Maps a parsed JsonNode tree onto a requested .NET type via reflection.
    /// This is where deserialization's real work happens; JsonParser only
    /// turns text into a generic tree, this turns that tree into the type
    /// the caller actually asked for.
    ///
    /// Every recursive call carries a "path" string (e.g. "$.Owner.Tags[2]")
    /// purely so error messages can say exactly where in the document a
    /// problem was found, instead of just "type mismatch" somewhere.
    /// </summary>
    internal static class JsonValueConverter
    {
        public static object? Convert(JsonNode node, Type targetType, string path)
        {
            Type? nullableUnderlying = Nullable.GetUnderlyingType(targetType);
            bool isNullableValueType = nullableUnderlying != null;
            Type effectiveType = nullableUnderlying ?? targetType;

            if (node.Type == JsonNodeType.Null)
            {
                if (effectiveType.IsValueType && !isNullableValueType)
                    throw new JsonDeserializationException(
                        $"Cannot assign JSON null to non-nullable value type '{targetType.FullName}' at '{path}'.");
                return null;
            }

            if (targetType == typeof(object))
                return ConvertToNatural(node);

            if (effectiveType == typeof(string))
            {
                if (node.Type != JsonNodeType.String)
                    throw Mismatch(node, "a string", path);
                return ((JsonStringNode)node).Value;
            }

            if (effectiveType == typeof(bool))
            {
                if (node.Type != JsonNodeType.Boolean)
                    throw Mismatch(node, "a bool", path);
                return ((JsonBooleanNode)node).Value;
            }

            if (effectiveType.IsEnum)
                return ConvertEnum(node, effectiveType, path);

            if (effectiveType == typeof(Guid))
            {
                if (node.Type != JsonNodeType.String)
                    throw Mismatch(node, "a GUID string", path);
                var text = ((JsonStringNode)node).Value;
                if (!Guid.TryParse(text, out var guid))
                    throw new JsonDeserializationException($"'{text}' is not a valid GUID at '{path}'.");
                return guid;
            }

            if (effectiveType == typeof(DateTime))
            {
                if (node.Type != JsonNodeType.String)
                    throw Mismatch(node, "a DateTime string", path);
                var text = ((JsonStringNode)node).Value;
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    throw new JsonDeserializationException($"'{text}' is not a valid DateTime at '{path}'.");
                return dt;
            }

            if (effectiveType == typeof(DateTimeOffset))
            {
                if (node.Type != JsonNodeType.String)
                    throw Mismatch(node, "a DateTimeOffset string", path);
                var text = ((JsonStringNode)node).Value;
                if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                    throw new JsonDeserializationException($"'{text}' is not a valid DateTimeOffset at '{path}'.");
                return dto;
            }

            if (IsNumericType(effectiveType))
            {
                if (node.Type != JsonNodeType.Number)
                    throw Mismatch(node, $"a number ({effectiveType.Name})", path);
                return ConvertNumber(((JsonNumberNode)node).RawText, effectiveType, path);
            }

            if (effectiveType.IsArray)
                return ConvertArray(node, effectiveType, path);

            if (TryGetDictionaryValueType(effectiveType, out var dictValueType))
                return ConvertDictionary(node, dictValueType!, path);

            if (TryGetEnumerableElementType(effectiveType, out var elementType))
                return ConvertEnumerable(node, effectiveType, elementType!, path);

            if (node.Type == JsonNodeType.Object)
                return ConvertToObject((JsonObjectNode)node, effectiveType, path);

            throw Mismatch(node, effectiveType.Name, path);
        }

        private static object ConvertEnum(JsonNode node, Type enumType, string path)
        {
            if (node.Type == JsonNodeType.String)
            {
                var text = ((JsonStringNode)node).Value;
                try
                {
                    return Enum.Parse(enumType, text, ignoreCase: true);
                }
                catch (Exception ex)
                {
                    var validNames = string.Join(", ", Enum.GetNames(enumType));
                    throw new JsonDeserializationException(
                        $"'{text}' is not a valid value of enum '{enumType.FullName}' at '{path}'. Valid values: {validNames}.", ex);
                }
            }

            if (node.Type == JsonNodeType.Number)
            {
                var raw = ((JsonNumberNode)node).RawText;
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric))
                    throw new JsonDeserializationException($"'{raw}' is not a valid underlying value for enum '{enumType.FullName}' at '{path}'.");
                return Enum.ToObject(enumType, numeric);
            }

            throw Mismatch(node, $"a string or number for enum '{enumType.Name}'", path);
        }

        private static object ConvertArray(JsonNode node, Type arrayType, string path)
        {
            if (node.Type != JsonNodeType.Array)
                throw Mismatch(node, "a JSON array", path);

            var arrayNode = (JsonArrayNode)node;
            var elementType = arrayType.GetElementType()!;
            var array = Array.CreateInstance(elementType, arrayNode.Items.Count);
            for (int i = 0; i < arrayNode.Items.Count; i++)
                array.SetValue(Convert(arrayNode.Items[i], elementType, $"{path}[{i}]"), i);
            return array;
        }

        private static object ConvertDictionary(JsonNode node, Type valueType, string path)
        {
            if (node.Type != JsonNodeType.Object)
                throw Mismatch(node, "a JSON object", path);

            var objNode = (JsonObjectNode)node;
            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
            var dict = (IDictionary)Activator.CreateInstance(dictType)!;
            foreach (var member in objNode.Members)
                dict[member.Key] = Convert(member.Value, valueType, $"{path}.{member.Key}");
            return dict;
        }

        private static object ConvertEnumerable(JsonNode node, Type targetType, Type elementType, string path)
        {
            if (node.Type != JsonNodeType.Array)
                throw Mismatch(node, "a JSON array", path);

            var arrayNode = (JsonArrayNode)node;
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            for (int i = 0; i < arrayNode.Items.Count; i++)
                list.Add(Convert(arrayNode.Items[i], elementType, $"{path}[{i}]"));

            if (targetType.IsAssignableFrom(listType))
                return list;

            // Target is some other concrete collection type (e.g. a custom
            // class implementing IEnumerable<T>) - fall back to building it
            // via its own parameterless constructor + public Add method.
            var ctor = targetType.GetConstructor(Type.EmptyTypes);
            if (ctor != null)
            {
                var addMethod = targetType.GetMethod("Add", new[] { elementType });
                if (addMethod != null)
                {
                    var instance = ctor.Invoke(null);
                    foreach (var item in list)
                        addMethod.Invoke(instance, new[] { item });
                    return instance;
                }
            }

            throw new JsonDeserializationException(
                $"Don't know how to construct a '{targetType.FullName}' from a JSON array at '{path}'. " +
                "Supported collection targets are arrays, List<T>, and types with a public parameterless " +
                "constructor plus a public Add(T) method.");
        }

        private static object ConvertToObject(JsonObjectNode node, Type targetType, string path)
        {
            var metadata = TypeMetadataCache.Get(targetType);
            if (metadata.ParameterlessConstructor == null)
                throw new JsonDeserializationException(
                    $"Type '{targetType.FullName}' has no public parameterless constructor and cannot be deserialized at '{path}'.");

            object instance;
            try
            {
                instance = metadata.ParameterlessConstructor.Invoke(null);
            }
            catch (Exception ex)
            {
                throw new JsonDeserializationException($"Failed to construct '{targetType.FullName}' at '{path}'.", ex);
            }

            // Unmatched JSON members are ignored (rather than erroring) so
            // that serializers evolve independently on both ends; unmatched
            // .NET properties simply keep whatever the constructor gave them.
            foreach (var prop in metadata.WritableProperties)
            {
                if (!node.TryGet(prop.Name, out var valueNode) || valueNode is null)
                    continue;

                object? converted;
                try
                {
                    converted = Convert(valueNode, prop.PropertyType, $"{path}.{prop.Name}");
                }
                catch (JsonDeserializationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new JsonDeserializationException(
                        $"Failed to convert property '{prop.Name}' on type '{targetType.FullName}' at '{path}'.", ex);
                }

                try
                {
                    prop.SetValue(instance, converted);
                }
                catch (Exception ex)
                {
                    throw new JsonDeserializationException(
                        $"Failed to set property '{prop.Name}' on type '{targetType.FullName}' at '{path}'.", ex);
                }
            }

            return instance;
        }

        /// <summary>
        /// Used when the target type is exactly `object` - there's no target
        /// shape to guide conversion, so JSON values map onto the most
        /// natural .NET equivalent: objects -> Dictionary&lt;string, object&gt;,
        /// arrays -> List&lt;object&gt;, integral-looking numbers -> long,
        /// everything else numeric -> double.
        /// </summary>
        private static object? ConvertToNatural(JsonNode node)
        {
            switch (node.Type)
            {
                case JsonNodeType.Null:
                    return null;
                case JsonNodeType.String:
                    return ((JsonStringNode)node).Value;
                case JsonNodeType.Boolean:
                    return ((JsonBooleanNode)node).Value;
                case JsonNodeType.Number:
                {
                    var raw = ((JsonNumberNode)node).RawText;
                    bool looksIntegral = raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0;
                    if (looksIntegral && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                        return l;
                    return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                case JsonNodeType.Array:
                {
                    var arr = (JsonArrayNode)node;
                    var list = new List<object?>();
                    foreach (var item in arr.Items) list.Add(ConvertToNatural(item));
                    return list;
                }
                case JsonNodeType.Object:
                {
                    var obj = (JsonObjectNode)node;
                    var dict = new Dictionary<string, object?>();
                    foreach (var member in obj.Members) dict[member.Key] = ConvertToNatural(member.Value);
                    return dict;
                }
                default:
                    throw new JsonDeserializationException("Unrecognized JSON node type.");
            }
        }

        private static object ConvertNumber(string raw, Type targetType, string path)
        {
            try
            {
                if (targetType == typeof(byte)) return byte.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(sbyte)) return sbyte.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(short)) return short.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(ushort)) return ushort.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(int)) return int.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(uint)) return uint.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(long)) return long.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(ulong)) return ulong.Parse(raw, CultureInfo.InvariantCulture);
                if (targetType == typeof(float)) return float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (targetType == typeof(double)) return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (targetType == typeof(decimal)) return decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new JsonDeserializationException($"'{raw}' is not a valid {targetType.Name} at '{path}'.", ex);
            }

            throw new JsonDeserializationException($"Unsupported numeric target type '{targetType.FullName}' at '{path}'.");
        }

        private static bool IsNumericType(Type t) =>
            t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) ||
            t == typeof(float) || t == typeof(double) || t == typeof(decimal);

        private static bool TryGetDictionaryValueType(Type type, out Type? valueType)
        {
            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>))
                {
                    var args = type.GetGenericArguments();
                    if (args[0] == typeof(string))
                    {
                        valueType = args[1];
                        return true;
                    }
                }
            }
            valueType = null;
            return false;
        }

        private static bool TryGetEnumerableElementType(Type type, out Type? elementType)
        {
            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(List<>) || def == typeof(IList<>) ||
                    def == typeof(ICollection<>) || def == typeof(IEnumerable<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            // Custom classes that implement IEnumerable<T> but aren't one of
            // the well-known shapes above.
            var ienum = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (ienum != null)
            {
                elementType = ienum.GetGenericArguments()[0];
                return true;
            }

            elementType = null;
            return false;
        }

        private static JsonDeserializationException Mismatch(JsonNode node, string expected, string path) =>
            new JsonDeserializationException($"Expected {expected} at '{path}' but found {Describe(node)}.");

        private static string Describe(JsonNode node) => node.Type switch
        {
            JsonNodeType.Object => "a JSON object",
            JsonNodeType.Array => "a JSON array",
            JsonNodeType.String => "a JSON string",
            JsonNodeType.Number => "a JSON number",
            JsonNodeType.Boolean => "a JSON boolean",
            JsonNodeType.Null => "JSON null",
            _ => "an unrecognized JSON value"
        };
    }
}
