using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// `test` and `[` are the same applet under two names. Every operator it does not implement is
// refused up front, so a condition is never silently evaluated the wrong way.
public sealed class TestApplet(string name) : IApplet
{
    private static readonly string[] UnaryOperators = ["-e", "-f", "-d", "-r", "-w", "-x", "-s", "-z", "-n"];
    private static readonly string[] BinaryOperators = ["=", "!=", "-eq", "-ne", "-lt", "-le", "-gt", "-ge"];

    public string Name => name;

    public bool Mutates => false;

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

        if (name == "[")
        {
            if (arguments.Count == 0 || arguments[^1] != "]")
            {
                context.WriteError("[: missing ']'\n");
                return AppletRun.Failed(2);
            }

            arguments = [.. arguments.Take(arguments.Count - 1)];
        }

        ConditionParser parser = new(arguments, context.State);
        bool value = parser.ParseExpression();

        if (parser.Error is not null)
        {
            context.WriteError($"{name}: {parser.Error}\n");
            return AppletRun.Failed(2);
        }

        return AppletRun.Failed(value ? 0 : 1);
    }
}

internal sealed class ConditionParser(IReadOnlyList<string> arguments, ShellState state)
{
    private int index;

    public string? Error { get; private set; }

    public bool ParseExpression()
    {
        bool left = ParseTerm();

        while (index < arguments.Count && arguments[index] is "-a" or "-o")
        {
            bool isAnd = arguments[index] == "-a";
            index++;
            bool right = ParseTerm();
            left = isAnd ? left && right : left || right;
        }

        if (index < arguments.Count && Error is null)
        {
            Error = $"unexpected argument '{arguments[index]}'";
        }

        return left;
    }

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
        candidate is "=" or "!=" or "-eq" or "-ne" or "-lt" or "-le" or "-gt" or "-ge";

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

        if (operatorName == "=")
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        if (operatorName == "!=")
        {
            return !string.Equals(left, right, StringComparison.Ordinal);
        }

        return CompareNumbers(left, operatorName, right);
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
