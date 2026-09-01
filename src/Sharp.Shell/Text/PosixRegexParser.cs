using System.Text;

namespace Sharp.Shell.Text;

// Reads a POSIX basic or extended regular expression. Every construct whose meaning differs from
// .NET is decided here, at the position where POSIX decides it: `*` is a repeat except at the start
// of a branch, `^` anchors only at the start and `$` only at the end, and in BRE the parentheses
// and braces that group in .NET are ordinary characters.
//
// Anything it cannot translate faithfully sets Refusal. It never throws and never guesses.
internal sealed class PosixRegexParser(string pattern, RegexDialect dialect, bool allowsGnuExtensions, bool isMultiline)
{
    private int index;

    public string? Refusal { get; private set; }

    public RegexNode? Parse()
    {
        RegexNode parsed = ParseAlternation();

        if (Refusal is not null)
        {
            return null;
        }

        if (index < pattern.Length)
        {
            Refuse($"unbalanced '{pattern[index]}'");
            return null;
        }

        return parsed;
    }

    private RegexNode ParseAlternation()
    {
        List<RegexNode> branches = [ParseSequence()];

        while (Refusal is null && TryConsumeAlternationBar())
        {
            branches.Add(ParseSequence());
        }

        return branches.Count == 1 ? branches[0] : new RegexAlternation(branches);
    }

    private RegexNode ParseSequence()
    {
        List<RegexNode> items = [];

        while (Refusal is null && index < pattern.Length && !IsAtGroupEnd() && !IsAtAlternationBar())
        {
            RegexNode? atom = ParseAtom(items.Count == 0);

            if (atom is null)
            {
                break;
            }

            items.Add(ParseQuantifiers(atom));
        }

        return items.Count == 1 ? items[0] : new RegexSequence(items);
    }

    private RegexNode? ParseAtom(bool isAtBranchStart)
    {
        char current = pattern[index];

        if (current == '.')
        {
            index++;
            return new RegexAtom(isMultiline ? @"[^\n]" : @"[\s\S]", IsZeroWidth: false);
        }

        if (current == '[')
        {
            return ParseBracketExpression();
        }

        if (current == '\\')
        {
            return ParseEscape(isAtBranchStart);
        }

        if (current == '^')
        {
            index++;
            return IsExtended || isAtBranchStart ? StartAnchor() : new RegexLiteral('^');
        }

        if (current == '$')
        {
            return ParseDollar();
        }

        if (IsExtended)
        {
            return ParseExtendedAtom(current, isAtBranchStart);
        }

        if (current == '*' && isAtBranchStart)
        {
            index++;
            return new RegexLiteral('*');
        }

        index++;
        return new RegexLiteral(current);
    }

    private RegexNode? ParseExtendedAtom(char current, bool isAtBranchStart)
    {
        if (current == '(')
        {
            return ParseGroup(openingLength: 1);
        }

        if (current is '*' or '+' or '?' && isAtBranchStart)
        {
            Refuse($"'{current}' with nothing to repeat");
            return null;
        }

        index++;
        return new RegexLiteral(current);
    }

    private RegexNode ParseDollar()
    {
        bool isLastCharacter = index + 1 == pattern.Length;
        bool precedesBranchEnd = IsEscapedAt(index + 1, ')') || IsEscapedAt(index + 1, '|');
        index++;

        return IsExtended || isLastCharacter || precedesBranchEnd ? EndAnchor() : new RegexLiteral('$');
    }

    private RegexNode? ParseGroup(int openingLength)
    {
        index += openingLength;
        RegexNode body = ParseAlternation();

        if (Refusal is not null)
        {
            return null;
        }

        if (!IsAtGroupEnd())
        {
            Refuse("unterminated group");
            return null;
        }

        index += IsExtended ? 1 : 2;
        return new RegexGroup(body);
    }

    private RegexNode ParseQuantifiers(RegexNode atom)
    {
        while (Refusal is null && index < pattern.Length)
        {
            RegexNode? repeated = TryParseQuantifier(atom);

            if (repeated is null)
            {
                break;
            }

            atom = repeated;
        }

        return atom;
    }

