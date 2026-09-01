using System.Text;

namespace Sharp.Shell.Lexing;

// Turns shell source into words and operators. Quoting structure is preserved in the word parts
// rather than resolved here, because expansion later needs to know which text was quoted.
public static class Lexer
{
    public static LexResult Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new LexScanner(source).Run();
    }

    // The parts of a run of text that expands but is not word-split: a here-document body. Errors
    // fall back to a single literal part, because a body is data, not a command.
    public static IReadOnlyList<WordPart> ExpandableParts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new LexScanner(text).RunExpandableText();
    }
}

internal sealed class LexScanner(string source)
{
    private readonly List<Token> tokens = [];
    private readonly List<WordPart> pendingParts = [];
    private readonly StringBuilder pendingLiteral = new();

    private readonly List<PendingHereDocument> pendingHereDocuments = [];

    private int index;
    private int wordStart;
    private string? error;
    private int awaitingDelimiterFor = -1;
    private bool awaitingStripsTabs;

    public LexResult Run()
    {
        while (error is null && index < source.Length)
        {
            ScanNext();
        }

        if (error is not null)
        {
            return LexResult.Failed(error);
        }

        FlushWord();
        CaptureHereDocuments();
        tokens.Add(new Token(TokenKind.EndOfInput, string.Empty, null, index));
        return new LexResult(tokens, null);
    }

    public IReadOnlyList<WordPart> RunExpandableText()
    {
        List<WordPart> parts = ReadExpandableRun(null);
        return error is null ? parts : [new WordPart(WordPartKind.Literal, source)];
    }

    private void ScanNext()
    {
        char current = source[index];

        if (current is ' ' or '\t')
        {
            FlushWord();
            index++;
            return;
        }

        if (current == '\n')
        {
            FlushWord();
            tokens.Add(new Token(TokenKind.Newline, "\n", null, index));
            index++;
            CaptureHereDocuments();
            return;
        }

        if (current == '#' && !HasPendingWord())
        {
            SkipComment();
            return;
        }

        if (IsOperatorStart(current))
        {
            ScanOperator();
            return;
        }

        ScanWordCharacter();
    }

    private void SkipComment()
    {
        while (index < source.Length && source[index] != '\n')
        {
            index++;
        }
    }

    private static bool IsOperatorStart(char character) => character is '|' or '&' or ';' or '<' or '>' or '(' or ')';

    private void ScanOperator()
    {
        int start = index;
        string prefix = TakeFileDescriptorPrefix();
        string text = ReadOperatorText();

        FlushWord();
        tokens.Add(new Token(TokenKind.Operator, prefix + text, null, start));

        if (text is "<<" or "<<-")
        {
            awaitingDelimiterFor = tokens.Count - 1;
            awaitingStripsTabs = text == "<<-";
        }
    }

    // "2>" binds the digits to the operator; "head -n 2 > out" does not, because the 2 was already
    // flushed by the space before the operator was reached.
    private string TakeFileDescriptorPrefix()
    {
        if (source[index] is not ('<' or '>'))
        {
            return string.Empty;
        }

        if (pendingParts.Count > 0 || pendingLiteral.Length == 0)
        {
            return string.Empty;
        }

        string pending = pendingLiteral.ToString();
        if (!pending.All(char.IsAsciiDigit))
        {
            return string.Empty;
        }

        pendingLiteral.Clear();
        return pending;
    }

    private string ReadOperatorText()
    {
        char current = source[index];
        char next = index + 1 < source.Length ? source[index + 1] : '\0';

        switch (current)
        {
            case '|':
                return Take(next == '|' ? "||" : "|");
            case '&':
                return Take(next switch { '&' => "&&", '>' => "&>", _ => "&" });
            case ';':
                return Take(next == ';' ? ";;" : ";");
            case '(':
            case ')':
                return Take(current.ToString());
            case '<':
                if (next == '(')
                {
                    return Take("<(");
                }

                if (next == '&')
                {
                    return Take("<&");
                }

                if (next == '<')
                {
                    bool stripsTabs = index + 2 < source.Length && source[index + 2] == '-';
                    return Take(stripsTabs ? "<<-" : "<<");
                }

                return Take("<");
            default:
                if (next == '(')
                {
                    return Take(">(");
                }

                if (next == '&')
                {
                    return Take(">&");
                }

                return Take(next == '>' ? ">>" : ">");
        }
    }

    private string Take(string text)
    {
        index += text.Length;
        return text;
    }

