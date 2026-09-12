using System;

namespace SimpleJson
{
    /// <summary>
    /// Thrown when the JSON text being parsed is not well-formed
    /// (bad tokens, unterminated strings, malformed numbers, trailing
    /// content, etc). Always reports the position where parsing failed.
    /// </summary>
    public class JsonParseException : Exception
    {
        /// <summary>Zero-based character offset into the input where the error was detected.</summary>
        public int Position { get; }

        /// <summary>1-based line number where the error was detected.</summary>
        public int Line { get; }

        /// <summary>1-based column number where the error was detected.</summary>
        public int Column { get; }

        public JsonParseException(string message, int position, int line, int column)
            : base($"{message} (line {line}, column {column}, position {position})")
        {
            Position = position;
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// Thrown when an object graph cannot be turned into JSON text -
    /// for example a circular reference, or a property getter that throws.
    /// </summary>
    public class JsonSerializationException : Exception
    {
        public JsonSerializationException(string message) : base(message) { }
        public JsonSerializationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Thrown when parsed JSON cannot be converted into the requested .NET
    /// type - a type mismatch, an invalid enum name, a JSON null assigned to
    /// a non-nullable value type, and so on. The message always names the
    /// JSON path (e.g. "$.Owner.Tags[2]") where the problem was found.
    /// </summary>
    public class JsonDeserializationException : Exception
    {
        public JsonDeserializationException(string message) : base(message) { }
        public JsonDeserializationException(string message, Exception inner) : base(message, inner) { }
    }
}
