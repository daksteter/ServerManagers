using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ServerManagerTool.Common.Lib
{
    /// <summary>
    /// A key in a Valve KeyValues text file (VDF), such as a SteamCMD app manifest: either a value or a section of
    /// further keys. Keys keep their file order and needn't be unique; lookups ignore case, as Steam does.
    /// </summary>
    public sealed class VdfNode
    {
        private VdfNode(string key, string value, List<VdfNode> children)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value;
            Children = children;
        }

        public static VdfNode CreateValue(string key, string value) =>
            new VdfNode(key, value ?? throw new ArgumentNullException(nameof(value)), null);

        public static VdfNode CreateSection(string key, IEnumerable<VdfNode> children = null) =>
            new VdfNode(key, null, children?.ToList() ?? new List<VdfNode>());

        public string Key { get; }

        /// <summary>The value, or null for a section.</summary>
        public string Value { get; }

        /// <summary>The keys in a section, or null for a value.</summary>
        public List<VdfNode> Children { get; }

        public bool IsSection => Children != null;

        /// <summary>The first key with this name in a section, or null.</summary>
        public VdfNode this[string key] =>
            Children?.FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads and writes Valve KeyValues text files (VDF) with one root key, as SteamCMD writes its app manifests.
    /// Quoted and unquoted strings, escape sequences and // comments are read; anything else, such as a
    /// [$WIN32] condition, is refused with a <see cref="FormatException"/> rather than lost when the file is saved.
    /// </summary>
    public static class VdfFile
    {
        /// <summary>Reads a file. The encoding is taken from its byte order mark, or UTF-8 without one.</summary>
        public static VdfNode Load(string file) => Parse(File.ReadAllText(file));

        /// <summary>Writes a file as UTF-8 without a byte order mark, as SteamCMD does.</summary>
        public static void Save(VdfNode root, string file) =>
            File.WriteAllText(file, Serialize(root), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        public static VdfNode Parse(string text)
        {
            var reader = new Reader(text ?? throw new ArgumentNullException(nameof(text)));

            var token = reader.Next();
            if (token.Kind != TokenKind.String)
                throw reader.Error("expected the root key");

            var root = ReadAfterKey(reader, token.Text);

            if (reader.Next().Kind != TokenKind.End)
                throw reader.Error("expected the end of the file after the root key");

            return root;
        }

        public static string Serialize(VdfNode root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            var builder = new StringBuilder();
            Write(builder, root, 0);
            return builder.ToString();
        }

        private static VdfNode ReadAfterKey(Reader reader, string key)
        {
            var token = reader.Next();
            switch (token.Kind)
            {
                case TokenKind.String:
                    return VdfNode.CreateValue(key, token.Text);

                case TokenKind.Open:
                    var section = VdfNode.CreateSection(key);
                    while (true)
                    {
                        token = reader.Next();
                        if (token.Kind == TokenKind.Close)
                            return section;
                        if (token.Kind != TokenKind.String)
                            throw reader.Error($"expected a key or '}}' in \"{key}\"");

                        section.Children.Add(ReadAfterKey(reader, token.Text));
                    }

                default:
                    throw reader.Error($"expected a value or '{{' after \"{key}\"");
            }
        }

        private static void Write(StringBuilder builder, VdfNode node, int depth)
        {
            var indent = new string('\t', depth);
            builder.Append(indent);
            WriteString(builder, node.Key);

            if (!node.IsSection)
            {
                builder.Append("\t\t");
                WriteString(builder, node.Value);
                builder.Append('\n');
                return;
            }

            builder.Append('\n').Append(indent).Append("{\n");
            foreach (var child in node.Children)
                Write(builder, child, depth + 1);
            builder.Append(indent).Append("}\n");
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append(@"\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append(@"\n"); break;
                    case '\r': builder.Append(@"\r"); break;
                    case '\t': builder.Append(@"\t"); break;
                    default: builder.Append(c); break;
                }
            }
            builder.Append('"');
        }

        private enum TokenKind
        {
            String,
            Open,
            Close,
            End,
        }

        private readonly struct Token
        {
            public Token(TokenKind kind, string text = null)
            {
                Kind = kind;
                Text = text;
            }

            public TokenKind Kind { get; }
            public string Text { get; }
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _position;
            private int _line = 1;

            public Reader(string text)
            {
                _text = text;
            }

            public FormatException Error(string message) => new FormatException($"VDF line {_line}: {message}.");

            public Token Next()
            {
                SkipWhitespaceAndComments();
                if (_position >= _text.Length)
                    return new Token(TokenKind.End);

                var c = _text[_position];
                switch (c)
                {
                    case '{':
                        _position++;
                        return new Token(TokenKind.Open);
                    case '}':
                        _position++;
                        return new Token(TokenKind.Close);
                    case '"':
                        _position++;
                        return new Token(TokenKind.String, ReadQuoted());
                    case '[':
                        throw Error("conditions such as [$WIN32] aren't supported");
                    default:
                        return new Token(TokenKind.String, ReadUnquoted());
                }
            }

            private void SkipWhitespaceAndComments()
            {
                while (_position < _text.Length)
                {
                    var c = _text[_position];
                    if (c == '\n')
                    {
                        _line++;
                        _position++;
                    }
                    else if (char.IsWhiteSpace(c) || c == '﻿')
                    {
                        _position++;
                    }
                    else if (c == '/' && _position + 1 < _text.Length && _text[_position + 1] == '/')
                    {
                        while (_position < _text.Length && _text[_position] != '\n')
                            _position++;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            private string ReadQuoted()
            {
                var builder = new StringBuilder();
                while (_position < _text.Length)
                {
                    var c = _text[_position++];
                    if (c == '"')
                        return builder.ToString();

                    if (c == '\n')
                        _line++;

                    if (c == '\\' && _position < _text.Length)
                    {
                        var escaped = _text[_position++];
                        switch (escaped)
                        {
                            case 'n': builder.Append('\n'); break;
                            case 'r': builder.Append('\r'); break;
                            case 't': builder.Append('\t'); break;
                            default: builder.Append(escaped); break;
                        }
                        continue;
                    }

                    builder.Append(c);
                }

                throw Error("a quoted string isn't closed");
            }

            private string ReadUnquoted()
            {
                var start = _position;
                while (_position < _text.Length)
                {
                    var c = _text[_position];
                    if (char.IsWhiteSpace(c) || c == '"' || c == '{' || c == '}')
                        break;
                    _position++;
                }
                return _text.Substring(start, _position - start);
            }
        }
    }
}