    private void ScanWordCharacter()
    {
        if (!HasPendingWord())
        {
            wordStart = index;
        }

        char current = source[index];

        switch (current)
        {
            case '\\':
                ScanBackslash();
                return;
            case '\'':
                ScanSingleQuoted();
                return;
            case '"':
                ScanDoubleQuoted();
                return;
            case '`':
                ScanBackticks();
                return;
            case '$':
                ScanDollar();
                return;
            case '~' when !HasPendingWord():
                index++;
                AddPart(new WordPart(WordPartKind.Tilde, string.Empty));
                return;
            default:
                pendingLiteral.Append(current);
                index++;
                return;
        }
    }

    private void ScanBackslash()
    {
        if (index + 1 >= source.Length)
        {
            pendingLiteral.Append('\\');
            index++;
            return;
        }

        char escaped = source[index + 1];
        index += 2;

        if (escaped != '\n')
        {
            pendingLiteral.Append(escaped);
        }
    }

    private void ScanSingleQuoted()
    {
        int closing = source.IndexOf('\'', index + 1);
        if (closing < 0)
        {
            error = "unterminated single quote";
            return;
        }

        AddPart(new WordPart(WordPartKind.SingleQuoted, source[(index + 1)..closing]));
        index = closing + 1;
    }

    private void ScanDoubleQuoted()
    {
        index++;
        List<WordPart> nested = ReadExpandableRun('"');

        if (error is not null)
        {
            return;
        }

        index++;
        AddPart(new WordPart(WordPartKind.DoubleQuoted, string.Empty, nested));
    }

    // The shared body of a double-quoted word and a here-document: literal text with $ and `
    // expansions, no field splitting, no globbing. A null terminator runs to end of input.
    private List<WordPart> ReadExpandableRun(char? terminator)
    {
        List<WordPart> nested = [];
        StringBuilder literal = new();

        while (index < source.Length && source[index] != terminator)
        {
            if (source[index] == '\\' && index + 1 < source.Length && IsDoubleQuoteEscapable(source[index + 1]))
            {
                if (source[index + 1] != '\n')
                {
                    literal.Append(source[index + 1]);
                }

                index += 2;
                continue;
            }

            if (source[index] is '$' or '`')
            {
                FlushLiteralInto(nested, literal);
                WordPart? part = source[index] == '$' ? ReadDollar() : ReadBackticks();
                if (part is null)
                {
                    return nested;
                }

                nested.Add(part);
                continue;
            }

            literal.Append(source[index]);
            index++;
        }

        if (terminator is not null && index >= source.Length)
        {
            error = "unterminated double quote";
            return nested;
        }

        FlushLiteralInto(nested, literal);
        return nested;
    }

    private static bool IsDoubleQuoteEscapable(char character) => character is '$' or '`' or '"' or '\\' or '\n';

    private static void FlushLiteralInto(List<WordPart> parts, StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        parts.Add(new WordPart(WordPartKind.Literal, literal.ToString()));
        literal.Clear();
    }

    private void ScanBackticks()
    {
        WordPart? part = ReadBackticks();
        if (part is not null)
        {
            AddPart(part);
        }
    }

    private WordPart? ReadBackticks()
    {
        int closing = source.IndexOf('`', index + 1);
        if (closing < 0)
        {
            error = "unterminated backtick command substitution";
            return null;
        }

        WordPart part = new(WordPartKind.CommandSubstitution, source[(index + 1)..closing]);
        index = closing + 1;
        return part;
    }

    private void ScanDollar()
    {
        WordPart? part = ReadDollar();
        if (part is null)
        {
            return;
        }

        if (part.Kind == WordPartKind.Literal)
        {
            pendingLiteral.Append(part.Text);
            return;
        }

        AddPart(part);
    }

    private WordPart? ReadDollar()
    {
        char next = index + 1 < source.Length ? source[index + 1] : '\0';

        if (next == '(')
        {
            return index + 2 < source.Length && source[index + 2] == '(' ? ReadArithmetic() : ReadCommandSubstitution();
        }

        if (next == '{')
        {
            return ReadBracedParameter();
        }

        if (char.IsAsciiLetter(next) || next == '_')
        {
            return ReadNamedParameter();
        }

        if (next is '?' or '$' or '!' or '#' or '*' or '@' || char.IsAsciiDigit(next))
        {
            index += 2;
            return new WordPart(WordPartKind.Parameter, next.ToString());
        }

        index++;
        return new WordPart(WordPartKind.Literal, "$");
    }

