using System.Globalization;
using System.Text;

namespace Sharp.Shell.Commands.Awk;

// awk's two context-sensitive lexing problems, both decided from the token that came before.
//
// A `/` starts a regular expression where a value is expected and divides where one just ended, so
// `$1 / 2` and `split($0, a, /,/)` are told apart by asking whether the previous token could end an
// operand. And a newline terminates a statement except after the tokens that cannot end one, so
// `a &&\n b` is one expression while `a\nb` is two rules.
internal sealed class AwkLexer(string source)
{
    private readonly List<AwkToken> tokens = [];

    private int index;

    private int line = 1;

    public string? Error { get; private set; }

    public IReadOnlyList<AwkToken>? Read()
    {
        while (Error is null && index < source.Length)
        {
            ReadToken();
        }

        if (Error is not null)
        {
            return null;
        }

        tokens.Add(AwkToken.Of(AwkTokenKind.EndOfInput, string.Empty, line));
        return tokens;
    }

    private void ReadToken()
    {
        char current = source[index];

        if (current is ' ' or '\t' or '\r')
        {
            index++;
            return;
        }

        if (current == '\\' && index + 1 < source.Length && source[index + 1] == '\n')
        {
            index += 2;
            line++;
            return;
        }

        if (current == '#')
        {
            while (index < source.Length && source[index] != '\n')
            {
                index++;
            }

            return;
        }

        if (current == '\n')
        {
            ReadNewline();
            return;
        }

        if (current == '"')
        {
            ReadString();
            return;
        }

        if (current == '/' && ExpectsRegex())
        {
            ReadRegex();
            return;
        }

        if (char.IsAsciiDigit(current) || (current == '.' && index + 1 < source.Length && char.IsAsciiDigit(source[index + 1])))
        {
            ReadNumber();
            return;
        }

        if (char.IsAsciiLetter(current) || current == '_')
        {
            ReadWord();
            return;
        }

        ReadOperator();
    }

    private void ReadNewline()
    {
        index++;

        if (tokens.Count > 0 && !SwallowsNewline(tokens[^1].Kind))
        {
            tokens.Add(AwkToken.Of(AwkTokenKind.Newline, "\n", line));
        }

        line++;
    }

    private void ReadString()
    {
        int startLine = line;
        StringBuilder text = new();
        index++;

        while (index < source.Length && source[index] != '"')
        {
            if (source[index] == '\n')
            {
                Fail("newline in string");
                return;
            }

            if (source[index] == '\\')
            {
                ReadStringEscape(text);
                continue;
            }

            text.Append(source[index]);
            index++;
        }

        if (index >= source.Length)
        {
            Fail("unterminated string");
            return;
        }

        index++;
        tokens.Add(AwkToken.Of(AwkTokenKind.String, text.ToString(), startLine));
    }

    private void ReadStringEscape(StringBuilder text)
    {
        if (index + 1 >= source.Length)
        {
            Fail("unterminated string");
            return;
        }

        AwkEscapes.Append(text, source, ref index);
    }

    // The regular expression body is handed to the translator as written except for the escapes awk
    // itself defines — `\t`, `\n`, `\/`, `\101` and the rest of the string table — which are decoded
    // here, because both reference implementations match a tab against `/a\tb/`. Everything else,
    // `\.` and `\[` and `\\` among them, is a regular expression escape and is passed through.
    private void ReadRegex()
    {
        int startLine = line;
        StringBuilder pattern = new();
        index++;

        while (index < source.Length && source[index] != '/')
        {
            if (source[index] == '\n')
            {
                Fail("newline in regular expression");
                return;
            }

            if (source[index] == '\\' && index + 1 < source.Length)
            {
                AppendRegexEscape(pattern);
                continue;
            }

            pattern.Append(source[index]);
            index++;
        }

        if (index >= source.Length)
        {
            Fail("unterminated regular expression");
            return;
        }

        index++;
        tokens.Add(AwkToken.Of(AwkTokenKind.Regex, pattern.ToString(), startLine));
    }

    private void AppendRegexEscape(StringBuilder pattern)
    {
        char escaped = source[index + 1];

        if (escaped is >= '0' and <= '7')
        {
            AwkEscapes.Append(pattern, source, ref index);
            return;
        }

        if (escaped == '\\' || !AwkEscapes.IsTableEscape(escaped))
        {
            pattern.Append(source[index]).Append(escaped);
            index += 2;
            return;
        }

        pattern.Append(AwkEscapes.Character(escaped));
        index += 2;
    }

    private void ReadNumber()
    {
        int start = index;

        while (index < source.Length && char.IsAsciiDigit(source[index]))
        {
            index++;
        }

        if (index < source.Length && source[index] == '.')
        {
            index++;

            while (index < source.Length && char.IsAsciiDigit(source[index]))
            {
                index++;
            }
        }

        ReadExponent();

        string text = source[start..index];
        tokens.Add(new AwkToken(
            AwkTokenKind.Number,
            text,
            double.Parse(text, CultureInfo.InvariantCulture),
            line));
    }

    private void ReadExponent()
    {
        if (index >= source.Length || source[index] is not ('e' or 'E'))
        {
            return;
        }

        int cursor = index + 1;

        if (cursor < source.Length && source[cursor] is '+' or '-')
        {
            cursor++;
        }

        if (cursor >= source.Length || !char.IsAsciiDigit(source[cursor]))
        {
            return;
        }

        while (cursor < source.Length && char.IsAsciiDigit(source[cursor]))
        {
            cursor++;
        }

        index = cursor;
    }