    private RegexNode? TryParseQuantifier(RegexNode atom)
    {
        if (pattern[index] == '*')
        {
            index++;
            return new RegexRepeat(atom, 0, RegexRepeat.Unbounded);
        }

        if (IsExtended)
        {
            return TryParseExtendedQuantifier(atom);
        }

        if (IsEscapedAt(index, '+'))
        {
            return GnuQuantifier(atom, @"\+", 1, RegexRepeat.Unbounded);
        }

        if (IsEscapedAt(index, '?'))
        {
            return GnuQuantifier(atom, @"\?", 0, 1);
        }

        return IsEscapedAt(index, '{') ? ParseInterval(atom, braceLength: 2) : null;
    }

    private RegexNode? TryParseExtendedQuantifier(RegexNode atom)
    {
        switch (pattern[index])
        {
            case '+':
                index++;
                return new RegexRepeat(atom, 1, RegexRepeat.Unbounded);
            case '?':
                index++;
                return new RegexRepeat(atom, 0, 1);
            case '{':
                return ParseInterval(atom, braceLength: 1);
            default:
                return null;
        }
    }

    private RegexNode? GnuQuantifier(RegexNode atom, string spelling, int minimum, int maximum)
    {
        if (!allowsGnuExtensions)
        {
            Refuse($"'{spelling}' is a GNU extension");
            return null;
        }

        index += 2;
        return new RegexRepeat(atom, minimum, maximum);
    }

    private RegexNode? ParseInterval(RegexNode atom, int braceLength)
    {
        int cursor = index + braceLength;
        int minimum = ReadNumber(ref cursor);

        if (minimum < 0)
        {
            return NotAnInterval(braceLength);
        }

        int maximum = minimum;

        if (cursor < pattern.Length && pattern[cursor] == ',')
        {
            cursor++;
            maximum = ReadNumber(ref cursor);
        }

        bool isClosed = braceLength == 1
            ? cursor < pattern.Length && pattern[cursor] == '}'
            : IsEscapedAt(cursor, '}');

        if (!isClosed)
        {
            return NotAnInterval(braceLength);
        }

        index = cursor + braceLength;
        return new RegexRepeat(atom, minimum, maximum);
    }

    // In ERE a brace that does not open an interval is an ordinary character, so the caller reads it
    // as a literal on the next pass. In BRE `\{` can only ever start one, so it is an error.
    private RegexNode? NotAnInterval(int braceLength)
    {
        if (braceLength == 2)
        {
            Refuse("unterminated interval");
        }

        return null;
    }

    private int ReadNumber(ref int cursor)
    {
        int start = cursor;

        while (cursor < pattern.Length && char.IsAsciiDigit(pattern[cursor]))
        {
            cursor++;
        }

        return cursor == start ? RegexRepeat.Unbounded : int.Parse(pattern[start..cursor]);
    }

    private RegexNode? ParseEscape(bool isAtBranchStart)
    {
        if (index + 1 >= pattern.Length)
        {
            Refuse("trailing backslash");
            return null;
        }

        char escaped = pattern[index + 1];

        if (!IsExtended && escaped == '(')
        {
            return ParseGroup(openingLength: 2);
        }

        if (!IsExtended && escaped is '+' or '?' or '{' && isAtBranchStart)
        {
            Refuse($@"'\{escaped}' with nothing to repeat");
            return null;
        }

        if (escaped is >= '1' and <= '9')
        {
            index += 2;
            return new RegexBackreference(escaped - '0');
        }

        if (escaped == 'n')
        {
            index += 2;
            return new RegexLiteral('\n');
        }

        RegexNode? gnu = ParseGnuEscape(escaped);

        if (gnu is not null || Refusal is not null)
        {
            return gnu;
        }

        if (char.IsAsciiLetterOrDigit(escaped))
        {
            Refuse($@"undefined escape '\{escaped}'");
            return null;
        }

        index += 2;
        return new RegexLiteral(escaped);
    }

