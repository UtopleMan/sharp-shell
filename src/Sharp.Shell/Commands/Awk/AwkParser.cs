namespace Sharp.Shell.Commands.Awk;

// POSIX awk's grammar, with the precedence table taken from the standard rather than from intuition.
// Three places where intuition is wrong and the tests pin it:
//
//   * relational operators do not associate, so `a < b < c` is a syntax error rather than a
//     comparison against 0 or 1;
//   * concatenation binds looser than `+`/`-`, so `a " " -1` concatenates a with -1 rather than
//     subtracting 1 from the concatenation;
//   * `^` binds tighter than unary minus, so `-2^2` is -4.
//
// Everything this dialect will not implement is refused by name before parsing starts, so a program
// that both uses `getline` and has a syntax error escalates rather than erroring.
internal sealed class AwkParser(IReadOnlyList<AwkToken> tokens)
{
    private readonly Dictionary<string, AwkFunction> functions = new(StringComparer.Ordinal);

    private readonly List<AwkRule> rules = [];

    private int index;

    private string? error;

    public static AwkParseResult Parse(string source)
    {
        AwkLexer lexer = new(source);
        IReadOnlyList<AwkToken>? read = lexer.Read();

        if (read is null)
        {
            return AwkParseResult.Failed(lexer.Error!);
        }

        string? refused = RefusedConstructIn(read);

        return refused is not null
            ? AwkParseResult.Unsupported(refused)
            : new AwkParser(read).ReadProgram();
    }

    // One pass over the tokens, before any structure exists, so every refusal names its construct
    // whatever position it appeared in.
    private static string? RefusedConstructIn(IReadOnlyList<AwkToken> read)
    {
        for (int position = 0; position < read.Count; position++)
        {
            string? refused = RefusalFor(read, position);

            if (refused is not null)
            {
                return refused;
            }
        }

        return null;
    }

    private static string? RefusalFor(IReadOnlyList<AwkToken> read, int position)
    {
        AwkToken token = read[position];

        return token.Kind switch
        {
            AwkTokenKind.Getline => "getline",
            AwkTokenKind.Pipe => token.Text,
            AwkTokenKind.Builtin when AwkBuiltins.IsRefusedBuiltin(token.Text) => $"{token.Text}()",
            AwkTokenKind.Name when AwkBuiltins.IsRefusedVariable(token.Text) => token.Text,
            AwkTokenKind.Caret or AwkTokenKind.PowerAssign when token.Text.StartsWith("**", StringComparison.Ordinal) => token.Text,
            AwkTokenKind.Name when token.Text == "func" && IntroducesFunction(read, position) => "func",
            _ => null,
        };
    }

    private static bool IntroducesFunction(IReadOnlyList<AwkToken> read, int position) =>
        position + 1 < read.Count && read[position + 1].Kind is AwkTokenKind.Name or AwkTokenKind.FunctionName;

    private AwkParseResult ReadProgram()
    {
        while (error is null)
        {
            SkipTerminators();

            if (Current.Kind == AwkTokenKind.EndOfInput)
            {
                break;
            }

            ReadItem();
        }

        return error is not null
            ? AwkParseResult.Failed(error)
            : AwkParseResult.Parsed(new AwkProgram(rules, functions));
    }

    private void ReadItem()
    {
        if (Current.Kind == AwkTokenKind.Function)
        {
            ReadFunction();
            return;
        }

        if (Current.Kind is AwkTokenKind.Begin or AwkTokenKind.End)
        {
            AwkPattern pattern = Current.Kind == AwkTokenKind.Begin ? new AwkBeginPattern() : new AwkEndPattern();
            index++;
            SkipNewlines();
            rules.Add(new AwkRule(pattern, ReadBlock()));
            return;
        }

        if (Current.Kind == AwkTokenKind.LeftBrace)
        {
            rules.Add(new AwkRule(null, ReadBlock()));
            return;
        }

        ReadPatternRule();
    }

