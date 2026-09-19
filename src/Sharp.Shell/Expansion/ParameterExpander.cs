using System.Text;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Expansion;

public sealed record ParameterResult(string Value, string? UnsupportedReason, string? ErrorMessage)
{
    public static ParameterResult Ok(string value) => new(value, null, null);

    public static ParameterResult Unsupported(string reason) => new(string.Empty, reason, null);

    public static ParameterResult Failed(string message) => new(string.Empty, null, message);
}

// The inside of a $name or ${...}. Every form bash offers that this shell does not implement is
// refused by name — an approximated ${x@Q} would produce wrong text that looks right.
public static class ParameterExpander
{
    public static ParameterResult Expand(string expression, ShellState state, Func<string, string> expandWord)
    {
        if (expression.Length == 0)
        {
            return ParameterResult.Ok(string.Empty);
        }

        if (TryExpandSpecial(expression, state, out ParameterResult? special))
        {
            return special!;
        }

        if (expression[0] == '#')
        {
            string name = expression[1..];
            return IsName(name)
                ? ParameterResult.Ok(Lookup(state, name).Length.ToString())
                : ParameterResult.Unsupported($"${{{expression}}} is not supported");
        }

        int nameLength = NameLength(expression);
        if (nameLength == 0)
        {
            return ParameterResult.Unsupported($"${{{expression}}} is not supported");
        }

        string variable = expression[..nameLength];
        string remainder = expression[nameLength..];

        return remainder.Length == 0
            ? ParameterResult.Ok(Lookup(state, variable))
            : ApplyOperator(variable, remainder, state, expandWord, expression);
    }

    private static bool TryExpandSpecial(string expression, ShellState state, out ParameterResult? result)
    {
        result = expression switch
        {
            "?" => ParameterResult.Ok(state.LastExitCode.ToString()),
            "#" => ParameterResult.Ok(state.PositionalArguments.Count.ToString()),
            "0" => ParameterResult.Ok("duetui-shell"),
            "$" => ParameterResult.Ok(SyntheticProcessId),
            "!" => ParameterResult.Ok(string.Empty),
            "@" or "*" => AllPositional(state),
            _ => expression.All(char.IsAsciiDigit) ? Positional(expression, state) : null,
        };

        return result is not null;
    }

    // $$ answers a fixed number rather than a real process id: this shell is not a process, and a
    // value that moved between runs would make every differential comparison unreproducible. Hosts
    // that need uniqueness must not build temp-file names from it.
    private const string SyntheticProcessId = "1";

    private static ParameterResult Positional(string digits, ShellState state)
    {
        int position = int.Parse(digits);

        return ParameterResult.Ok(position >= 1 && position <= state.PositionalArguments.Count
            ? state.PositionalArguments[position - 1]
            : string.Empty);
    }

    // "$@" is N fields in bash and one field here, because an expansion answers with one string.
    // The two agree for every argument without whitespace in it, so the narrow case where they
    // would differ is refused rather than answered wrongly.
    private static ParameterResult AllPositional(ShellState state)
    {
        if (state.PositionalArguments.Any(argument => argument.Any(char.IsWhiteSpace)))
        {
            return ParameterResult.Unsupported("$@ is not supported when an argument contains whitespace");
        }

        return ParameterResult.Ok(string.Join(' ', state.PositionalArguments));
    }

