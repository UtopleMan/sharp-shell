using System.Text;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class CutApplet : IApplet
{
    public string Name => "cut";

    public bool Mutates => false;

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Reading(FlagReader.PositionsOfOperands(arguments, "-d", "-f", "-c"));

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-d", "-f", "-c", "-s");

    public AppletRun Run(AppletContext context)
    {
        string? fields = ValueFor(context.Arguments, "-f");
        string? characters = ValueFor(context.Arguments, "-c");

        if (fields is null && characters is null)
        {
            context.WriteError("cut: you must specify a list of bytes, characters, or fields\n");
            return AppletRun.Failed(2);
        }

        AppletRun run = new();
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments, "-d", "-f", "-c");
        IEnumerable<string> input = operands.Count == 0
            ? context.Input
            : FileOperands.Read(operands, context, run, "cut");

        string delimiter = ValueFor(context.Arguments, "-d") ?? "\t";
        Range selection = ParseRange(fields ?? characters!);

        run.Output = TextStream.FromLines(
            TextStream.Lines(input).Select(line => fields is not null
                ? SelectFields(line, delimiter, selection)
                : SelectCharacters(line, selection)));

        return run;
    }

    private static string SelectFields(string line, string delimiter, Range selection)
    {
        string[] pieces = line.Split(delimiter);
        return string.Join(delimiter, Slice(pieces, selection));
    }

    private static string SelectCharacters(string line, Range selection)
    {
        StringBuilder output = new();
        for (int position = selection.First; position <= Math.Min(selection.Last, line.Length); position++)
        {
            output.Append(line[position - 1]);
        }

        return output.ToString();
    }

    private static IEnumerable<string> Slice(string[] pieces, Range selection)
    {
        for (int position = selection.First; position <= Math.Min(selection.Last, pieces.Length); position++)
        {
            yield return pieces[position - 1];
        }
    }

    private static Range ParseRange(string specification)
    {
        int dash = specification.IndexOf('-', StringComparison.Ordinal);

        if (dash < 0)
        {
            int single = int.TryParse(specification, out int value) ? value : 1;
            return new Range(single, single);
        }

        int first = int.TryParse(specification[..dash], out int start) ? start : 1;
        int last = int.TryParse(specification[(dash + 1)..], out int end) ? end : int.MaxValue;

        return new Range(first, last);
    }

    private static string? ValueFor(IReadOnlyList<string> arguments, string flag)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == flag)
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private readonly record struct Range(int First, int Last);
}
