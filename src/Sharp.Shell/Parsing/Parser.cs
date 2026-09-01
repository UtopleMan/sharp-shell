using Sharp.Shell.Lexing;

namespace Sharp.Shell.Parsing;

// Builds the tree, and refuses — by name — every construct that has no meaning without processes.
// Refusing here rather than emulating is the whole point: a plausible-looking stand-in produces
// wrong output silently, while a refusal escalates the line to a real shell.
public static class Parser
{
    public static ParseResult Parse(string source)
    {
        LexResult lexed = Lexer.Tokenize(source);

        return lexed.Error is not null
            ? ParseResult.Unsupported(lexed.Error)
            : new TokenParser(lexed.Tokens).Run();
    }
}

internal sealed class TokenParser(IReadOnlyList<Token> tokens)
{
    private static readonly string[] ProcessOnlyCommands = ["trap", "jobs", "fg", "bg", "wait", "disown", "coproc"];

    // Words that may only appear where the parser expects them. Meeting one where a command should
    // start is a syntax error, not a command named "fi".
    private static readonly string[] ClosingKeywords =
        ["then", "elif", "else", "fi", "do", "done", "esac", "}", ";;"];

    private static readonly string[] UnsupportedKeywords = ["select", "function"];

    private int index;
    private string? unsupported;

    public ParseResult Run()
    {
        ShellNode? program = ParseSequence();

        if (unsupported is not null)
        {
            return ParseResult.Unsupported(unsupported);
        }

        return program is null
            ? ParseResult.Unsupported("could not parse the command")
            : ParseResult.Parsed(program);
    }

    private Token Current => tokens[index];

    private bool IsOperator(string text) => Current.Kind == TokenKind.Operator && Current.Text == text;

    private ShellNode? ParseSequence()
    {
        List<ShellNode> items = [];
        SkipSeparators();

        while (Current.Kind != TokenKind.EndOfInput)
        {
            ShellNode? item = ParseAndOr();
            if (item is null)
            {
                return null;
            }

            items.Add(item);

            if (IsOperator("&"))
            {
                return Reject("background execution (&) has no meaning without processes");
            }

            if (Current.Kind != TokenKind.EndOfInput && !IsSeparator())
            {
                return Reject($"unexpected '{Describe(Current)}'");
            }

            SkipSeparators();
        }

        return items.Count == 1 ? items[0] : new SequenceNode(items);
    }

    private bool IsSeparator() => Current.Kind == TokenKind.Newline || IsOperator(";");

    private void SkipSeparators()
    {
        while (IsSeparator())
        {
            index++;
        }
    }

    private void SkipNewlines()
    {
        while (Current.Kind == TokenKind.Newline)
        {
            index++;
        }
    }

    private ShellNode? ParseAndOr()
    {
        ShellNode? left = ParsePipeline();

        while (left is not null && (IsOperator("&&") || IsOperator("||")))
        {
            AndOrKind kind = Current.Text == "&&" ? AndOrKind.And : AndOrKind.Or;
            index++;
            SkipNewlines();

            ShellNode? right = ParsePipeline();
            if (right is null)
            {
                return null;
            }

            left = new AndOrNode(left, kind, right);
        }

        return left;
    }

    private ShellNode? ParsePipeline()
    {
        bool negated = false;
        if (Current.Kind == TokenKind.Word && Current.Word!.IsFullyLiteral && Current.Word.LiteralText == "!")
        {
            negated = true;
            index++;
        }

        List<ShellNode> stages = [];
        while (true)
        {
            ShellNode? stage = ParseCommand();
            if (stage is null)
            {
                return null;
            }

            stages.Add(stage);

            if (!IsOperator("|"))
            {
                break;
            }

            index++;
            SkipNewlines();
        }

        return stages.Count == 1 && !negated ? stages[0] : new PipelineNode(stages, negated);
    }