    private WordPart ReadNamedParameter()
    {
        int start = index + 1;
        int end = start;
        while (end < source.Length && (char.IsAsciiLetterOrDigit(source[end]) || source[end] == '_'))
        {
            end++;
        }

        WordPart part = new(WordPartKind.Parameter, source[start..end]);
        index = end;
        return part;
    }

    private WordPart? ReadBracedParameter()
    {
        int depth = 0;
        int cursor = index + 1;
        while (cursor < source.Length)
        {
            if (source[cursor] == '{')
            {
                depth++;
            }
            else if (source[cursor] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    WordPart part = new(WordPartKind.Parameter, source[(index + 2)..cursor]);
                    index = cursor + 1;
                    return part;
                }
            }

            cursor++;
        }

        error = "unterminated parameter expansion";
        return null;
    }

    private WordPart? ReadCommandSubstitution()
    {
        int closing = FindClosingParenthesis(index + 2, 1);
        if (closing < 0)
        {
            error = "unterminated command substitution";
            return null;
        }

        WordPart part = new(WordPartKind.CommandSubstitution, source[(index + 2)..closing]);
        index = closing + 1;
        return part;
    }

    // Both parentheses of "$((" are already consumed, so the scan starts at depth 2 and the
    // offset it returns is the outer ')'. The inner one immediately precedes it.
    private WordPart? ReadArithmetic()
    {
        int closing = FindClosingParenthesis(index + 3, 2);
        if (closing < 1 || source[closing - 1] != ')')
        {
            error = "unterminated arithmetic expansion";
            return null;
        }

        WordPart part = new(WordPartKind.Arithmetic, source[(index + 3)..(closing - 1)]);
        index = closing + 1;
        return part;
    }

    // Returns the offset of the parenthesis that closes the run, honouring quotes so a ')' inside
    // 'text' does not end the substitution early.
    private int FindClosingParenthesis(int start, int depth)
    {
        int cursor = start;
        while (cursor < source.Length)
        {
            char current = source[cursor];

            if (current == '\\')
            {
                cursor += 2;
                continue;
            }

            if (current is '\'' or '"')
            {
                int closingQuote = source.IndexOf(current, cursor + 1);
                if (closingQuote < 0)
                {
                    return -1;
                }

                cursor = closingQuote + 1;
                continue;
            }

            if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return cursor;
                }
            }

            cursor++;
        }

        return -1;
    }

    private void AddPart(WordPart part)
    {
        FlushLiteralInto(pendingParts, pendingLiteral);
        pendingParts.Add(part);
    }

    private bool HasPendingWord() => pendingParts.Count > 0 || pendingLiteral.Length > 0;

    private void FlushWord()
    {
        if (!HasPendingWord())
        {
            return;
        }

        FlushLiteralInto(pendingParts, pendingLiteral);
        Word word = new([.. pendingParts]);
        tokens.Add(new Token(TokenKind.Word, string.Empty, word, wordStart));
        pendingParts.Clear();

        if (awaitingDelimiterFor < 0)
        {
            return;
        }

        pendingHereDocuments.Add(new PendingHereDocument(awaitingDelimiterFor, word.LiteralText, awaitingStripsTabs));
        awaitingDelimiterFor = -1;
    }

    // Bodies are read at the newline that ends the command line, in the order their operators
    // appeared, and patched onto the operator tokens that are already in the list.
    private void CaptureHereDocuments()
    {
        if (pendingHereDocuments.Count == 0)
        {
            return;
        }

        foreach (PendingHereDocument pending in pendingHereDocuments)
        {
            tokens[pending.TokenIndex] = tokens[pending.TokenIndex] with { HereDocumentBody = ReadBody(pending) };
        }

        pendingHereDocuments.Clear();
    }

    private string ReadBody(PendingHereDocument pending)
    {
        StringBuilder body = new();

        while (index < source.Length)
        {
            int lineEnd = source.IndexOf('\n', index);
            string line = lineEnd < 0 ? source[index..] : source[index..lineEnd];
            index = lineEnd < 0 ? source.Length : lineEnd + 1;

            string content = pending.StripsTabs ? line.TrimStart('\t') : line;
            if (content == pending.Delimiter)
            {
                return body.ToString();
            }

            body.Append(content).Append('\n');
        }

        return body.ToString();
    }

    private readonly record struct PendingHereDocument(int TokenIndex, string Delimiter, bool StripsTabs);
}
