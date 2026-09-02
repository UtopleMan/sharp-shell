using System.Globalization;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// One of the few applets that cannot stream: ordering needs the whole input before the first line
// can be emitted.
//
// Two comparisons, not one. The key comparison is what -u calls equal; the ordering comparison adds
// the last-resort byte compare sort applies to lines whose keys tie. Deduplicating on the ordering
// comparison would keep lines that -u is meant to collapse.
public sealed class SortApplet : IApplet
{
    public string Name => "sort";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-r", "-n", "-u"];

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Reading(FlagReader.PositionsOfOperands(arguments, "-k", "-t"));

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-r", "-n", "-u", "-k", "-t");

    public AppletRun Run(AppletContext context)
    {
        AppletRun run = new();
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments, "-k", "-t");
        IEnumerable<string> input = operands.Count == 0
            ? context.Input
            : FileOperands.Read(operands, context, run, "sort");

        bool deduplicates = context.Arguments.Contains("-u", StringComparer.Ordinal);
        Comparison<string> keys = KeyComparison(context.Arguments);
        Comparison<string> ordering = Reversing(
            deduplicates ? keys : WithLastResort(keys), context.Arguments);

        List<string> lines =
        [
            .. TextStream.Lines(input).OrderBy(line => line, Comparer<string>.Create(ordering)),
        ];

        run.Output = TextStream.FromLines(deduplicates ? Deduplicate(lines, keys) : lines);
        return run;
    }

    private static Comparison<string> KeyComparison(IReadOnlyList<string> arguments)
    {
        bool numeric = arguments.Contains("-n", StringComparer.Ordinal);
        string? separator = ValueFor(arguments, "-t");
        int? field = FieldFor(arguments);

        return (left, right) =>
        {
            string first = Key(left, field, separator);
            string second = Key(right, field, separator);

            return numeric ? CompareNumerically(first, second) : string.CompareOrdinal(first, second);
        };
    }

    private static Comparison<string> WithLastResort(Comparison<string> keys) =>
        (left, right) =>
        {
            int order = keys(left, right);
            return order != 0 ? order : string.CompareOrdinal(left, right);
        };

    private static Comparison<string> Reversing(
        Comparison<string> comparison,
        IReadOnlyList<string> arguments) =>
        arguments.Contains("-r", StringComparer.Ordinal)
            ? (left, right) => -comparison(left, right)
            : comparison;

    private static List<string> Deduplicate(List<string> lines, Comparison<string> comparison)
    {
        List<string> unique = [];
        foreach (string line in lines)
        {
            if (unique.Count == 0 || comparison(unique[^1], line) != 0)
            {
                unique.Add(line);
            }
        }

        return unique;
    }

    private static int CompareNumerically(string left, string right) =>
        LeadingNumber(left).CompareTo(LeadingNumber(right));

    // A numeric key is the leading number and nothing else: "960 SedMachine.cs" is 960, and a line
    // that starts with no number at all sorts as zero rather than falling back to a byte compare.
    private static double LeadingNumber(string text)
    {
        ReadOnlySpan<char> trimmed = text.AsSpan().TrimStart();
        int length = trimmed.Length > 0 && (trimmed[0] == '-' || trimmed[0] == '+') ? 1 : 0;

        while (length < trimmed.Length && char.IsAsciiDigit(trimmed[length]))
        {
            length++;
        }

        if (length < trimmed.Length && trimmed[length] == '.')
        {
            length++;
            while (length < trimmed.Length && char.IsAsciiDigit(trimmed[length]))
            {
                length++;
            }
        }

        return double.TryParse(
            trimmed[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;
    }

    private static string Key(string line, int? field, string? separator)
    {
        if (field is null)
        {
            return line;
        }

        string[] pieces = separator is null
            ? line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            : line.Split(separator);

        return field.Value - 1 < pieces.Length ? pieces[field.Value - 1] : string.Empty;
    }

    private static int? FieldFor(IReadOnlyList<string> arguments)
    {
        string? key = ValueFor(arguments, "-k");
        if (key is null)
        {
            return null;
        }

        string head = key.Split(',')[0];
        return int.TryParse(head, out int field) ? field : null;
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
}
