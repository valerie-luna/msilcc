using System.Diagnostics;
using System.Text;
namespace Msilcc;

[DebuggerDisplay("Value = {Location.Span} Kind = {Kind}")]
public readonly struct Token(TokenKind kind, Location loc) : IEquatable<Token>
{
    public TokenKind Kind { get; init; } = kind;
    public long NumericValue { get; init; }
    public Location Location { get; init; } = loc;

    public string Identifier
    {
        get
        {
            //Debug.Assert(Kind is TokenKind.Identifier);
            return Location.Span.ToString();
        }
    }

    public byte[] StringLiteral
    {
        get
        {
            Debug.Assert(Kind is TokenKind.String);
            List<byte> bytes = new();
            var span = Location.Span;
            for (int i = 0; i < span.Length; i++)
            {
                char c = span[i];
                if (!char.IsAscii(c))
                    throw new InvalidOperationException();
                if (c is '\\')
                {
                    c = span[++i];
                    // checked is just for safety
                    bytes.Add(checked((byte)(c switch
                    {
                        >= '0' and <= '7' => ParseOctal(span, ref i),
                        'x' => ParseHex(span, ref i),
                        'a' => '\a',
                        'b' => '\b',
                        't' => '\t',
                        'n' => '\n',
                        'v' => '\v',
                        'f' => '\f',
                        'r' => '\r',
                        // [GNU] \e for the ASCII escape character is a GNU C extension.
                        'e' => 27,
                        _ => c
                    })));
                    static byte ParseOctal(ReadOnlySpan<char> span, ref int i)
                    {
                        char c = span[i++];
                        int ret = c - '0';
                        if (i < span.Length && IsOctalChar(c = span[i]))
                        {
                            i++;
                            ret = (ret << 3) + (c - '0');
                            if (i < span.Length && IsOctalChar(c = span[i]))
                            {
                                i++;
                                ret = (ret << 3) + (c - '0');
                            }
                        }
                        return checked((byte)ret);
                    }
                    static bool IsOctalChar(char c) => c >= '0' && c <= '7';
                    static byte ParseHex(ReadOnlySpan<char> span, ref int i)
                    {
                        i++;
                        char c;
                        int ret = 0;
                        while (i < span.Length && char.IsAsciiHexDigit(c = span[i++]))
                        {
                            ret = (ret << 4) + FromHexChar(c);
                        }
                        return checked((byte)ret);
                    }
                    static int FromHexChar(char c)
                    {
                        if ('0' <= c && c <= '9')
                            return c - '0';
                        if ('a' <= c && c <= 'f')
                            return c - 'a' + 10;
                        return c - 'A' + 10;
                    }
                }
                else
                {
                    bytes.Add(checked((byte)c));
                }
            }
            bytes.Add(0);
            return [.. bytes];
        }
    }

    public bool EqualTo(string str) => Location.EqualTo(str);

    public static IEnumerable<Token> Tokenize(string Filename, TextReader reader)
    {
        // there has to be a better way but whatever
        ReadOnlyMemory<char> str = new([.. reader.ReadToEnd()]);
        return Tokenize(Filename, str);
    }

    public static IEnumerable<Token> Tokenize(string Filename, ReadOnlyMemory<char> str)
    {
        int i = 0;
        while (i < str.Span.Length)
        {
            if (i + 2 <= str.Span.Length && str.Span[i..(i+2)] is "//")
            {
                i += 2;
                while (str.Span[i] != '\n' && i < str.Span.Length)
                {
                    i++;
                }
                continue;
            }

            if (i + 2 <= str.Span.Length && str.Span[i..(i+2)] is "/*")
            {
                int start = i;
                i += 2;
                while (i + 2 <= str.Span.Length && str.Span[i..(i+2)] is not "*/")
                {
                    i++;
                }
                if (i >= str.Span.Length)
                {
                    Utilities.ErrorAt(new(Filename, str, start..), "unclosed block comment");
                }
                i += 2;
                continue;
            }

            if (char.IsWhiteSpace(str.Span[i]))
            {
                i++;
                continue;
            }

            if (char.IsDigit(str.Span[i]))
            {
                var digit = GetRange(str.Span, i, char.IsDigit);
                long val = long.Parse(str.Span[digit]);

                yield return new(TokenKind.Number, new(Filename, str, digit))
                {
                    NumericValue = val
                };
                
                Debug.Assert(!digit.Start.IsFromEnd && !digit.End.IsFromEnd);
                i += digit.End.Value - digit.Start.Value;
                continue;
            }

            if (i + 2 <= str.Span.Length && str.Span[i..(i+2)] is "==" or "!=" or "<=" or ">=" or "->")
            {
                yield return new (TokenKind.Reserved, new(Filename, str, i..(i+2)));
                i += 2;
                continue;
            }

            if (IsReservedCharacter(str.Span[i]))
            {
                yield return new(TokenKind.Reserved, new(Filename, str, i..(i + 1)));
                i++;
                continue;
            }

            if (str.Span[i] == '"')
            {
                i++;
                int strstart = i, strend;
                while (str.Span[i] != '"' && i < str.Span.Length)
                {
                    if (str.Span[i] == '\0')
                        Utilities.ErrorAt(new (Filename, str, strstart..), "null byte in string literal");
                    if (str.Span[i] == '\\')
                        i++;
                    i++;
                }
                if (i >= str.Span.Length)
                    Utilities.ErrorAt(new (Filename, str, strstart..), "unclosed string literal");
                strend = i++; // i should now be past the ", while strend should be on it
                yield return new (TokenKind.String, new(Filename, str, strstart..strend));
                continue;
            }

            if (IsValidFirstIdentifier(str.Span[i]))
            {
                int start = i;
                while (IsValidIdentifier(str.Span[i]))
                    i++;
                var loc = new Location(Filename, str, start..i);
                TokenKind kind = TokenKind.Identifier;
                if (IsKeyword(loc))
                {
                    kind = TokenKind.Reserved;
                }
                yield return new (kind, loc);
                continue;
            }

            Utilities.ErrorAt(new(Filename, str, i..(i + 1)), "Invalid token");
        }
        yield return new Token(TokenKind.EOF, new(Filename, str, i..));

        static Range GetRange(ReadOnlySpan<char> mem, int startIndex, Func<char, bool> determinator)
        {
            int endindex = startIndex;
            while (++endindex < mem.Length && determinator(mem[endindex]))
                ;
            return startIndex..endindex;
        }
    }

    private static bool IsValidFirstIdentifier(char c) => char.IsAsciiLetter(c) || c == '_';
    private static bool IsValidIdentifier(char c) => IsValidFirstIdentifier(c) || char.IsAsciiDigit(c);

    private static bool IsKeyword(Location loc)
    {
        return loc.EqualTo("return")
            || loc.EqualTo("if")
            || loc.EqualTo("else")
            || loc.EqualTo("for")
            || loc.EqualTo("while")
            || loc.EqualTo("int")
            || loc.EqualTo("sizeof")
            || loc.EqualTo("char")
            || loc.EqualTo("struct")
            || loc.EqualTo("union")
            || loc.EqualTo("short")
            || loc.EqualTo("long")
            || loc.EqualTo("void")
            || loc.EqualTo("typedef");
    }

    private static bool IsReservedCharacter(char c) => c is 
        '+' or '-' or '/' or '*' or 
        '(' or ')' or '{' or '}' or 
        '>' or '<' or '[' or ']' or
        ';' or 
        '=' or 
        '&' or '*' or ',' or '.';

    public bool Equals(Token other)
    {
        return Kind == other.Kind && NumericValue == other.NumericValue && Location == other.Location;
    }

    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }

    public override bool Equals(object? obj)
    {
        return obj is Token token && Equals(token);
    }

    public static bool operator ==(Token left, Token right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Token left, Token right)
    {
        return !(left == right);
    }
}