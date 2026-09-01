using System.Text;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class TrApplet : IApplet
{
    public string Name => "tr";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-d", "-s"];

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-d", "-s");

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        bool deletes = context.Arguments.Contains("-d", StringComparer.Ordinal);
        bool squeezes = context.Arguments.Contains("-s", StringComparer.Ordinal);

        if (operands.Count == 0)
        {
            context.WriteError("tr: usage: tr [-ds] set1 [set2]\n");
            return AppletRun.Failed(2);
        }

        string first = ExpandSet(operands[0]);
        string second = operands.Count > 1 ? ExpandSet(operands[1]) : string.Empty;

        return new AppletRun { Output = Translate(context.Input, first, second, deletes, squeezes) };
    }

    private static IEnumerable<string> Translate(
        IEnumerable<string> input,
        string first,
        string second,
        bool deletes,
        bool squeezes)
    {
        char? previous = null;

        foreach (string chunk in input)
        {
            StringBuilder output = new(chunk.Length);

            foreach (char character in chunk)
            {
                int position = first.IndexOf(character, StringComparison.Ordinal);

                if (deletes && position >= 0)
                {
                    continue;
                }

                char translated = position >= 0 && second.Length > 0
                    ? second[Math.Min(position, second.Length - 1)]
                    : character;

                if (squeezes && position >= 0 && previous == translated)
                {
                    continue;
                }

                previous = translated;
                output.Append(translated);
            }

            yield return output.ToString();
        }
    }

    // "a-z" is a range; everything else is a literal member of the set.
    private static string ExpandSet(string specification)
    {
        StringBuilder set = new();

        for (int index = 0; index < specification.Length; index++)
        {
            if (index + 2 < specification.Length && specification[index + 1] == '-')
            {
                for (char character = specification[index]; character <= specification[index + 2]; character++)
                {
                    set.Append(character);
                }

                index += 2;
                continue;
            }

            set.Append(specification[index]);
        }

        return set.ToString();
    }
}
