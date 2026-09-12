using System;
using System.Globalization;
using System.Text;
using SimpleJson;

namespace SimpleJson.Internal
{
    internal enum JsonTokenType
    {
        ObjectStart,
        ObjectEnd,
        ArrayStart,
        ArrayEnd,
        Colon,
        Comma,
        String,
        Number,
        True,
        False,
        Null,
        EndOfInput
    }

    internal readonly struct JsonToken
    {
        public JsonTokenType Type { get; }

        /// <summary>
        /// For String tokens: the decoded string value (escapes already resolved).
        /// For Number tokens: the raw literal text, kept as text so the caller
        /// can parse it into whatever numeric type it actually needs.
        /// </summary>
        public string? Text { get; }

        public int Position { get; }
        public int Line { get; }
        public int Column { get; }

        public JsonToken(JsonTokenType type, string? text, int position, int line, int column)
        {
            Type = type;
            Text = text;
            Position = position;
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// Turns raw JSON text into a stream of tokens. Knows nothing about JSON
    /// grammar (object/array nesting rules) - that is the parser's job. Keeping
    /// this split means lexical errors ("bad character", "unterminated string")
    /// and grammatical errors ("expected ',' or '}'") get separate, clearer
    /// messages instead of one tangled parsing routine.
    /// </summary>
    internal sealed class JsonTokenizer
    {
        private readonly string _text;
        private int _pos;
        private int _line = 1;
        private int _column = 1;

        public JsonTokenizer(string text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public JsonToken NextToken()
        {
            SkipWhitespace();

            if (_pos >= _text.Length)
                return new JsonToken(JsonTokenType.EndOfInput, null, _pos, _line, _column);

            char c = _text[_pos];
            int startPos = _pos;
            int startLine = _line;
            int startCol = _column;

            switch (c)
            {
                case '{': Advance(); return new JsonToken(JsonTokenType.ObjectStart, "{", startPos, startLine, startCol);
                case '}': Advance(); return new JsonToken(JsonTokenType.ObjectEnd, "}", startPos, startLine, startCol);
                case '[': Advance(); return new JsonToken(JsonTokenType.ArrayStart, "[", startPos, startLine, startCol);
                case ']': Advance(); return new JsonToken(JsonTokenType.ArrayEnd, "]", startPos, startLine, startCol);
                case ':': Advance(); return new JsonToken(JsonTokenType.Colon, ":", startPos, startLine, startCol);
                case ',': Advance(); return new JsonToken(JsonTokenType.Comma, ",", startPos, startLine, startCol);
                case '"':
                    return ReadString(startPos, startLine, startCol);
                case 't':
                    Expect("true", startPos, startLine, startCol);
                    return new JsonToken(JsonTokenType.True, "true", startPos, startLine, startCol);
                case 'f':
                    Expect("false", startPos, startLine, startCol);
                    return new JsonToken(JsonTokenType.False, "false", startPos, startLine, startCol);
                case 'n':
                    Expect("null", startPos, startLine, startCol);
                    return new JsonToken(JsonTokenType.Null, "null", startPos, startLine, startCol);
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ReadNumber(startPos, startLine, startCol);
                    throw new JsonParseException($"Unexpected character '{c}'", startPos, startLine, startCol);
            }
        }

        private void Expect(string literal, int startPos, int startLine, int startCol)
        {
            if (_pos + literal.Length > _text.Length || string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                throw new JsonParseException($"Expected literal '{literal}'", startPos, startLine, startCol);
            for (int i = 0; i < literal.Length; i++) Advance();
        }

        private JsonToken ReadString(int startPos, int startLine, int startCol)
        {
            Advance(); // consume opening quote
            var sb = new StringBuilder();

            while (true)
            {
                if (_pos >= _text.Length)
                    throw new JsonParseException("Unterminated string literal", startPos, startLine, startCol);

                char c = _text[_pos];

                if (c == '"')
                {
                    Advance();
                    break;
                }

                if (c == '\\')
                {
                    Advance();
                    if (_pos >= _text.Length)
                        throw new JsonParseException("Unterminated escape sequence", _pos, _line, _column);

                    char esc = _text[_pos];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); Advance(); break;
                        case '\\': sb.Append('\\'); Advance(); break;
                        case '/': sb.Append('/'); Advance(); break;
                        case 'b': sb.Append('\b'); Advance(); break;
                        case 'f': sb.Append('\f'); Advance(); break;
                        case 'n': sb.Append('\n'); Advance(); break;
                        case 'r': sb.Append('\r'); Advance(); break;
                        case 't': sb.Append('\t'); Advance(); break;
                        case 'u':
                            Advance();
                            if (_pos + 4 > _text.Length)
                                throw new JsonParseException("Incomplete unicode escape sequence", _pos, _line, _column);
                            string hex = _text.Substring(_pos, 4);
                            if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                                throw new JsonParseException($"Invalid unicode escape '\\u{hex}'", _pos, _line, _column);
                            sb.Append((char)code);
                            for (int i = 0; i < 4; i++) Advance();
                            break;
                        default:
                            throw new JsonParseException($"Invalid escape sequence '\\{esc}'", _pos, _line, _column);
                    }
                }
                else if (char.IsControl(c))
                {
                    throw new JsonParseException("Invalid control character in string literal", _pos, _line, _column);
                }
                else
                {
                    sb.Append(c);
                    Advance();
                }
            }

            return new JsonToken(JsonTokenType.String, sb.ToString(), startPos, startLine, startCol);
        }

        private JsonToken ReadNumber(int startPos, int startLine, int startCol)
        {
            int begin = _pos;

            if (Peek() == '-') Advance();

            if (Peek() == '0')
            {
                Advance();
            }
            else if (IsDigit(Peek()))
            {
                while (IsDigit(Peek())) Advance();
            }
            else
            {
                throw new JsonParseException("Invalid number literal", startPos, startLine, startCol);
            }

            if (Peek() == '.')
            {
                Advance();
                if (!IsDigit(Peek()))
                    throw new JsonParseException("Expected a digit after the decimal point", _pos, _line, _column);
                while (IsDigit(Peek())) Advance();
            }

            if (Peek() == 'e' || Peek() == 'E')
            {
                Advance();
                if (Peek() == '+' || Peek() == '-') Advance();
                if (!IsDigit(Peek()))
                    throw new JsonParseException("Expected a digit in the exponent", _pos, _line, _column);
                while (IsDigit(Peek())) Advance();
            }

            string raw = _text.Substring(begin, _pos - begin);
            return new JsonToken(JsonTokenType.Number, raw, startPos, startLine, startCol);
        }

        private bool IsDigit(char c) => c >= '0' && c <= '9';

        private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

        private void SkipWhitespace()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == ' ' || c == '\t' || c == '\r')
                {
                    Advance();
                }
                else if (c == '\n')
                {
                    _pos++;
                    _line++;
                    _column = 1;
                }
                else
                {
                    break;
                }
            }
        }

        private void Advance()
        {
            if (_pos < _text.Length)
            {
                _pos++;
                _column++;
            }
        }
    }
}
