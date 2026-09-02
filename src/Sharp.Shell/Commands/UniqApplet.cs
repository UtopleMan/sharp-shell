using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Collapses *adjacent* duplicates, as uniq does — the input is expected to be sorted already.
public sealed class UniqApplet : IApplet
{
    public string Name => "uniq";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-c", "-d", "-u"];

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Reading(FlagReader.PositionsOfOperands(arguments));

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-c", "-d", "-u");

    public AppletRun Run(AppletContext context)
    {
        AppletRun run = new();
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        IEnumerable<string> input = operands.Count == 0
            ? context.Input
            : FileOperands.Read(operands, context, run, "uniq");

        bool counts = context.Arguments.Contains("-c", StringComparer.Ordinal);
        bool duplicatesOnly = context.Arguments.Contains("-d", StringComparer.Ordinal);
        bool uniqueOnly = context.Arguments.Contains("-u", StringComparer.Ordinal);

        run.Output = TextStream.FromLines(Collapse(TextStream.Lines(input), counts, duplicatesOnly, uniqueOnly));
        return run;
    }

    private static IEnumerable<string> Collapse(
        IEnumerable<string> lines,
        bool counts,
        bool duplicatesOnly,
        bool uniqueOnly)
    {
        string? previous = null;
        int repeats = 0;

        foreach (string line in lines)
        {
            if (previous is not null && string.Equals(previous, line, StringComparison.Ordinal))
            {
                repeats++;
                continue;
            }

            if (previous is not null && Keeps(repeats, duplicatesOnly, uniqueOnly))
            {
                yield return Render(previous, repeats, counts);
            }

            previous = line;
            repeats = 1;
        }

        if (previous is not null && Keeps(repeats, duplicatesOnly, uniqueOnly))
        {
            yield return Render(previous, repeats, counts);
        }
    }

    private static bool Keeps(int repeats, bool duplicatesOnly, bool uniqueOnly)
    {
        if (duplicatesOnly)
        {
            return repeats > 1;
        }

        return !uniqueOnly || repeats == 1;
    }

    private static string Render(string line, int repeats, bool counts) => counts ? $"{repeats} {line}" : line;
}
