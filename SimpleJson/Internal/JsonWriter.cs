using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimpleJson;

namespace SimpleJson.Internal
{
    /// <summary>
    /// Walks an object graph via reflection and writes it out as JSON text.
    ///
    /// Design notes:
    /// - Property discovery goes through TypeMetadataCache, not raw reflection
    ///   calls, so repeated serialization of the same type is fast (see
    ///   PERFORMANCE.md).
    /// - Nullable value types (int?, DateTime?, ...) need no special handling
    ///   here: when a property getter returns a Nullable&lt;T&gt;, the CLR
    ///   boxes it as either `null` or a boxed `T`, so by the time WriteValue
    ///   sees it, it's already just "null" or a plain value.
    /// - Circular references are caught with a reference-identity stack
    ///   (_ancestors). If an object is encountered while it is still one of
    ///   its own ancestors in the graph, that's a cycle. Rather than silently
    ///   emitting null or truncating the cycle (which would produce JSON that
    ///   looks complete but has silently lost data), this throws a
    ///   JsonSerializationException that names the offending type - a loud
    ///   failure was judged better than a quietly wrong result.
    /// </summary>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly HashSet<object> _ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);

        public string Write(object? value)
        {
            WriteValue(value);
            return _sb.ToString();
        }

        private void WriteValue(object? value)
        {
            if (value is null)
            {
                _sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    WriteString(s);
                    return;
                case bool b:
                    _sb.Append(b ? "true" : "false");
                    return;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    _sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case float f:
                    WriteFloatingPoint(f.ToString(CultureInfo.InvariantCulture));
                    return;
                case double d:
                    WriteFloatingPoint(d.ToString(CultureInfo.InvariantCulture));
                    return;
                case decimal dec:
                    _sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                    return;
                case DateTime dt:
                    // Always normalized to UTC and written as ISO-8601 ("o"),
                    // e.g. "2024-01-01T00:00:00.0000000Z". This is the most
                    // widely understood text representation of a DateTime and
                    // avoids ambiguity about which time zone was intended.
                    WriteString(dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                    return;
                case DateTimeOffset dto:
                    WriteString(dto.ToString("o", CultureInfo.InvariantCulture));
                    return;
                case Guid g:
                    WriteString(g.ToString());
                    return;
                case Enum e:
                    // Written as its name ("Admin"), not its numeric value,
                    // so the JSON stays readable and stable if enum members
                    // are ever reordered.
                    WriteString(e.ToString());
                    return;
            }

            if (value is IDictionary dict)
            {
                WriteDictionary(dict);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                WriteArray(enumerable);
                return;
            }

            WriteObject(value);
        }

        private void WriteFloatingPoint(string raw)
        {
            // JSON has no representation for NaN/Infinity. Rather than throw
            // (which would make an entire object graph unserializable because
            // of one bad float), fall back to null - matching common
            // behavior in other JSON libraries.
            if (raw is "NaN" or "Infinity" or "-Infinity")
                _sb.Append("null");
            else
                _sb.Append(raw);
        }

        private void WriteObject(object value)
        {
            if (!_ancestors.Add(value))
            {
                throw new JsonSerializationException(
                    $"Circular reference detected while serializing an instance of '{value.GetType().FullName}'. " +
                    "This serializer does not represent cycles in JSON output - break the cycle, or exclude the " +
                    "back-reference property, before serializing.");
            }

            try
            {
                var metadata = TypeMetadataCache.Get(value.GetType());
                _sb.Append('{');
                bool first = true;
                foreach (var prop in metadata.ReadableProperties)
                {
                    object? propValue;
                    try
                    {
                        propValue = prop.GetValue(value);
                    }
                    catch (Exception ex)
                    {
                        throw new JsonSerializationException(
                            $"Failed to read property '{prop.Name}' on type '{value.GetType().FullName}'.", ex);
                    }

                    if (!first) _sb.Append(',');
                    first = false;
                    WriteString(prop.Name);
                    _sb.Append(':');
                    WriteValue(propValue);
                }
                _sb.Append('}');
            }
            finally
            {
                _ancestors.Remove(value);
            }
        }

        private void WriteDictionary(IDictionary dict)
        {
            if (!_ancestors.Add(dict))
                throw new JsonSerializationException("Circular reference detected while serializing a dictionary.");

            try
            {
                _sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Key is null)
                        throw new JsonSerializationException("Dictionary keys must not be null.");

                    if (!first) _sb.Append(',');
                    first = false;
                    WriteString(entry.Key.ToString() ?? "");
                    _sb.Append(':');
                    WriteValue(entry.Value);
                }
                _sb.Append('}');
            }
            finally
            {
                _ancestors.Remove(dict);
            }
        }

        private void WriteArray(IEnumerable enumerable)
        {
            if (!_ancestors.Add(enumerable))
                throw new JsonSerializationException("Circular reference detected while serializing a collection.");

            try
            {
                _sb.Append('[');
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first) _sb.Append(',');
                    first = false;
                    WriteValue(item);
                }
                _sb.Append(']');
            }
            finally
            {
                _ancestors.Remove(enumerable);
            }
        }

        private void WriteString(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