    private void ReadPatternRule()
    {
        AwkExpression first = ReadExpression(isPrintContext: false);
        AwkPattern pattern = new AwkExpressionPattern(first);

        if (Current.Kind == AwkTokenKind.Comma)
        {
            index++;
            SkipNewlines();
            pattern = new AwkRangePattern(first, ReadExpression(isPrintContext: false));
        }

        AwkBlock? action = Current.Kind == AwkTokenKind.LeftBrace ? ReadBlock() : null;
        rules.Add(new AwkRule(pattern, action));
    }

    private void ReadFunction()
    {
        index++;

        if (Current.Kind is not (AwkTokenKind.Name or AwkTokenKind.FunctionName))
        {
            Fail("function name expected");
            return;
        }

        string name = Current.Text;
        index++;
        Expect(AwkTokenKind.LeftParen, "'(' expected");

        List<string> parameters = [];

        while (error is null && Current.Kind != AwkTokenKind.RightParen)
        {
            if (Current.Kind != AwkTokenKind.Name)
            {
                Fail("parameter name expected");
                return;
            }

            parameters.Add(Current.Text);
            index++;

            if (Current.Kind == AwkTokenKind.Comma)
            {
                index++;
                SkipNewlines();
            }
        }

        Expect(AwkTokenKind.RightParen, "')' expected");
        SkipNewlines();
        AwkBlock body = ReadBlock();

        if (error is null)
        {
            functions[name] = new AwkFunction(name, parameters, body);
        }
    }

    private AwkBlock ReadBlock()
    {
        Expect(AwkTokenKind.LeftBrace, "'{' expected");
        List<AwkStatement> statements = [];

        while (error is null)
        {
            SkipTerminators();

            if (Current.Kind == AwkTokenKind.RightBrace)
            {
                index++;
                break;
            }

            if (Current.Kind == AwkTokenKind.EndOfInput)
            {
                Fail("unexpected end of program");
                break;
            }

            statements.Add(ReadStatement());
        }

        return new AwkBlock(statements);
    }

    private AwkStatement ReadStatement() => Current.Kind switch
    {
        AwkTokenKind.LeftBrace => ReadBlock(),
        AwkTokenKind.If => ReadIf(),
        AwkTokenKind.While => ReadWhile(),
        AwkTokenKind.Do => ReadDoWhile(),
        AwkTokenKind.For => ReadFor(),
        AwkTokenKind.Break => Keyword(new AwkBreakStatement()),
        AwkTokenKind.Continue => Keyword(new AwkContinueStatement()),
        AwkTokenKind.Next => Keyword(new AwkNextStatement()),
        AwkTokenKind.NextFile => Keyword(new AwkNextFileStatement()),
        AwkTokenKind.Exit => ReadExit(),
        AwkTokenKind.Return => ReadReturn(),
        AwkTokenKind.Delete => ReadDelete(),
        AwkTokenKind.Print or AwkTokenKind.Printf => ReadPrint(),
        AwkTokenKind.Semicolon => Keyword(new AwkEmptyStatement()),
        _ => ReadExpressionStatement(),
    };

    private AwkStatement Keyword(AwkStatement statement)
    {
        index++;
        return statement;
    }

    private AwkStatement ReadIf()
    {
        index++;
        Expect(AwkTokenKind.LeftParen, "'(' expected after if");
        AwkExpression condition = ReadExpression(isPrintContext: false);
        Expect(AwkTokenKind.RightParen, "')' expected");
        SkipNewlines();
        AwkStatement then = ReadStatement();

        int resume = index;
        SkipTerminators();

        if (Current.Kind != AwkTokenKind.Else)
        {
            index = resume;
            return new AwkIfStatement(condition, then, null);
        }

        index++;
        SkipNewlines();
        return new AwkIfStatement(condition, then, ReadStatement());
    }

    private AwkStatement ReadWhile()
    {
        index++;
        Expect(AwkTokenKind.LeftParen, "'(' expected after while");
        AwkExpression condition = ReadExpression(isPrintContext: false);
        Expect(AwkTokenKind.RightParen, "')' expected");
        SkipNewlines();

        return new AwkWhileStatement(condition, ReadStatement());
    }

