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

        if (lexed.Error is not null)
        {
            return new ParseResult(null, lexed.Error, lexed.IsIncomplete);
        }

        ParseResult parsed = new TokenParser(lexed.Tokens).Run();

        // A here-document still waiting for its delimiter lexes and parses cleanly, so the lexer's
        // answer has to survive a successful parse.
        return lexed.IsIncomplete ? parsed with { IsIncomplete = true } : parsed;
    }
}

internal sealed class TokenParser(IReadOnlyList<Token> tokens)
{
    private static readonly string[] ProcessOnlyCommands = ["trap", "jobs", "fg", "bg", "wait", "disown", "coproc"];

    // Words that may only appear where the parser expects them. Meeting one where a command should
    // start is a syntax error, not a command named "fi".
    private static readonly string[] ClosingKeywords =
        ["then", "elif", "else", "fi", "do", "done", "esac", "}", ";;"];


    private int index;
    private string? unsupported;
    private bool incomplete;
    private bool unsupportedConstruct;

    public ParseResult Run()
    {
        ShellNode? program = ParseSequence();

        if (unsupported is not null)
        {
            return new ParseResult(null, unsupported, incomplete, unsupportedConstruct);
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
                return RejectUnsupportedConstruct("background execution (&) has no meaning without processes");
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

        if (IsReservedWord("select"))
        {
            return ParseSelect();
        }

        if (IsReservedWord("function"))
        {
            return ParseKeywordFunction();
        }

        if (IsReservedWord("[["))
        {
            return ParseCondition();
        }

        if (StartsAFunctionDefinition())
        {
            return ParseNamedFunction();
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
                return RejectUnsupportedConstruct("process substitution has no meaning without processes");
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

        // Nothing at all where a command should start. At the end of the input that is a line
        // ending in `|` or `&&`, which wants another line; anywhere else it is a syntax error.
        if (words.Count == 0 && redirections.Count == 0 && assignments.Count == 0)
        {
            string reason = $"unexpected '{Describe(Current)}'";
            return AtEndOfInput ? RejectIncomplete(reason) : Reject(reason);
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
        (string digits, string symbol) = SplitFileDescriptor(Current.Text);

        if (RedirectionKindOf(symbol) is not { } kind)
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
            kind,
            digits.Length > 0 ? int.Parse(digits) : DefaultFileDescriptor(kind),
            target,
            operatorToken.HereDocumentBody,
            symbol == "<<-",
            target.Parts.All(part => part.Kind is not (WordPartKind.SingleQuoted or WordPartKind.DoubleQuoted)));
        return true;
    }

    // `2>` and `2>&1` name their descriptor in front of the operator; `>` and `&>` leave it to the
    // default for their kind.
    private static (string Digits, string Symbol) SplitFileDescriptor(string text)
    {
        string digits = new([.. text.TakeWhile(char.IsAsciiDigit)]);
        return (digits, text[digits.Length..]);
    }

    private static RedirectionKind? RedirectionKindOf(string symbol) => symbol switch
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

    private static int DefaultFileDescriptor(RedirectionKind kind) =>
        kind is RedirectionKind.Input or RedirectionKind.HereDocument or RedirectionKind.DuplicateInput ? 0 : 1;

    private ShellNode? RejectProcessOnlyWords(IReadOnlyList<Word> words)
    {
        if (words.Count > 0 && words[0].IsFullyLiteral)
        {
            string name = words[0].LiteralText;

            if (ProcessOnlyCommands.Contains(name))
            {
                return RejectUnsupportedConstruct($"'{name}' has no meaning without processes");
            }

            if (name == "exec" && words.Count > 1)
            {
                return RejectUnsupportedConstruct("'exec' with a program has no meaning without processes");
            }
        }

        foreach (Word word in words)
        {
            if (FindProcessOnlyParameter(word.Parts) is { } parameter)
            {
                return RejectUnsupportedConstruct($"'{parameter}' has no meaning without processes");
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

            if (part.Text == "PPID")
            {
                return "$PPID";
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

        string reason = $"expected '{keyword}' but found '{Describe(Current)}'";

        if (AtEndOfInput)
        {
            RejectIncomplete(reason);
        }
        else
        {
            Reject(reason);
        }

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
                return RejectUnsupportedConstruct("background execution (&) has no meaning without processes");
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
            return AtEndOfInput ? RejectIncomplete("expected ')'") : Reject("expected ')'");
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
            if (AtEndOfInput)
            {
                return RejectIncomplete("expected 'esac'");
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
                string reason = $"expected a case pattern but found '{Describe(Current)}'";

                if (AtEndOfInput)
                {
                    RejectIncomplete(reason);
                }
                else
                {
                    Reject(reason);
                }

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
            if (AtEndOfInput)
            {
                RejectIncomplete("expected ')' after a case pattern");
            }
            else
            {
                Reject("expected ')' after a case pattern");
            }

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

    // `name ( )` — three tokens, because the lexer splits the parentheses off the name. Only a valid
    // name qualifies, so `echo (` stays the syntax error it is.
    private bool StartsAFunctionDefinition() =>
        Current.Kind == TokenKind.Word
        && Current.Word!.IsFullyLiteral
        && IsName(Current.Word.LiteralText)
        && index + 2 < tokens.Count
        && tokens[index + 1] is { Kind: TokenKind.Operator, Text: "(" }
        && tokens[index + 2] is { Kind: TokenKind.Operator, Text: ")" };

    private ShellNode? ParseNamedFunction()
    {
        string name = Current.Word!.LiteralText;
        index += 3;

        return ParseFunctionBody(name);
    }

    // `function name { … }`, with the parentheses optional after the name.
    private ShellNode? ParseKeywordFunction()
    {
        index++;

        if (Current.Kind != TokenKind.Word || !Current.Word!.IsFullyLiteral || !IsName(Current.Word.LiteralText))
        {
            return Reject("expected a function name after 'function'");
        }

        string name = Current.Word.LiteralText;
        index++;

        if (index + 1 < tokens.Count
            && tokens[index] is { Kind: TokenKind.Operator, Text: "(" }
            && tokens[index + 1] is { Kind: TokenKind.Operator, Text: ")" })
        {
            index += 2;
        }

        return ParseFunctionBody(name);
    }

    private ShellNode? ParseFunctionBody(string name)
    {
        SkipSeparators();

        if (IsReservedWord("{"))
        {
            ShellNode? body = ParseBraceGroup();
            return body is null ? null : new FunctionDefinition(name, body);
        }

        if (IsOperator("("))
        {
            ShellNode? body = ParseSubshell();
            return body is null ? null : new FunctionDefinition(name, body);
        }

        string missingBody = $"'{name}' needs a '{{ … }}' body";

        return AtEndOfInput ? RejectIncomplete(missingBody) : Reject(missingBody);
    }

    // The operands are kept as words and expanded without splitting at run time, so `]]` closing the
    // construct is the only thing this has to find. An operator between them — `&&`, `||`, `(` — is
    // taken as a literal operand, because inside [[ ]] that is what it is.
    private ShellNode? ParseCondition()
    {
        List<Word> words = [Current.Word!];
        index++;
        int depth = 0;

        while (!ClosesCondition())
        {
            if (AtEndOfInput)
            {
                return RejectIncomplete("expected ']]'");
            }

            // A newline inside [[ ]] separates nothing: the construct runs until its terminator,
            // and bash keeps reading until it arrives.
            if (Current.Kind == TokenKind.Newline)
            {
                index++;
                continue;
            }

            if (Current.Kind == TokenKind.Operator)
            {
                depth += Current.Text == "(" ? 1 : Current.Text == ")" ? -1 : 0;

                if (depth < 0)
                {
                    return Reject("unexpected ')' in a [[ ]] condition");
                }
            }

            words.Add(Current.Kind == TokenKind.Word
                ? Current.Word!
                : new Word([new WordPart(WordPartKind.Literal, Current.Text)]));
            index++;
        }

        if (depth > 0)
        {
            return Reject("expected ')' in a [[ ]] condition");
        }

        words.Add(Current.Word!);
        index++;

        return new ConditionNode(words);
    }

    // Only bare `]]` closes the construct. `[[ -z ']]' ]]` asks whether the two characters are an
    // empty string, and a quoted operand that happens to spell the terminator is an operand.
    private bool ClosesCondition() =>
        Current.Kind == TokenKind.Word
        && Current.Word!.Parts is [{ Kind: WordPartKind.Literal, Text: "]]" }];

    private ShellNode? ParseSelect()
    {
        index++;

        if (Current.Kind != TokenKind.Word || !Current.Word!.IsFullyLiteral)
        {
            return Reject("expected a variable name after 'select'");
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

        return ExpectReservedWord("done") ? new SelectNode(variable, items, body) : null;
    }

    private ShellNode? Reject(string reason)
    {
        unsupported ??= reason;
        return null;
    }

    // A failure more input could fix: an unclosed construct rather than a wrong one. Marked at the
    // sites that know the difference, never inferred from where the tokens ran out — `trap 'x' EXIT`
    // also ends the input, and no amount of further input makes it something this shell can run.
    private ShellNode? RejectIncomplete(string reason)
    {
        incomplete |= unsupported is null;
        return Reject(reason);
    }

    // Valid bash this shell does not implement, as opposed to input bash would refuse too. The
    // line is handed to a real shell rather than being an error, so a script does not stop at it.
    private ShellNode? RejectUnsupportedConstruct(string reason)
    {
        unsupportedConstruct |= unsupported is null;
        return Reject(reason);
    }

    private bool AtEndOfInput => Current.Kind == TokenKind.EndOfInput;

    private static string Describe(Token token) => token.Kind switch
    {
        TokenKind.EndOfInput => "end of input",
        TokenKind.Newline => "newline",
        TokenKind.Word => token.Word!.LiteralText,
        _ => token.Text,
    };
}