    private ShellNode? ParseCommand()
    {
        if (IsOperator("("))
        {
            return ParseSubshell();
        }

        if (IsOperator(")"))
        {
            return Reject("unexpected ')'");
        }

        if (IsReservedWord("{"))
        {
            return ParseBraceGroup();
        }

        if (IsReservedWord("if"))
        {
            return ParseIf();
        }

        if (IsReservedWord("while") || IsReservedWord("until"))
        {
            return ParseWhile();
        }

        if (IsReservedWord("for"))
        {
            return ParseFor();
        }

        if (IsReservedWord("case"))
        {
            return ParseCase();
        }

        if (UnsupportedKeywords.Any(IsReservedWord))
        {
            return Reject($"'{Current.Word!.LiteralText}' is not supported yet");
        }

        if (ClosingKeywords.Any(IsReservedWord))
        {
            return Reject($"unexpected '{Current.Word!.LiteralText}'");
        }

        List<Assignment> assignments = [];
        List<Word> words = [];
        List<Redirection> redirections = [];

        while (true)
        {
            if (Current.Kind == TokenKind.Word)
            {
                if (words.Count > 0 && ClosingKeywords.Any(IsReservedWord))
                {
                    break;
                }

                if (!TakeWord(assignments, words))
                {
                    return null;
                }

                continue;
            }

            if (IsOperator("<(") || IsOperator(">("))
            {
                return Reject("process substitution has no meaning without processes");
            }

            if (Current.Kind == TokenKind.Operator && TryReadRedirection(out Redirection? redirection))
            {
                if (redirection is null)
                {
                    return null;
                }

                redirections.Add(redirection);
                continue;
            }

            break;
        }

        if (words.Count == 0 && redirections.Count == 0 && assignments.Count == 0)
        {
            return Reject($"unexpected '{Describe(Current)}'");
        }

        return RejectProcessOnlyWords(words) ?? new SimpleCommand(assignments, words, redirections);
    }

    private bool TakeWord(List<Assignment> assignments, List<Word> words)
    {
        Word word = Current.Word!;

        if (words.Count == 0 && TryReadAssignment(word) is { } assignment)
        {
            assignments.Add(assignment);
            index++;
            return true;
        }

        words.Add(word);
        index++;
        return true;
    }

    // NAME=value only counts before the command name, and only when the '=' sits in the word's
    // literal head — "echo x=1" is an argument, not an assignment.
    private static Assignment? TryReadAssignment(Word word)
    {
        if (word.Parts.Count == 0 || word.Parts[0].Kind != WordPartKind.Literal)
        {
            return null;
        }

        string head = word.Parts[0].Text;
        int equals = head.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0 || !IsName(head[..equals]))
        {
            return null;
        }

        List<WordPart> value = [];
        if (equals + 1 < head.Length)
        {
            value.Add(new WordPart(WordPartKind.Literal, head[(equals + 1)..]));
        }

