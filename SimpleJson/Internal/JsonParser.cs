using System.Collections.Generic;
using SimpleJson;

namespace SimpleJson.Internal
{
    /// <summary>
    /// Recursive-descent parser: token stream -> JsonNode tree.
    /// One JSON value is read, and any non-whitespace content left over
    /// afterwards is treated as an error (e.g. "123 456" is not one document).
    /// </summary>
    internal sealed class JsonParser
    {
        private readonly JsonTokenizer _tokenizer;
        private JsonToken _current;

        private JsonParser(string text)
        {
            _tokenizer = new JsonTokenizer(text);
            _current = _tokenizer.NextToken();
        }

        public static JsonNode ParseText(string text)
        {
            var parser = new JsonParser(text);

            if (parser._current.Type == JsonTokenType.EndOfInput)
                throw new JsonParseException("Input was empty; expected a JSON value", 0, 1, 1);

            var node = parser.ParseValue();

            if (parser._current.Type != JsonTokenType.EndOfInput)
                throw new JsonParseException(
                    "Unexpected trailing content after the JSON value",
                    parser._current.Position, parser._current.Line, parser._current.Column);

            return node;
        }

        private void Advance() => _current = _tokenizer.NextToken();

        private JsonNode ParseValue()
        {
            switch (_current.Type)
            {
                case JsonTokenType.ObjectStart:
                    return ParseObject();
                case JsonTokenType.ArrayStart:
                    return ParseArray();
                case JsonTokenType.String:
                {
                    var node = new JsonStringNode(_current.Text!);
                    Advance();
                    return node;
                }
                case JsonTokenType.Number:
                {
                    var node = new JsonNumberNode(_current.Text!);
                    Advance();
                    return node;
                }
                case JsonTokenType.True:
                    Advance();
                    return new JsonBooleanNode(true);
                case JsonTokenType.False:
                    Advance();
                    return new JsonBooleanNode(false);
                case JsonTokenType.Null:
                    Advance();
                    return JsonNullNode.Instance;
                case JsonTokenType.EndOfInput:
                    throw new JsonParseException(
                        "Unexpected end of input while expecting a value",
                        _current.Position, _current.Line, _current.Column);
                default:
                    throw new JsonParseException(
                        $"Unexpected token '{_current.Text}' while expecting a value",
                        _current.Position, _current.Line, _current.Column);
            }
        }

        private JsonNode ParseObject()
        {
            var node = new JsonObjectNode();
            Advance(); // consume '{'

            if (_current.Type == JsonTokenType.ObjectEnd)
            {
                Advance();
                return node;
            }

            while (true)
            {
                if (_current.Type != JsonTokenType.String)
                    throw new JsonParseException(
                        "Expected a string key", _current.Position, _current.Line, _current.Column);

                string key = _current.Text!;
                Advance();

                if (_current.Type != JsonTokenType.Colon)
                    throw new JsonParseException(
                        "Expected ':' after object key", _current.Position, _current.Line, _current.Column);
                Advance();

                var value = ParseValue();
                node.Members.Add(new KeyValuePair<string, JsonNode>(key, value));

                if (_current.Type == JsonTokenType.Comma)
                {
                    Advance();
                    // A trailing comma before '}' is invalid JSON - require
                    // another key to follow so "{ "a": 1, }" is rejected.
                    continue;
                }

                if (_current.Type == JsonTokenType.ObjectEnd)
                {
                    Advance();
                    break;
                }

                throw new JsonParseException(
                    "Expected ',' or '}' in object", _current.Position, _current.Line, _current.Column);
            }

            return node;
        }

        private JsonNode ParseArray()
        {
            var node = new JsonArrayNode();
            Advance(); // consume '['

            if (_current.Type == JsonTokenType.ArrayEnd)
            {
                Advance();
                return node;
            }

            while (true)
            {
                var value = ParseValue();
                node.Items.Add(value);

                if (_current.Type == JsonTokenType.Comma)
                {
                    Advance();
                    continue;
                }

                if (_current.Type == JsonTokenType.ArrayEnd)
                {
                    Advance();
                    break;
                }

                throw new JsonParseException(
                    "Expected ',' or ']' in array", _current.Position, _current.Line, _current.Column);
            }

            return node;
        }
    }
}