    private AwkStatement ReadDoWhile()
    {
        index++;
        SkipNewlines();
        AwkStatement body = ReadStatement();
        SkipTerminators();
        Expect(AwkTokenKind.While, "'while' expected after do body");
        Expect(AwkTokenKind.LeftParen, "'(' expected after while");
        AwkExpression condition = ReadExpression(isPrintContext: false);
        Expect(AwkTokenKind.RightParen, "')' expected");

        return new AwkDoWhileStatement(body, condition);
    }

    // `for (k in a)` and `for (init; cond; update)` share an opening parenthesis and nothing else, so
    // the membership form is recognised by its exact four-token shape before anything is parsed.
    private AwkStatement ReadFor()
    {
        index++;
        Expect(AwkTokenKind.LeftParen, "'(' expected after for");

        if (IsMembershipHeader())
        {
            string variable = Current.Text;
            string array = tokens[index + 2].Text;
            index += 4;
            SkipNewlines();
            return new AwkForInStatement(variable, array, ReadStatement());
        }

        AwkStatement? initialiser = Current.Kind == AwkTokenKind.Semicolon ? null : ReadExpressionStatement();
        Expect(AwkTokenKind.Semicolon, "';' expected in for header");
        AwkExpression? condition = Current.Kind == AwkTokenKind.Semicolon
            ? null
            : ReadExpression(isPrintContext: false);
        Expect(AwkTokenKind.Semicolon, "';' expected in for header");
        AwkStatement? update = Current.Kind == AwkTokenKind.RightParen ? null : ReadExpressionStatement();
        Expect(AwkTokenKind.RightParen, "')' expected");
        SkipNewlines();

        return new AwkForStatement(initialiser, condition, update, ReadStatement());
    }

    private bool IsMembershipHeader() =>
        Current.Kind == AwkTokenKind.Name
        && index + 3 < tokens.Count
        && tokens[index + 1].Kind == AwkTokenKind.In
        && tokens[index + 2].Kind == AwkTokenKind.Name
        && tokens[index + 3].Kind == AwkTokenKind.RightParen;

    private AwkStatement ReadExit()
    {
        index++;
        return new AwkExitStatement(StartsExpression(Current.Kind) ? ReadExpression(isPrintContext: false) : null);
    }

    private AwkStatement ReadReturn()
    {
        index++;
        return new AwkReturnStatement(StartsExpression(Current.Kind) ? ReadExpression(isPrintContext: false) : null);
    }

    private AwkStatement ReadDelete()
    {
        index++;

        if (Current.Kind != AwkTokenKind.Name)
        {
            Fail("array name expected after delete");
            return new AwkEmptyStatement();
        }

        string name = Current.Text;
        index++;

        if (Current.Kind != AwkTokenKind.LeftBracket)
        {
            return new AwkDeleteStatement(name, []);
        }

        index++;
        List<AwkExpression> subscripts = ReadExpressionList(AwkTokenKind.RightBracket);
        Expect(AwkTokenKind.RightBracket, "']' expected");

        return new AwkDeleteStatement(name, subscripts);
    }

    private AwkStatement ReadExpressionStatement() =>
        new AwkExpressionStatement(ReadExpression(isPrintContext: false));

    private AwkStatement ReadPrint()
    {
        bool isFormatted = Current.Kind == AwkTokenKind.Printf;
        index++;

        IReadOnlyList<AwkExpression> arguments = ReadPrintArguments();
        AwkRedirection? redirection = ReadRedirection();

        if (isFormatted && arguments.Count == 0)
        {
            Fail("printf needs a format");
        }

        return isFormatted
            ? new AwkPrintfStatement(arguments, redirection)
            : new AwkPrintStatement(arguments, redirection);
    }

    // `print (a, b) > "f"` prints two fields to a file; `print (a) (b)` concatenates. The
    // parenthesised list is only the argument list when nothing follows it but the end of the
    // statement or a redirection, so it is tried and abandoned rather than guessed at.
    private IReadOnlyList<AwkExpression> ReadPrintArguments()
    {
        if (!StartsExpression(Current.Kind))
        {
            return [];
        }

        if (Current.Kind == AwkTokenKind.LeftParen)
        {
            int resume = index;
            IReadOnlyList<AwkExpression>? parenthesised = TryReadParenthesisedList();

            if (parenthesised is not null && EndsPrintArguments(Current.Kind))
            {
                return parenthesised;
            }

            index = resume;
        }

        return ReadPrintExpressionList();
    }