    private RegexNode? ParseGnuEscape(char escaped)
    {
        if (!IsGnuEscape(escaped))
        {
            return null;
        }

        if (!allowsGnuExtensions)
        {
            Refuse($@"'\{escaped}' is a GNU extension");
            return null;
        }

        return escaped switch
        {
            't' => ConsumeLiteral('\t'),
            'r' => ConsumeLiteral('\r'),
            'f' => ConsumeLiteral('\f'),
            'v' => ConsumeLiteral('\v'),
            'a' => ConsumeLiteral('\a'),
            'c' => ParseControlEscape(),
            'd' => ParseNumericEscape(radix: 10, maximumDigits: 3),
            'o' => ParseNumericEscape(radix: 8, maximumDigits: 3),
            'x' => ParseNumericEscape(radix: 16, maximumDigits: 2),
            'w' => ConsumeAtom($"[{PosixCharacterClasses.Word}]"),
            'W' => ConsumeAtom(NegatedClass(PosixCharacterClasses.Word)),
            's' => ConsumeAtom($"[{PosixCharacterClasses.Space}]"),
            'S' => ConsumeAtom(NegatedClass(PosixCharacterClasses.Space)),
            'b' => ConsumeAssertion($"(?:{WordStart}|{WordEnd})"),
            'B' => ConsumeAssertion($"(?:(?<=[{PosixCharacterClasses.Word}])(?=[{PosixCharacterClasses.Word}])|(?<![{PosixCharacterClasses.Word}])(?![{PosixCharacterClasses.Word}]))"),
            '<' => ConsumeAssertion(WordStart),
            '>' => ConsumeAssertion(WordEnd),
            '`' => ConsumeAssertion(@"\A"),
            '\'' => ConsumeAssertion(@"\z"),
            _ => null,
        };
    }

    private static bool IsGnuEscape(char escaped) =>
        escaped is 't' or 'r' or 'f' or 'v' or 'a' or 'c' or 'd' or 'o' or 'x'
            or 'w' or 'W' or 's' or 'S' or 'b' or 'B' or '<' or '>' or '`' or '\'';

    private static string WordStart => $"(?<![{PosixCharacterClasses.Word}])(?=[{PosixCharacterClasses.Word}])";

    private static string WordEnd => $"(?<=[{PosixCharacterClasses.Word}])(?![{PosixCharacterClasses.Word}])";

    private RegexNode? ParseControlEscape()
    {
        if (index + 2 >= pattern.Length)
        {
            Refuse(@"'\c' without a control character");
            return null;
        }

        char control = (char)(char.ToUpperInvariant(pattern[index + 2]) ^ 0x40);
        index += 3;
        return new RegexLiteral(control);
    }

    private RegexNode? ParseNumericEscape(int radix, int maximumDigits)
    {
        int cursor = index + 2;
        int value = 0;
        int digits = 0;

        while (cursor < pattern.Length && digits < maximumDigits && TryReadDigit(pattern[cursor], radix, out int digit))
        {
            value = (value * radix) + digit;
            cursor++;
            digits++;
        }

        if (digits == 0)
        {
            Refuse($@"'\{pattern[index + 1]}' without digits");
            return null;
        }

        index = cursor;
        return new RegexLiteral((char)value);
    }

    private static bool TryReadDigit(char character, int radix, out int digit)
    {
        digit = char.IsAsciiDigit(character) ? character - '0'
            : char.IsAsciiHexDigit(character) ? char.ToLowerInvariant(character) - 'a' + 10
            : -1;

        return digit >= 0 && digit < radix;
    }

    private RegexNode? ParseBracketExpression()
    {
        int cursor = index + 1;
        bool isNegated = cursor < pattern.Length && pattern[cursor] == '^';

        if (isNegated)
        {
            cursor++;
        }

        StringBuilder members = new();
        bool isFirstMember = true;

        while (true)
        {
            if (cursor >= pattern.Length)
            {
                Refuse("unterminated bracket expression");
                return null;
            }

            if (pattern[cursor] == ']' && !isFirstMember)
            {
                cursor++;
                break;
            }

            isFirstMember = false;

            if (!TryReadBracketMember(ref cursor, members))
            {
                return null;
            }
        }

        index = cursor;
        string body = members.ToString() + (isNegated && isMultiline ? @"\n" : string.Empty);
        return new RegexAtom($"[{(isNegated ? "^" : string.Empty)}{body}]", IsZeroWidth: false);
    }

