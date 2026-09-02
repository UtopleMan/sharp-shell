using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Stops enumerating once it has what it asked for, which is what stops the producer upstream.
public sealed class HeadApplet : IApplet
{
    private const int DefaultLines = 10;

    public string Name => "head";

    public bool Mutates => false;

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Reading(FlagReader.PositionsOfOperands(arguments, "-n"));

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (!FlagReader.IsFlag(argument) || argument == "-n" || IsCountShorthand(argument))
            {
                continue;
            }

            return FlagSupport.Reject(argument);
        }

        return FlagSupport.Supported;
    }

    public AppletRun Run(AppletContext context)
    {
        int wanted = ReadCount(context.Arguments);
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments, "-n");
        AppletRun run = new() { Output = TextStream.Empty };

        IEnumerable<string> input = operands.Count == 0
            ? context.Input
            : FileOperands.Read(operands, context, run, "head");

        run.Output = TextStream.FromLines(TextStream.Lines(input).Take(wanted));
        return run;
    }

    private static bool IsCountShorthand(string argument) => argument.Skip(1).All(char.IsAsciiDigit);

    private static int ReadCount(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "-n" && index + 1 < arguments.Count && int.TryParse(arguments[index + 1], out int explicitCount))
            {
                return explicitCount;
            }

            if (FlagReader.IsFlag(arguments[index]) && IsCountShorthand(arguments[index]))
            {
                return int.Parse(arguments[index][1..]);
            }
        }

        return DefaultLines;
    }
}