    private IReadOnlyList<AwkExpression>? TryReadParenthesisedList()
    {
        index++;
        List<AwkExpression> arguments = [];

        while (true)
        {
            arguments.Add(ReadExpression(isPrintContext: false));

            if (error is not null)
            {
                return null;
            }

            if (Current.Kind != AwkTokenKind.Comma)
            {
                break;
            }

            index++;
            SkipNewlines();
        }

        if (Current.Kind != AwkTokenKind.RightParen)
        {
            return null;
        }

        index++;
        return arguments;
    }

    private List<AwkExpression> ReadPrintExpressionList()
    {
        List<AwkExpression> arguments = [ReadExpression(isPrintContext: true)];

        while (error is null && Current.Kind == AwkTokenKind.Comma)
        {
            index++;
            SkipNewlines();
            arguments.Add(ReadExpression(isPrintContext: true));
        }

        return arguments;
    }

    private AwkRedirection? ReadRedirection()
    {
        if (Current.Kind is not (AwkTokenKind.Greater or AwkTokenKind.Append))
        {
            return null;
        }

        AwkRedirectionKind kind = Current.Kind == AwkTokenKind.Greater
            ? AwkRedirectionKind.Truncate
            : AwkRedirectionKind.Append;

        index++;
        return new AwkRedirection(kind, ReadConcatenation(isPrintContext: true));
    }

    private List<AwkExpression> ReadExpressionList(AwkTokenKind closer)
    {
        List<AwkExpression> items = [];

        while (error is null && Current.Kind != closer)
        {
            items.Add(ReadExpression(isPrintContext: false));

            if (Current.Kind != AwkTokenKind.Comma)
            {
                break;
            }

            index++;
            SkipNewlines();
        }

        return items;
    }

    private AwkExpression ReadExpression(bool isPrintContext) => ReadAssignment(isPrintContext);

    private AwkExpression ReadAssignment(bool isPrintContext)
    {
        AwkExpression target = ReadTernary(isPrintContext);
        AwkAssignOperator? assignment = AssignmentOperatorOf(Current.Kind);

        if (assignment is null)
        {
            return target;
        }

        if (!IsAssignable(target))
        {
            Fail("assignment to something that is not a variable, field or array element");
            return target;
        }

        index++;
        SkipNewlines();
        return new AwkAssignment(target, assignment.Value, ReadAssignment(isPrintContext));
    }

    private AwkExpression ReadTernary(bool isPrintContext)
    {
        AwkExpression condition = ReadOr(isPrintContext);

        if (Current.Kind != AwkTokenKind.Question)
        {
            return condition;
        }

        index++;
        SkipNewlines();
        AwkExpression whenTrue = ReadTernary(isPrintContext);
        Expect(AwkTokenKind.Colon, "':' expected");
        SkipNewlines();

        return new AwkTernary(condition, whenTrue, ReadTernary(isPrintContext));
    }

    private AwkExpression ReadOr(bool isPrintContext)
    {
        AwkExpression left = ReadAnd(isPrintContext);

        while (error is null && Current.Kind == AwkTokenKind.Or)
        {
            index++;
            SkipNewlines();
            left = new AwkBinary(left, AwkBinaryOperator.Or, ReadAnd(isPrintContext));
        }

        return left;
    }

    private AwkExpression ReadAnd(bool isPrintContext)
    {
        AwkExpression left = ReadMembership(isPrintContext);

        while (error is null && Current.Kind == AwkTokenKind.And)
        {
            index++;
            SkipNewlines();
            left = new AwkBinary(left, AwkBinaryOperator.And, ReadMembership(isPrintContext));
        }

        return left;
    }

    private AwkExpression ReadMembership(bool isPrintContext)
    {
        AwkExpression left = ReadMatch(isPrintContext);

        while (error is null && Current.Kind == AwkTokenKind.In)
        {
            index++;

            if (Current.Kind != AwkTokenKind.Name)
            {
                Fail("array name expected after in");
                return left;
            }

            left = new AwkMembership(SubscriptsOf(left), Current.Text);
            index++;
        }

        return left;
    }