    private static ParameterResult ApplyOperator(
        string variable,
        string remainder,
        ShellState state,
        Func<string, string> expandWord,
        string expression)
    {
        bool colon = remainder[0] == ':';
        string body = colon ? remainder[1..] : remainder;

        if (body.Length == 0)
        {
            return ParameterResult.Unsupported($"${{{expression}}} is not supported");
        }

        bool isSet = state.Variables.TryGetValue(variable, out string? existing);
        string value = existing ?? string.Empty;
        bool present = colon ? isSet && value.Length > 0 : isSet;

        switch (body[0])
        {
            case '-':
                return ParameterResult.Ok(present ? value : expandWord(body[1..]));

            case '=':
                if (present)
                {
                    return ParameterResult.Ok(value);
                }

                string assigned = expandWord(body[1..]);
                state.Variables[variable] = assigned;
                return ParameterResult.Ok(assigned);

            case '+':
                return ParameterResult.Ok(present ? expandWord(body[1..]) : string.Empty);

            case '?':
                return present
                    ? ParameterResult.Ok(value)
                    : ParameterResult.Failed($"{variable}: {expandWord(body[1..])}");

            case '#':
                return ParameterResult.Ok(TrimPrefix(value, body, expandWord));

            case '%':
                return ParameterResult.Ok(TrimSuffix(value, body, expandWord));

            case '/':
                return Replace(value, body, expandWord);

            default:
                return ParameterResult.Unsupported($"${{{expression}}} is not supported");
        }
    }

    private static string TrimPrefix(string value, string body, Func<string, string> expandWord)
    {
        bool longest = body.Length > 1 && body[1] == '#';
        string pattern = expandWord(body[(longest ? 2 : 1)..]);
        int length = PatternMatcher.MatchingPrefixLength(value, pattern, longest);

        return length <= 0 ? value : value[length..];
    }

    private static string TrimSuffix(string value, string body, Func<string, string> expandWord)
    {
        bool longest = body.Length > 1 && body[1] == '%';
        string pattern = expandWord(body[(longest ? 2 : 1)..]);
        int start = PatternMatcher.MatchingSuffixStart(value, pattern, longest);

        return start < 0 || start == value.Length ? value : value[..start];
    }

    private static ParameterResult Replace(string value, string body, Func<string, string> expandWord)
    {
        bool all = body.Length > 1 && body[1] == '/';
        string rest = body[(all ? 2 : 1)..];
        int separator = IndexOfUnescaped(rest, '/');
        string pattern = expandWord(separator < 0 ? rest : rest[..separator]);
        string replacement = separator < 0 ? string.Empty : expandWord(rest[(separator + 1)..]);

        if (pattern.Length == 0)
        {
            return ParameterResult.Ok(value);
        }

        return ParameterResult.Ok(ReplaceMatches(value, pattern, replacement, all));
    }

    private static string ReplaceMatches(string value, string pattern, string replacement, bool all)
    {
        StringBuilder output = new();
        int index = 0;

        while (index <= value.Length)
        {
            int matched = LongestMatchAt(value, index, pattern);

            if (matched < 0)
            {
                if (index < value.Length)
                {
                    output.Append(value[index]);
                }

                index++;
                continue;
            }

            output.Append(replacement);
            index += Math.Max(matched, 1);

            if (all)
            {
                continue;
            }

            output.Append(value[Math.Min(index, value.Length)..]);
            return output.ToString();
        }

        return output.ToString();
    }

    private static int LongestMatchAt(string value, int start, string pattern)
    {
        if (start > value.Length)
        {
            return -1;
        }

        for (int length = value.Length - start; length >= 1; length--)
        {
            if (PatternMatcher.Matches(value.Substring(start, length), pattern))
            {
                return length;
            }
        }

        return -1;
    }

    private static int IndexOfUnescaped(string text, char target)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }

            if (text[index] == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Lookup(ShellState state, string name) =>
        state.Variables.TryGetValue(name, out string? value) ? value : string.Empty;

    private static int NameLength(string expression)
    {
        if (!char.IsAsciiLetter(expression[0]) && expression[0] != '_')
        {
            return 0;
        }

        int length = 1;
        while (length < expression.Length && (char.IsAsciiLetterOrDigit(expression[length]) || expression[length] == '_'))
        {
            length++;
        }

        return length;
    }

    private static bool IsName(string candidate) => candidate.Length > 0 && NameLength(candidate) == candidate.Length;
}