    private bool TryReadBracketMember(ref int cursor, StringBuilder members)
    {
        if (pattern[cursor] == '[' && cursor + 1 < pattern.Length && pattern[cursor + 1] is ':' or '.' or '=')
        {
            return TryReadBracketConstruct(ref cursor, members);
        }

        char member = ReadBracketCharacter(ref cursor);

        if (cursor + 1 < pattern.Length && pattern[cursor] == '-' && pattern[cursor + 1] != ']')
        {
            cursor++;
            char upper = ReadBracketCharacter(ref cursor);
            members.Append(EscapeInClass(member)).Append('-').Append(EscapeInClass(upper));
            return true;
        }

        members.Append(EscapeInClass(member));
        return true;
    }

    private bool TryReadBracketConstruct(ref int cursor, StringBuilder members)
    {
        char kind = pattern[cursor + 1];

        if (kind == '.')
        {
            Refuse("collating symbols ('[. .]') are not supported");
            return false;
        }

        if (kind == '=')
        {
            Refuse("equivalence classes ('[= =]') are not supported");
            return false;
        }

        int close = pattern.IndexOf(":]", cursor + 2, StringComparison.Ordinal);

        if (close < 0)
        {
            Refuse("unterminated character class");
            return false;
        }

        string name = pattern[(cursor + 2)..close];
        string? translated = PosixCharacterClasses.Translate(name);

        if (translated is null)
        {
            Refuse($"unknown character class '[:{name}:]'");
            return false;
        }

        members.Append(translated);
        cursor = close + 2;
        return true;
    }

    // POSIX says a backslash is an ordinary character inside brackets; GNU recognises its escapes
    // there, which is what real scripts rely on.
    private char ReadBracketCharacter(ref int cursor)
    {
        char member = pattern[cursor];
        cursor++;

        if (member != '\\' || !allowsGnuExtensions || cursor >= pattern.Length)
        {
            return member;
        }

        char escaped = pattern[cursor];
        char? unescaped = escaped switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            'f' => '\f',
            'v' => '\v',
            'a' => '\a',
            '\\' => '\\',
            ']' => ']',
            _ => null,
        };

        if (unescaped is null)
        {
            return member;
        }

        cursor++;
        return unescaped.Value;
    }

    private static string NegatedClass(string body) => $"[^{body}]";

    private static string EscapeInClass(char character) => character switch
    {
        '\\' or ']' or '^' or '-' or '[' => $@"\{character}",
        '\n' => @"\n",
        '\r' => @"\r",
        '\t' => @"\t",
        '\f' => @"\f",
        '\v' => @"\v",
        _ => character < ' ' || character == (char)0x7f ? $@"\x{(int)character:x2}" : character.ToString(),
    };

    private RegexNode ConsumeLiteral(char character)
    {
        index += 2;
        return new RegexLiteral(character);
    }

    private RegexNode ConsumeAtom(string emission)
    {
        index += 2;
        return new RegexAtom(emission, IsZeroWidth: false);
    }

    private RegexNode ConsumeAssertion(string emission)
    {
        index += 2;
        return new RegexAtom(emission, IsZeroWidth: true);
    }

    private RegexNode StartAnchor() => new RegexAtom(isMultiline ? "^" : @"\A", IsZeroWidth: true);

    private RegexNode EndAnchor() => new RegexAtom(isMultiline ? "$" : @"\z", IsZeroWidth: true);

    private bool IsExtended => dialect == RegexDialect.ExtendedPosix;

    private bool IsAtGroupEnd() => IsExtended ? pattern[index] == ')' : IsEscapedAt(index, ')');

    private bool IsAtAlternationBar()
    {
        if (IsExtended)
        {
            return pattern[index] == '|';
        }

        if (!IsEscapedAt(index, '|'))
        {
            return false;
        }

        if (!allowsGnuExtensions)
        {
            Refuse(@"'\|' is a GNU extension");
        }

        return true;
    }

    private bool TryConsumeAlternationBar()
    {
        if (index >= pattern.Length || !IsAtAlternationBar() || Refusal is not null)
        {
            return false;
        }

        index += IsExtended ? 1 : 2;
        return true;
    }

    private bool IsEscapedAt(int at, char character) =>
        at + 1 < pattern.Length && pattern[at] == '\\' && pattern[at + 1] == character;

    private void Refuse(string reason) => Refusal ??= reason;
}