    private static IReadOnlyList<AwkExpression> SubscriptsOf(AwkExpression left) =>
        left is AwkSubscriptList list ? list.Subscripts : [left];

    private AwkExpression ReadMatch(bool isPrintContext)
    {
        AwkExpression left = ReadRelational(isPrintContext);

        while (error is null && Current.Kind is AwkTokenKind.Match or AwkTokenKind.NotMatch)
        {
            bool isNegated = Current.Kind == AwkTokenKind.NotMatch;
            index++;
            left = new AwkMatchExpression(left, ReadRelational(isPrintContext), isNegated);
        }

        return left;
    }

    // POSIX makes the relational operators non-associative, so `a < b < c` is a syntax error rather
    // than a comparison of c against a 0 or a 1.
    private AwkExpression ReadRelational(bool isPrintContext)
    {
        AwkExpression left = ReadConcatenation(isPrintContext);
        AwkBinaryOperator? relation = RelationalOperatorOf(Current.Kind, isPrintContext);

        if (relation is null)
        {
            return left;
        }

        index++;
        AwkExpression right = ReadConcatenation(isPrintContext);

        if (RelationalOperatorOf(Current.Kind, isPrintContext) is not null)
        {
            Fail("relational operators do not associate");
        }

        return new AwkBinary(left, relation.Value, right);
    }

    private AwkExpression ReadConcatenation(bool isPrintContext)
    {
        AwkExpression left = ReadAdditive(isPrintContext);

        while (error is null && StartsConcatenationOperand(Current.Kind))
        {
            left = new AwkBinary(left, AwkBinaryOperator.Concatenate, ReadAdditive(isPrintContext));
        }

        return left;
    }

    private AwkExpression ReadAdditive(bool isPrintContext)
    {
        AwkExpression left = ReadMultiplicative(isPrintContext);

        while (error is null && Current.Kind is AwkTokenKind.Plus or AwkTokenKind.Minus)
        {
            AwkBinaryOperator addition = Current.Kind == AwkTokenKind.Plus
                ? AwkBinaryOperator.Add
                : AwkBinaryOperator.Subtract;

            index++;
            left = new AwkBinary(left, addition, ReadMultiplicative(isPrintContext));
        }

        return left;
    }

    private AwkExpression ReadMultiplicative(bool isPrintContext)
    {
        AwkExpression left = ReadUnary(isPrintContext);

        while (error is null && Current.Kind is AwkTokenKind.Star or AwkTokenKind.Slash or AwkTokenKind.Percent)
        {
            AwkBinaryOperator product = Current.Kind switch
            {
                AwkTokenKind.Star => AwkBinaryOperator.Multiply,
                AwkTokenKind.Slash => AwkBinaryOperator.Divide,
                _ => AwkBinaryOperator.Modulo,
            };

            index++;
            left = new AwkBinary(left, product, ReadUnary(isPrintContext));
        }

        return left;
    }

    private static AwkUnaryOperator? UnaryOperatorOf(AwkTokenKind kind) => kind switch
    {
        AwkTokenKind.Not => AwkUnaryOperator.Not,
        AwkTokenKind.Minus => AwkUnaryOperator.Negate,
        AwkTokenKind.Plus => AwkUnaryOperator.Plus,
        _ => null,
    };

    private AwkExpression ReadUnary(bool isPrintContext)
    {
        AwkUnaryOperator? unary = UnaryOperatorOf(Current.Kind);

        if (unary is null)
        {
            return ReadPower(isPrintContext);
        }

        index++;
        return new AwkUnary(unary.Value, ReadUnary(isPrintContext));
    }

    // `^` is right-associative and binds tighter than unary minus, so `-2^2` is -4 and `2^3^2` is
    // 2^9. Its right operand goes back through the unary level, which is what makes `2^-1` legal.
    private AwkExpression ReadPower(bool isPrintContext)
    {
        AwkExpression left = ReadPostfix(isPrintContext);

        if (Current.Kind != AwkTokenKind.Caret)
        {
            return left;
        }

        index++;
        return new AwkBinary(left, AwkBinaryOperator.Power, ReadUnary(isPrintContext));
    }

