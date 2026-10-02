using System.Text;

namespace AimOdometer.Core.Games;

/// <summary>
/// A node of Valve's text KeyValues format (used by Steam's .vdf and .acf files).
/// A node has either a string value or children. Key lookups are case-insensitive, like Steam's own parser.
/// </summary>
public sealed class KeyValues
{
    private readonly List<KeyValues> _children = [];

    private KeyValues(string key, string? value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    /// <summary>String value, or null when this node is a section with children.</summary>
    public string? Value { get; }

    public IReadOnlyList<KeyValues> Children => _children;

    /// <summary>First child with the given key (case-insensitive), or null.</summary>
    public KeyValues? this[string key] =>
        _children.Find(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Value of the first child with the given key, or null.</summary>
    public string? GetString(string key) => this[key]?.Value;

    /// <summary>Parses a document. Returns the single root section (e.g. "AppState").</summary>
    /// <exception cref="FormatException">The text is not valid KeyValues.</exception>
    public static KeyValues Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text);
        var root = new KeyValues(string.Empty, null);
        reader.ReadSection(root, topLevel: true);
        return root.Children.Count switch
        {
            1 => root.Children[0],
            0 => throw new FormatException("Empty KeyValues document."),
            _ => root,
        };
    }

    private sealed class Reader(string text)
    {
        private readonly string _text = text;
        private int _position;

        public void ReadSection(KeyValues parent, bool topLevel)
        {
            while (true)
            {
                var token = NextToken(out var kind);
                if (kind == TokenKind.End)
                {
                    if (!topLevel)
                    {
                        throw Error("Unexpected end of file inside a section");
                    }

                    return;
                }

                if (kind == TokenKind.Close)
                {
                    if (topLevel)
                    {
                        throw Error("Unexpected '}'");
                    }

                    return;
                }

                if (kind != TokenKind.String)
                {
                    throw Error("Expected a key");
                }

                var value = NextToken(out var valueKind);
                SkipConditional();
                switch (valueKind)
                {
                    case TokenKind.String:
                        parent._children.Add(new KeyValues(token, value));
                        break;
                    case TokenKind.Open:
                        var child = new KeyValues(token, null);
                        parent._children.Add(child);
                        ReadSection(child, topLevel: false);
                        break;
                    default:
                        throw Error($"Expected a value or '{{' after key '{token}'");
                }
            }
        }

        private enum TokenKind
        {
            String,
            Open,
            Close,
            End,
        }

        private string NextToken(out TokenKind kind)
        {
            SkipWhitespaceAndComments();
            if (_position >= _text.Length)
            {
                kind = TokenKind.End;
                return string.Empty;
            }

            var c = _text[_position];
            switch (c)
            {
                case '{':
                    _position++;
                    kind = TokenKind.Open;
                    return "{";
                case '}':
                    _position++;
                    kind = TokenKind.Close;
                    return "}";
                case '"':
                    kind = TokenKind.String;
                    return ReadQuoted();
                default:
                    kind = TokenKind.String;
                    return ReadUnquoted();
            }
        }

        private string ReadQuoted()
        {
            _position++; // opening quote
            var builder = new StringBuilder();
            while (_position < _text.Length)
            {
                var c = _text[_position++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c == '\\' && _position < _text.Length)
                {
                    var escaped = _text[_position++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => escaped, // \\ and \" (and anything else) map to the character itself
                    });
                }
                else
                {
                    builder.Append(c);
                }
            }

            throw Error("Unterminated string");
        }

        private string ReadUnquoted()
        {
            var start = _position;
            while (_position < _text.Length && !char.IsWhiteSpace(_text[_position]) && _text[_position] is not ('{' or '}' or '"'))
            {
                _position++;
            }

            return _text[start.._position];
        }

        /// <summary>Skips platform conditionals such as [$WIN32] that may follow a value.</summary>
        private void SkipConditional()
        {
            var save = _position;
            SkipInlineWhitespace();
            if (_position < _text.Length && _text[_position] == '[')
            {
                var end = _text.IndexOf(']', _position);
                _position = end < 0 ? _text.Length : end + 1;
            }
            else
            {
                _position = save;
            }
        }

        private void SkipInlineWhitespace()
        {
            while (_position < _text.Length && _text[_position] is ' ' or '\t')
            {
                _position++;
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]))
                {
                    _position++;
                }
                else if (_text[_position] == '/' && _position + 1 < _text.Length && _text[_position + 1] == '/')
                {
                    var end = _text.IndexOf('\n', _position);
                    _position = end < 0 ? _text.Length : end + 1;
                }
                else
                {
                    return;
                }
            }
        }

        private FormatException Error(string message)
        {
            var line = 1;
            for (var i = 0; i < Math.Min(_position, _text.Length); i++)
            {
                if (_text[i] == '\n')
                {
                    line++;
                }
            }

            return new FormatException($"{message} (line {line}).");
        }
    }
}
