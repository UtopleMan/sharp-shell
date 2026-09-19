using Sharp.Shell.Execution;

namespace Sharp.Shell.Expansion;

// Integer arithmetic for $((...)). Recursive descent over the operators bash uses most; anything
// else is refused by name rather than guessed at.
public static class ArithmeticEvaluator
{
    public static bool TryEvaluate(string expression, ShellState state, out long value, out string error)
    {
        ArithmeticParser parser = new(expression, state);
        return parser.TryRun(out value, out error);
    }
}

internal sealed class ArithmeticParser(string expression, ShellState state)
{
    private int index;
    private string? failure;

    public bool TryRun(out long value, out string error)
    {
        value = ParseOr();
        SkipSpaces();

        if (failure is null && index < expression.Length)
        {
            failure = $"unexpected '{expression[index]}' in arithmetic expression";
        }

        error = failure ?? string.Empty;
        return failure is null;
    }

    private void SkipSpaces()
    {
        while (index < expression.Length && char.IsWhiteSpace(expression[index]))
        {
            index++;
        }
    }

    private bool TryTake(string token)
    {
        SkipSpaces();
        if (!expression.AsSpan(index).StartsWith(token, StringComparison.Ordinal))
        {
            return false;
        }

        index += token.Length;
        return true;
    }

    private long ParseOr()
    {
        long left = ParseAnd();
        while (TryTake("||"))
        {
            long right = ParseAnd();
            left = left != 0 || right != 0 ? 1 : 0;
        }

        return left;
    }

    private long ParseAnd()
    {
        long left = ParseComparison();
        while (TryTake("&&"))
        {
            long right = ParseComparison();
            left = left != 0 && right != 0 ? 1 : 0;
        }

        return left;
    }

    private long ParseComparison()
    {
        long left = ParseSum();

        while (true)
        {
            if (TryTake("=="))
            {
                left = left == ParseSum() ? 1 : 0;
                continue;
            }

            if (TryTake("!="))
            {
                left = left != ParseSum() ? 1 : 0;
                continue;
            }

            if (TryTake("<="))
            {
                left = left <= ParseSum() ? 1 : 0;
                continue;
            }

            if (TryTake(">="))
            {
                left = left >= ParseSum() ? 1 : 0;
                continue;
            }

            if (TryTake("<"))
            {
                left = left < ParseSum() ? 1 : 0;
                continue;
            }

            if (TryTake(">"))
            {
                left = left > ParseSum() ? 1 : 0;
                continue;
            }

            return left;
        }
    }

    private long ParseSum()
    {
        long left = ParseProduct();

        while (true)
        {
            if (TryTake("+"))
            {
                left += ParseProduct();
                continue;
            }

            if (TryTake("-"))
            {
                left -= ParseProduct();
                continue;
            }

            return left;
        }
    }

    private long ParseProduct()
    {
        long left = ParseUnary();

        while (true)
        {
            if (TryTake("*"))
            {
                left *= ParseUnary();
                continue;
            }

            if (TryTake("/"))
            {
                long divisor = ParseUnary();
                if (divisor == 0)
                {
                    failure ??= "division by 0";
                    return 0;
                }

                left /= divisor;
                continue;
            }

            if (TryTake("%"))
            {
                long divisor = ParseUnary();
                if (divisor == 0)
                {
                    failure ??= "division by 0";
                    return 0;
                }

                left %= divisor;
                continue;
            }

            return left;
        }
    }

    private long ParseUnary()
    {
        if (TryTake("-"))
        {
            return -ParseUnary();
        }

        if (TryTake("+"))
        {
            return ParseUnary();
        }

        if (TryTake("!"))
        {
            return ParseUnary() == 0 ? 1 : 0;
        }

        return ParsePrimary();
    }

    private long ParsePrimary()
    {
        SkipSpaces();

        if (TryTake("("))
        {
            long inner = ParseOr();
            if (!TryTake(")"))
            {
                failure ??= "missing ')' in arithmetic expression";
            }

            return inner;
        }

        if (index >= expression.Length)
        {
            failure ??= "unexpected end of arithmetic expression";
            return 0;
        }

        if (char.IsAsciiDigit(expression[index]))
        {
            int start = index;
            while (index < expression.Length && char.IsAsciiDigit(expression[index]))
            {
                index++;
            }

            return long.Parse(expression[start..index]);
        }

        if (expression[index] == '$')
        {
            index++;
            return ReadVariable();
        }

        if (char.IsAsciiLetter(expression[index]) || expression[index] == '_')
        {
            return ReadVariable();
        }

        failure ??= $"unexpected '{expression[index]}' in arithmetic expression";
        return 0;
    }

    private long ReadVariable()
    {
        int start = index;
        while (index < expression.Length && (char.IsAsciiLetterOrDigit(expression[index]) || expression[index] == '_'))
        {
            index++;
        }

        string name = expression[start..index];
        if (name.Length == 0)
        {
            failure ??= "expected a variable name in arithmetic expression";
            return 0;
        }

        return ValueOf(name);
    }

    // $1 inside $(( )) is the first positional parameter, not a variable called "1" — which is the
    // only reason `add() { echo $(($1 + $2)); }` gives an answer rather than zero.
    private long ValueOf(string name) =>
        long.TryParse(TextOf(name), out long value) ? value : 0;

    private string TextOf(string name)
    {
        if (name.All(char.IsAsciiDigit) && int.TryParse(name, out int position))
        {
            return position >= 1 && position <= state.PositionalArguments.Count
                ? state.PositionalArguments[position - 1]
                : string.Empty;
        }

        return state.Variables.TryGetValue(name, out string? text) ? text : string.Empty;
    }
}