    private AwkExpression ReadPostfix(bool isPrintContext)
    {
        AwkExpression operand = ReadPrimary(isPrintContext);

        while (error is null
            && Current.Kind is AwkTokenKind.Increment or AwkTokenKind.Decrement
            && IsAssignable(operand))
        {
            operand = new AwkIncrement(operand, IsPrefix: false, Current.Kind == AwkTokenKind.Decrement);
            index++;
        }

        return operand;
    }

    private AwkExpression ReadPrimary(bool isPrintContext)
    {
        AwkToken token = Current;

        switch (token.Kind)
        {
            case AwkTokenKind.Number:
                index++;
                return new AwkNumberLiteral(token.Number);
            case AwkTokenKind.String:
                index++;
                return new AwkStringLiteral(token.Text);
            case AwkTokenKind.Regex:
                index++;
                return new AwkRegexLiteral(token.Text);
            case AwkTokenKind.Dollar:
                index++;
                return new AwkField(ReadFieldOperand(isPrintContext));
            case AwkTokenKind.Increment:
            case AwkTokenKind.Decrement:
                index++;
                return new AwkIncrement(
                    ReadFieldOperand(isPrintContext),
                    IsPrefix: true,
                    token.Kind == AwkTokenKind.Decrement);
            case AwkTokenKind.LeftParen:
                return ReadParenthesised();
            case AwkTokenKind.Name:
                return ReadNameReference();
            case AwkTokenKind.FunctionName:
                return ReadCall(token.Text);
            case AwkTokenKind.Builtin:
                return ReadBuiltinCall(token.Text);
            default:
                Fail($"unexpected '{token.Text}'");
                return new AwkNumberLiteral(0);
        }
    }

    // `$` binds tighter than `++`, so `$i++` increments the field rather than the index, and its
    // operand is therefore read without the postfix level. A sign or a `!` still belongs to the
    // operand, though — `$-0` is `$0` and both reference implementations accept it.
    private AwkExpression ReadFieldOperand(bool isPrintContext)
    {
        AwkUnaryOperator? unary = UnaryOperatorOf(Current.Kind);

        if (unary is null)
        {
            return ReadPrimary(isPrintContext);
        }

        index++;
        return new AwkUnary(unary.Value, ReadFieldOperand(isPrintContext));
    }

    private AwkExpression ReadParenthesised()
    {
        index++;
        List<AwkExpression> items = [ReadExpression(isPrintContext: false)];

        while (error is null && Current.Kind == AwkTokenKind.Comma)
        {
            index++;
            SkipNewlines();
            items.Add(ReadExpression(isPrintContext: false));
        }

        Expect(AwkTokenKind.RightParen, "')' expected");

        if (items.Count == 1)
        {
            return new AwkGrouping(items[0]);
        }

        if (Current.Kind != AwkTokenKind.In)
        {
            Fail("a parenthesised list is only an expression before 'in'");
        }

        return new AwkSubscriptList(items);
    }

    private AwkExpression ReadNameReference()
    {
        string name = Current.Text;
        index++;

        if (Current.Kind != AwkTokenKind.LeftBracket)
        {
            return new AwkVariable(name);
        }

        index++;
        List<AwkExpression> subscripts = ReadExpressionList(AwkTokenKind.RightBracket);
        Expect(AwkTokenKind.RightBracket, "']' expected");

        return new AwkArrayElement(name, subscripts);
    }

    private AwkExpression ReadCall(string name)
    {
        index++;
        Expect(AwkTokenKind.LeftParen, "'(' expected");
        List<AwkExpression> arguments = ReadExpressionList(AwkTokenKind.RightParen);
        Expect(AwkTokenKind.RightParen, "')' expected");

        return new AwkCall(name, arguments);
    }

