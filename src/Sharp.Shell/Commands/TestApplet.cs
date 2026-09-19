using Sharp.Shell.Execution;
using Sharp.Shell.Expansion;
using Sharp.Shell.Text;

namespace Sharp.Shell.Commands;

// `test`, `[` and `[[` are the same applet under three names. Every operator it does not implement
// is refused up front, so a condition is never silently evaluated the wrong way.
//
// `[[` differs in two places, and only two: its `==` and `!=` match a glob pattern rather than
// comparing text, and it has `=~` for a regular expression. Everything else — the file tests, the
// numeric comparisons, the boolean operators — is the same condition language, so it is the same
// parser rather than a second one that would drift.
public sealed class TestApplet(string name) : IApplet
{
    private static readonly string[] UnaryOperators = ["-e", "-f", "-d", "-r", "-w", "-x", "-s", "-z", "-n"];
    private static readonly string[] BinaryOperators = ["=", "!=", "-eq", "-ne", "-lt", "-le", "-gt", "-ge"];

    public string Name => name;

    public bool Mutates => false;

    private string? Closer => name switch
    {
        "[" => "]",
        "[[" => "]]",
        _ => null,
    };

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (!FlagReader.IsFlag(argument) || UnaryOperators.Contains(argument, StringComparer.Ordinal)
                || BinaryOperators.Contains(argument, StringComparer.Ordinal) || argument is "-a" or "-o")
            {
                continue;
            }

            return FlagSupport.Reject(argument);
        }

        return FlagSupport.Supported;
    }

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> arguments = context.Arguments;

        if (Closer is { } closer)
        {
            if (arguments.Count == 0 || arguments[^1] != closer)
            {
                context.WriteError($"{name}: missing '{closer}'\n");
                return AppletRun.Failed(2);
            }

            arguments = [.. arguments.Take(arguments.Count - 1)];
        }

        ConditionParser parser = new(arguments, context.State, name == "[[");
        bool value = parser.ParseExpression();

        if (parser.Error is not null)
        {
            context.WriteError($"{name}: {parser.Error}\n");
            return AppletRun.Failed(2);
        }

        return AppletRun.Failed(value ? 0 : 1);
    }
}

internal sealed class ConditionParser(IReadOnlyList<string> arguments, ShellState state, bool matchesPatterns)
{
    private int index;

    public string? Error { get; private set; }

    public bool ParseExpression()
    {
        bool value = ParseBooleanChain();

        if (index < arguments.Count && Error is null)
        {
            Error = $"unexpected argument '{arguments[index]}'";
        }

        return value;
    }

    // `-a` / `-o` inside [ ] and `&&` / `||` inside [[ ]] are the same two operators spelled twice.
    private bool ParseBooleanChain()
    {
        bool left = ParseTerm();

        while (index < arguments.Count && IsConjunction(arguments[index]))
        {
            bool isAnd = arguments[index] is "-a" or "&&";
            index++;
            bool right = ParseTerm();
            left = isAnd ? left && right : left || right;
        }

        return left;
    }

    private static bool IsConjunction(string candidate) => candidate is "-a" or "-o" or "&&" or "||";

    private bool ParseTerm()
    {
        if (index >= arguments.Count)
        {
            return false;
        }

        if (arguments[index] == "!")
        {
            index++;
            return !ParseTerm();
        }

        if (matchesPatterns && arguments[index] == "(")
        {
            return ParseGroup();
        }

        if (index + 2 < arguments.Count + 1 && index + 1 < arguments.Count && IsBinary(arguments[index + 1]))
        {
            return ParseBinary();
        }

        if (IsUnary(arguments[index]))
        {
            return ParseUnary();
        }

        string operand = arguments[index];
        index++;
        return operand.Length > 0;
    }

    private static bool IsUnary(string candidate) =>
        candidate is "-e" or "-f" or "-d" or "-r" or "-w" or "-x" or "-s" or "-z" or "-n";

    private static bool IsBinary(string candidate) =>
        candidate is "=" or "==" or "!=" or "=~" or "<" or ">"
            or "-eq" or "-ne" or "-lt" or "-le" or "-gt" or "-ge";

    private bool ParseGroup()
    {
        index++;
        bool value = ParseBooleanChain();

        if (index >= arguments.Count || arguments[index] != ")")
        {
            Error = "expected ')'";
            return false;
        }

        index++;
        return value;
    }

    private bool ParseUnary()
    {
        string operatorName = arguments[index];
        index++;

        if (index >= arguments.Count)
        {
            Error = $"{operatorName}: operand expected";
            return false;
        }

        string operand = arguments[index];
        index++;

        if (operatorName == "-z")
        {
            return operand.Length == 0;
        }

        if (operatorName == "-n")
        {
            return operand.Length > 0;
        }

        return CheckPath(operatorName, operand);
    }

    private bool CheckPath(string operatorName, string operand)
    {
        string absolute = state.Resolve(operand);
        if (!state.IsInsideRoot(absolute))
        {
            return false;
        }

        return operatorName switch
        {
            "-e" => File.Exists(absolute) || Directory.Exists(absolute),
            "-f" => File.Exists(absolute),
            "-d" => Directory.Exists(absolute),
            "-r" or "-w" or "-x" => File.Exists(absolute) || Directory.Exists(absolute),
            "-s" => File.Exists(absolute) && new FileInfo(absolute).Length > 0,
            _ => false,
        };
    }

    private bool ParseBinary()
    {
        string left = arguments[index];
        string operatorName = arguments[index + 1];
        index += 2;

        if (index >= arguments.Count)
        {
            Error = $"{operatorName}: operand expected";
            return false;
        }

        string right = arguments[index];
        index++;

        if (operatorName is "=" or "==")
        {
            return Equal(left, right);
        }

        if (operatorName == "!=")
        {
            return !Equal(left, right);
        }

        if (operatorName == "=~")
        {
            return MatchesRegex(left, right);
        }

        if (operatorName is "<" or ">")
        {
            int order = string.CompareOrdinal(left, right);
            return operatorName == "<" ? order < 0 : order > 0;
        }

        return CompareNumbers(left, operatorName, right);
    }

    // Inside [[ ]] the right operand of == is a glob pattern, which is the one place where using
    // `test`'s rule would silently give the wrong answer: `[[ $f == *.cs ]]` is a match, not a
    // comparison against the four characters `*.cs`.
    private bool Equal(string left, string right) =>
        matchesPatterns
            ? PatternMatcher.Matches(left, right)
            : string.Equals(left, right, StringComparison.Ordinal);

    private bool MatchesRegex(string subject, string pattern)
    {
        RegexTranslation translation = PosixRegexTranslator.Translate(
            pattern, RegexDialect.ExtendedPosix, allowsGnuExtensions: false, usesMatchExtent: false);

        if (!translation.IsTranslated)
        {
            Error = $"=~: {translation.RefusalReason}";
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(subject, translation.Pattern!);
    }

    private bool CompareNumbers(string left, string operatorName, string right)
    {
        if (!long.TryParse(left, out long first) || !long.TryParse(right, out long second))
        {
            Error = $"{operatorName}: integer expression expected";
            return false;
        }

        return operatorName switch
        {
            "-eq" => first == second,
            "-ne" => first != second,
            "-lt" => first < second,
            "-le" => first <= second,
            "-gt" => first > second,
            _ => first >= second,
        };
    }
}