        value.AddRange(word.Parts.Skip(1));
        return new Assignment(head[..equals], new Word(value));
    }

    private static bool IsName(string candidate) =>
        candidate.Length > 0
        && (char.IsAsciiLetter(candidate[0]) || candidate[0] == '_')
        && candidate.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private bool TryReadRedirection(out Redirection? redirection)
    {
        redirection = null;
        Token operatorToken = Current;
        string text = Current.Text;
        string digits = new([.. text.TakeWhile(char.IsAsciiDigit)]);
        string symbol = text[digits.Length..];

        RedirectionKind? kind = symbol switch
        {
            "<" => RedirectionKind.Input,
            ">" => RedirectionKind.Output,
            ">>" => RedirectionKind.Append,
            "<<" or "<<-" => RedirectionKind.HereDocument,
            ">&" => RedirectionKind.DuplicateOutput,
            "<&" => RedirectionKind.DuplicateInput,
            "&>" => RedirectionKind.OutputAndError,
            _ => null,
        };

        if (kind is null)
        {
            return false;
        }

        index++;
        if (Current.Kind != TokenKind.Word)
        {
            Reject($"'{symbol}' needs a target");
            return true;
        }

        Word target = Current.Word!;
        index++;
        redirection = new Redirection(
            kind.Value,
            digits.Length > 0 ? int.Parse(digits) : DefaultFileDescriptor(kind.Value),
            target,
            operatorToken.HereDocumentBody,
            symbol == "<<-",
            target.Parts.All(part => part.Kind is not (WordPartKind.SingleQuoted or WordPartKind.DoubleQuoted)));
        return true;
    }

    private static int DefaultFileDescriptor(RedirectionKind kind) =>
        kind is RedirectionKind.Input or RedirectionKind.HereDocument or RedirectionKind.DuplicateInput ? 0 : 1;

    private ShellNode? RejectProcessOnlyWords(IReadOnlyList<Word> words)
    {
        if (words.Count > 0 && words[0].IsFullyLiteral)
        {
            string name = words[0].LiteralText;

            if (ProcessOnlyCommands.Contains(name))
            {
                return Reject($"'{name}' has no meaning without processes");
            }

            if (name == "exec" && words.Count > 1)
            {
                return Reject("'exec' with a program has no meaning without processes");
            }
        }

        foreach (Word word in words)
        {
            if (FindProcessOnlyParameter(word.Parts) is { } parameter)
            {
                return Reject($"'{parameter}' has no meaning without processes");
            }
        }

        return null;
    }

    private static string? FindProcessOnlyParameter(IReadOnlyList<WordPart> parts)
    {
        foreach (WordPart part in parts)
        {
            if (part.Nested is not null && FindProcessOnlyParameter(part.Nested) is { } nested)
            {
                return nested;
            }

            if (part.Kind != WordPartKind.Parameter)
            {
                continue;
            }

            if (part.Text is "$" or "!" or "PPID")
            {
                return $"${part.Text}";
            }
        }

        return null;
    }

    private bool IsReservedWord(string keyword) =>
        Current.Kind == TokenKind.Word && Current.Word!.IsFullyLiteral && Current.Word.LiteralText == keyword;

    private bool TakeReservedWord(string keyword)
    {
        if (!IsReservedWord(keyword))
        {
            return false;
        }

        index++;
        return true;
    }

    private bool ExpectReservedWord(string keyword)
    {
        SkipSeparators();
        if (TakeReservedWord(keyword))
        {
            return true;
        }

        Reject($"expected '{keyword}' but found '{Describe(Current)}'");
        return false;
    }

    // A list of commands running until one of the words that closes the enclosing construct. The
    // terminator is left unconsumed so the caller can check which one arrived.
    private ShellNode? ParseCompoundList(params string[] terminators)
    {
        List<ShellNode> items = [];
        SkipSeparators();

        while (Current.Kind != TokenKind.EndOfInput && !AtTerminator(terminators))
        {
            ShellNode? item = ParseAndOr();
            if (item is null)
            {
                return null;
            }

            items.Add(item);

            if (IsOperator("&"))
            {
                return Reject("background execution (&) has no meaning without processes");
            }

            if (!IsSeparator() && !AtTerminator(terminators) && Current.Kind != TokenKind.EndOfInput)
            {
                return Reject($"unexpected '{Describe(Current)}'");
            }

            SkipSeparators();
        }

        return items.Count == 1 ? items[0] : new SequenceNode(items);
    }

    private bool AtTerminator(string[] terminators) =>
        terminators.Any(terminator => terminator.StartsWith(';') ? IsOperator(terminator) : IsReservedWord(terminator))
        || (terminators.Contains(")") && IsOperator(")"));

    private ShellNode? ParseSubshell()
    {
        index++;
        ShellNode? body = ParseCompoundList(")");
        if (body is null)
        {
            return null;
        }

        if (!IsOperator(")"))
        {
            return Reject("expected ')'");
        }

        index++;
        return new SubshellNode(body);
    }

    private ShellNode? ParseBraceGroup()
    {
        index++;
        ShellNode? body = ParseCompoundList("}");
        if (body is null)
        {
            return null;
        }

        return ExpectReservedWord("}") ? new BraceGroupNode(body) : null;
    }

    private ShellNode? ParseIf()
    {
        index++;
        List<ConditionalBranch> branches = [];
        ShellNode? elseBody = null;

        while (true)
        {
            ShellNode? condition = ParseCompoundList("then");
            if (condition is null || !ExpectReservedWord("then"))
            {
                return null;
            }

            ShellNode? body = ParseCompoundList("elif", "else", "fi");
            if (body is null)
            {
                return null;
            }

            branches.Add(new ConditionalBranch(condition, body));

            if (TakeReservedWord("elif"))
            {
                continue;
            }

            if (TakeReservedWord("else"))
            {
                elseBody = ParseCompoundList("fi");
                if (elseBody is null)
                {
                    return null;
                }
            }

            return ExpectReservedWord("fi") ? new IfNode(branches, elseBody) : null;
        }
    }

    private ShellNode? ParseWhile()
    {
        bool untilForm = IsReservedWord("until");
        index++;

        ShellNode? condition = ParseCompoundList("do");
        if (condition is null || !ExpectReservedWord("do"))
        {
            return null;
        }

        ShellNode? body = ParseCompoundList("done");
        if (body is null)
        {
            return null;
        }

        return ExpectReservedWord("done") ? new WhileNode(condition, body, untilForm) : null;
    }

    private ShellNode? ParseFor()
    {
        index++;
        if (Current.Kind != TokenKind.Word || !Current.Word!.IsFullyLiteral)
        {
            return Reject("expected a variable name after 'for'");
        }

        string variable = Current.Word.LiteralText;
        index++;

        List<Word> items = [];
        if (TakeReservedWord("in"))
        {
            while (Current.Kind == TokenKind.Word && !IsReservedWord("do"))
            {
                items.Add(Current.Word!);
                index++;
            }
        }

        SkipSeparators();
        if (!ExpectReservedWord("do"))
        {
            return null;
        }

        ShellNode? body = ParseCompoundList("done");
        if (body is null)
        {
            return null;
        }

        return ExpectReservedWord("done") ? new ForNode(variable, items, body) : null;
    }

    private ShellNode? ParseCase()
    {
        index++;
        if (Current.Kind != TokenKind.Word)
        {
            return Reject("expected a word after 'case'");
        }

        Word subject = Current.Word!;
        index++;

        SkipSeparators();
        if (!ExpectReservedWord("in"))
        {
            return null;
        }

        List<CaseArm> arms = [];
        SkipSeparators();

        while (!IsReservedWord("esac"))
        {
            if (Current.Kind == TokenKind.EndOfInput)
            {
                return Reject("expected 'esac'");
            }

            CaseArm? arm = ParseCaseArm();
            if (arm is null)
            {
                return null;
            }

            arms.Add(arm);
            SkipSeparators();
        }

        index++;
        return new CaseNode(subject, arms);
    }

    private CaseArm? ParseCaseArm()
    {
        List<Word> patterns = [];

        while (true)
        {
            if (Current.Kind != TokenKind.Word)
            {
                Reject($"expected a case pattern but found '{Describe(Current)}'");
                return null;
            }

            patterns.Add(Current.Word!);
            index++;

            if (IsOperator("|"))
            {
                index++;
                continue;
            }

            break;
        }

        if (!IsOperator(")"))
        {
            Reject("expected ')' after a case pattern");
            return null;
        }

        index++;
        ShellNode? body = ParseCompoundList(";;", "esac");
        if (body is null)
        {
            return null;
        }

        if (IsOperator(";;"))
        {
            index++;
        }

        return new CaseArm(patterns, body);
    }

    private ShellNode? Reject(string reason)
    {
        unsupported ??= reason;
        return null;
    }

    private static string Describe(Token token) => token.Kind switch
    {
        TokenKind.EndOfInput => "end of input",
        TokenKind.Newline => "newline",
        TokenKind.Word => token.Word!.LiteralText,
        _ => token.Text,
    };
}