    private AwkExpression ReadBuiltinCall(string name)
    {
        index++;

        if (!AwkBuiltins.TryResolve(name, out AwkBuiltin builtin))
        {
            Fail($"unknown builtin '{name}'");
            return new AwkNumberLiteral(0);
        }

        List<AwkExpression> arguments = [];

        if (Current.Kind == AwkTokenKind.LeftParen)
        {
            index++;
            arguments = ReadExpressionList(AwkTokenKind.RightParen);
            Expect(AwkTokenKind.RightParen, "')' expected");
        }
        else if (builtin != AwkBuiltin.Length)
        {
            Fail($"'{name}' needs its arguments");
        }

        (int minimum, int maximum) = AwkBuiltins.ArityOf(builtin);

        if (arguments.Count < minimum || arguments.Count > maximum)
        {
            Fail($"'{name}' takes {minimum} to {maximum} arguments, not {arguments.Count}");
        }

        return new AwkBuiltinCall(builtin, arguments);
    }

    private static AwkAssignOperator? AssignmentOperatorOf(AwkTokenKind kind) => kind switch
    {
        AwkTokenKind.Assign => AwkAssignOperator.Assign,
        AwkTokenKind.AddAssign => AwkAssignOperator.Add,
        AwkTokenKind.SubtractAssign => AwkAssignOperator.Subtract,
        AwkTokenKind.MultiplyAssign => AwkAssignOperator.Multiply,
        AwkTokenKind.DivideAssign => AwkAssignOperator.Divide,
        AwkTokenKind.ModuloAssign => AwkAssignOperator.Modulo,
        AwkTokenKind.PowerAssign => AwkAssignOperator.Power,
        _ => null,
    };

    // In an unparenthesised print argument list a top-level `>` is a redirection, so it must not be
    // read as a comparison. `>=` stays a comparison: POSIX only gives `>`, `>>` and `|` to the
    // redirection rule.
    private static AwkBinaryOperator? RelationalOperatorOf(AwkTokenKind kind, bool isPrintContext) => kind switch
    {
        AwkTokenKind.Less => AwkBinaryOperator.Less,
        AwkTokenKind.LessOrEqual => AwkBinaryOperator.LessOrEqual,
        AwkTokenKind.GreaterOrEqual => AwkBinaryOperator.GreaterOrEqual,
        AwkTokenKind.Equal => AwkBinaryOperator.Equal,
        AwkTokenKind.NotEqual => AwkBinaryOperator.NotEqual,
        AwkTokenKind.Greater when !isPrintContext => AwkBinaryOperator.Greater,
        _ => null,
    };

    private static bool StartsExpression(AwkTokenKind kind) => kind
        is AwkTokenKind.Number
        or AwkTokenKind.String
        or AwkTokenKind.Regex
        or AwkTokenKind.Name
        or AwkTokenKind.FunctionName
        or AwkTokenKind.Builtin
        or AwkTokenKind.Dollar
        or AwkTokenKind.Not
        or AwkTokenKind.Minus
        or AwkTokenKind.Plus
        or AwkTokenKind.LeftParen
        or AwkTokenKind.Increment
        or AwkTokenKind.Decrement;

    // Concatenation cannot start with `+` or `-`, which is the whole reason `1 - 1` subtracts and
    // `a " " -1` concatenates a with -1.
    private static bool StartsConcatenationOperand(AwkTokenKind kind) =>
        StartsExpression(kind) && kind is not (AwkTokenKind.Minus or AwkTokenKind.Plus);

    private static bool EndsPrintArguments(AwkTokenKind kind) => kind
        is AwkTokenKind.Newline
        or AwkTokenKind.Semicolon
        or AwkTokenKind.RightBrace
        or AwkTokenKind.EndOfInput
        or AwkTokenKind.Greater
        or AwkTokenKind.Append;

    private static bool IsAssignable(AwkExpression expression) =>
        expression is AwkVariable or AwkField or AwkArrayElement;

    private AwkToken Current => tokens[index];

    private void Expect(AwkTokenKind kind, string message)
    {
        if (Current.Kind != kind)
        {
            Fail(message);
            return;
        }

        index++;
    }

    private void SkipNewlines()
    {
        while (Current.Kind == AwkTokenKind.Newline)
        {
            index++;
        }
    }

    private void SkipTerminators()
    {
        while (Current.Kind is AwkTokenKind.Newline or AwkTokenKind.Semicolon)
        {
            index++;
        }
    }

    private void Fail(string message)
    {
        error ??= $"syntax error at source line {Current.Line}: {message}";
        index = tokens.Count - 1;
    }
}
