namespace Sharp.Shell.Commands.Awk;

// awk's command line, read once and used twice: by CheckFlags before anything runs, and by the applet
// when it does. A flag this sandbox cannot honour is reported as UnsupportedFlag so the whole command
// line escalates; malformed usage is an Error so awk reports it the way awk does.
internal sealed record AwkOptions(
    string? FieldSeparator,
    IReadOnlyList<string> Assignments,
    IReadOnlyList<string> ProgramFiles,
    IReadOnlyList<string> Operands,
    IReadOnlyList<int> OperandIndices,
    int ProgramIndex,
    string? Error,
    string? UnsupportedFlag)
{
    public bool HasProgramFile => ProgramFiles.Count > 0;

    // The program as far as it can be known without reading anything: null when -f is involved,
    // because classification does no I/O.
    public string? InlineProgram => HasProgramFile ? null : Operands.Count > 0 ? Operands[0] : null;

    public IReadOnlyList<string> InputOperands => HasProgramFile ? Operands : [.. Operands.Skip(1)];

    // An input operand of the form var=value is an assignment, not a file, so classification must
    // not report it as one. The rest are the files this invocation will open, at the positions the
    // command line gave them.
    public IReadOnlyList<int> InputFileIndices =>
        [
            .. Operands
                .Zip(OperandIndices)
                .Skip(HasProgramFile ? 0 : 1)
                .Where(operand => !IsAssignment(operand.First))
                .Select(operand => operand.Second),
        ];

    private static bool IsAssignment(string operand)
    {
        int equals = operand.IndexOf('=', StringComparison.Ordinal);

        return equals > 0 && operand[..equals].All(character => character == '_' || char.IsAsciiLetterOrDigit(character));
    }

    // Where the program sits in the argument list, so classification can insist that exactly that
    // word was literal before expansion. -1 when the program came from a file.
    public bool HasInlineProgramAt(out int position)
    {
        position = ProgramIndex;
        return position >= 0;
    }

    public static AwkOptions Parse(IReadOnlyList<string> arguments) => new AwkOptionReader(arguments).Read();
}

internal sealed class AwkOptionReader(IReadOnlyList<string> arguments)
{
    private const string ImpossibleFlagSuffix = " cannot run inside the sandbox";

    private readonly List<string> assignments = [];

    private readonly List<string> programFiles = [];

    private readonly List<string> operands = [];

    private readonly List<int> operandIndices = [];

    private string? fieldSeparator;

    private int programIndex = -1;

    private bool isPastOptions;

    private string? error;

    private string? unsupportedFlag;

    private int index;

    public AwkOptions Read()
    {
        for (index = 0; index < arguments.Count && unsupportedFlag is null; index++)
        {
            string argument = arguments[index];

            if (isPastOptions || !FlagReader.IsFlag(argument))
            {
                ReadOperand(argument);
                continue;
            }

            if (argument == "--")
            {
                isPastOptions = true;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                unsupportedFlag = $"{argument}{ImpossibleFlagSuffix}";
                continue;
            }

            ReadShortOptions(argument);
        }

        return new AwkOptions(
            fieldSeparator, assignments, programFiles, operands, operandIndices, programIndex, error, unsupportedFlag);
    }

    // The first operand is the program when no -f was given, and everything after it is input —
    // so once the program has been seen, no later word is a flag either.
    private void ReadOperand(string argument)
    {
        operands.Add(argument);
        operandIndices.Add(index);

        if (isPastOptions || programFiles.Count > 0 || operands.Count != 1)
        {
            return;
        }

        programIndex = index;
        isPastOptions = true;
    }

    private void ReadShortOptions(string argument)
    {
        for (int position = 1; position < argument.Length && unsupportedFlag is null; position++)
        {
            switch (argument[position])
            {
                case 'F': fieldSeparator = SeparatorFrom(TakeAttachedOrNext(argument, ref position, "-F")); break;
                case 'v': assignments.Add(TakeAttachedOrNext(argument, ref position, "-v")); break;
                case 'f': programFiles.Add(TakeAttachedOrNext(argument, ref position, "-f")); break;
                default: unsupportedFlag = $"-{argument[position]}"; break;
            }
        }
    }

    // `-F '\t'` is a tab because awk's escapes are processed here, and `-F t` is a tab as well,
    // which is historical practice one-true-awk keeps and `gawk --posix` does not.
    private static string SeparatorFrom(string value)
    {
        string decoded = AwkEscapes.Decode(value);

        return decoded == "t" ? "\t" : decoded;
    }

    private string TakeAttachedOrNext(string argument, ref int position, string flag)
    {
        string attached = argument[(position + 1)..];
        position = argument.Length;

        return attached.Length > 0 ? attached : TakeValue(flag);
    }

    private string TakeValue(string flag)
    {
        if (index + 1 >= arguments.Count)
        {
            error ??= $"option '{flag}' requires an argument";
            return string.Empty;
        }

        index++;
        return arguments[index];
    }
}
