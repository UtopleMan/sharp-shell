using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Unlike head, tail has to see the whole stream before it knows what to emit, so it buffers the
// last N lines rather than the whole input.
public sealed class TailApplet : IApplet
{
    private const int DefaultLines = 10;

    public string Name => "tail";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-n", "-c"];

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (!FlagReader.IsFlag(argument) || argument is "-n" or "-c" || argument.Skip(1).All(char.IsAsciiDigit))
            {
                continue;
            }

            return FlagSupport.Reject(argument);
        }

        return FlagSupport.Supported;
    }

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments, "-n", "-c");
        AppletRun run = new();
        IEnumerable<string> input = operands.Count == 0
            ? context.Input
            : FileOperands.Read(operands, context, run, "tail");

        run.Output = TakeCharacters(context.Arguments) is { } characters
            ? TextStream.FromText(LastCharacters(input, characters))
            : TextStream.FromLines(LastLines(input, TakeLines(context.Arguments)));

        return run;
    }

    private static IEnumerable<string> LastLines(IEnumerable<string> input, int wanted)
    {
        Queue<string> kept = new();

        foreach (string line in TextStream.Lines(input))
        {
            kept.Enqueue(line);
            if (kept.Count > wanted)
            {
                kept.Dequeue();
            }
        }

        return kept;
    }

    private static string LastCharacters(IEnumerable<string> input, int wanted)
    {
        string all = TextStream.Collect(input);
        return all.Length <= wanted ? all : all[^wanted..];
    }

    private static int TakeLines(IReadOnlyList<string> arguments) => ValueFor(arguments, "-n") ?? DefaultLines;

    private static int? TakeCharacters(IReadOnlyList<string> arguments) => ValueFor(arguments, "-c");

    private static int? ValueFor(IReadOnlyList<string> arguments, string flag)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == flag && index + 1 < arguments.Count && int.TryParse(arguments[index + 1], out int value))
            {
                return value;
            }

            if (flag == "-n" && FlagReader.IsFlag(arguments[index]) && arguments[index].Skip(1).All(char.IsAsciiDigit))
            {
                return int.Parse(arguments[index][1..]);
            }
        }

        return null;
    }
}