    private void ReadWord()
    {
        int start = index;

        while (index < source.Length && (char.IsAsciiLetterOrDigit(source[index]) || source[index] == '_'))
        {
            index++;
        }

        string word = source[start..index];
        AwkTokenKind? keyword = KeywordKind(word);

        if (keyword is not null)
        {
            tokens.Add(AwkToken.Of(keyword.Value, word, line));
            return;
        }

        if (AwkBuiltins.IsBuiltin(word))
        {
            tokens.Add(AwkToken.Of(AwkTokenKind.Builtin, word, line));
            return;
        }

        bool callsWithoutSpace = index < source.Length && source[index] == '(';
        tokens.Add(AwkToken.Of(callsWithoutSpace ? AwkTokenKind.FunctionName : AwkTokenKind.Name, word, line));
    }

    private void ReadOperator()
    {
        foreach ((string spelling, AwkTokenKind kind) in Operators)
        {
            if (!source.AsSpan(index).StartsWith(spelling, StringComparison.Ordinal))
            {
                continue;
            }

            index += spelling.Length;
            tokens.Add(AwkToken.Of(kind, spelling, line));
            return;
        }

        Fail($"unexpected character '{source[index]}'");
    }

    // Longest spelling first: `>=` must not be read as `>` followed by `=`, and `**=` must reach the
    // parser whole so it can be named in its refusal.
    private static readonly (string Spelling, AwkTokenKind Kind)[] Operators =
    [
        ("**=", AwkTokenKind.PowerAssign),
        ("|&", AwkTokenKind.Pipe),
        ("**", AwkTokenKind.Caret),
        ("+=", AwkTokenKind.AddAssign),
        ("-=", AwkTokenKind.SubtractAssign),
        ("*=", AwkTokenKind.MultiplyAssign),
        ("/=", AwkTokenKind.DivideAssign),
        ("%=", AwkTokenKind.ModuloAssign),
        ("^=", AwkTokenKind.PowerAssign),
        ("==", AwkTokenKind.Equal),
        ("!=", AwkTokenKind.NotEqual),
        ("<=", AwkTokenKind.LessOrEqual),
        (">=", AwkTokenKind.GreaterOrEqual),
        (">>", AwkTokenKind.Append),
        ("&&", AwkTokenKind.And),
        ("||", AwkTokenKind.Or),
        ("++", AwkTokenKind.Increment),
        ("--", AwkTokenKind.Decrement),
        ("!~", AwkTokenKind.NotMatch),
        ("{", AwkTokenKind.LeftBrace),
        ("}", AwkTokenKind.RightBrace),
        ("(", AwkTokenKind.LeftParen),
        (")", AwkTokenKind.RightParen),
        ("[", AwkTokenKind.LeftBracket),
        ("]", AwkTokenKind.RightBracket),
        (";", AwkTokenKind.Semicolon),
        (",", AwkTokenKind.Comma),
        ("?", AwkTokenKind.Question),
        (":", AwkTokenKind.Colon),
        ("=", AwkTokenKind.Assign),
        ("<", AwkTokenKind.Less),
        (">", AwkTokenKind.Greater),
        ("+", AwkTokenKind.Plus),
        ("-", AwkTokenKind.Minus),
        ("*", AwkTokenKind.Star),
        ("/", AwkTokenKind.Slash),
        ("%", AwkTokenKind.Percent),
        ("^", AwkTokenKind.Caret),
        ("!", AwkTokenKind.Not),
        ("~", AwkTokenKind.Match),
        ("$", AwkTokenKind.Dollar),
        ("|", AwkTokenKind.Pipe),
    ];

    private static AwkTokenKind? KeywordKind(string word) => word switch
    {
        "BEGIN" => AwkTokenKind.Begin,
        "END" => AwkTokenKind.End,
        "function" => AwkTokenKind.Function,
        "if" => AwkTokenKind.If,
        "else" => AwkTokenKind.Else,
        "while" => AwkTokenKind.While,
        "for" => AwkTokenKind.For,
        "do" => AwkTokenKind.Do,
        "break" => AwkTokenKind.Break,
        "continue" => AwkTokenKind.Continue,
        "next" => AwkTokenKind.Next,
        "nextfile" => AwkTokenKind.NextFile,
        "exit" => AwkTokenKind.Exit,
        "return" => AwkTokenKind.Return,
        "delete" => AwkTokenKind.Delete,
        "in" => AwkTokenKind.In,
        "print" => AwkTokenKind.Print,
        "printf" => AwkTokenKind.Printf,
        "getline" => AwkTokenKind.Getline,
        _ => null,
    };

    private bool ExpectsRegex() => tokens.Count == 0 || !EndsAValue(tokens[^1].Kind);

    private static bool EndsAValue(AwkTokenKind kind) => kind
        is AwkTokenKind.Number
        or AwkTokenKind.String
        or AwkTokenKind.Regex
        or AwkTokenKind.Name
        or AwkTokenKind.Builtin
        or AwkTokenKind.RightParen
        or AwkTokenKind.RightBracket
        or AwkTokenKind.Increment
        or AwkTokenKind.Decrement;

    private static bool SwallowsNewline(AwkTokenKind kind) => kind
        is AwkTokenKind.LeftBrace
        or AwkTokenKind.And
        or AwkTokenKind.Or
        or AwkTokenKind.Comma
        or AwkTokenKind.Do
        or AwkTokenKind.Else
        or AwkTokenKind.Semicolon
        or AwkTokenKind.Newline
        or AwkTokenKind.Question
        or AwkTokenKind.Colon;

    private void Fail(string reason) => Error ??= $"syntax error at source line {line}: {reason}";
}
