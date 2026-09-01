using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Counts characters, not bytes: this shell's stream medium is text. For ASCII input, which is what
// the corpus and real agent commands overwhelmingly use, the two agree.
//
// Counts are unpadded, GNU-style, and named per operand, because `wc -l *.cs | sort -rn` is how a
// model asks which file is largest. A single concatenated total answers a different question.
public sealed class WcApplet : IApplet
{
    public string Name => "wc";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-l", "-w", "-c", "-m"];

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-l", "-w", "-c", "-m");

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        AppletRun run = new() { Output = TextStream.Empty };

        run.Output = operands.Count == 0
            ? CountInput(context)
            : CountOperands(context, operands, run);

        return run;
    }

    private static IEnumerable<string> CountInput(AppletContext context)
    {
        yield return $"{Selected(context.Arguments, Count(context.Input))}\n";
    }

    private static IEnumerable<string> CountOperands(
        AppletContext context,
        IReadOnlyList<string> operands,
        AppletRun run)
    {
        Counts totals = new(0, 0, 0);

        foreach (string operand in operands)
        {
            string absolute = context.State.Resolve(operand);
            if (!context.State.IsInsideRoot(absolute) || !File.Exists(absolute))
            {
                context.WriteError($"wc: {operand}: No such file or directory\n");
                run.ExitCode = 1;
                continue;
            }

            Counts counts = Count(FileChunks.Read(absolute));
            totals = totals.Plus(counts);
            yield return $"{Selected(context.Arguments, counts)} {operand}\n";
        }

        if (operands.Count > 1)
        {
            yield return $"{Selected(context.Arguments, totals)} total\n";
        }
    }

    private static Counts Count(IEnumerable<string> input)
    {
        int lines = 0;
        int words = 0;
        int characters = 0;
        bool inWord = false;

        foreach (string chunk in input)
        {
            foreach (char character in chunk)
            {
                characters++;
                if (character == '\n')
                {
                    lines++;
                }

                if (char.IsWhiteSpace(character))
                {
                    inWord = false;
                    continue;
                }

                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
        }

        return new Counts(lines, words, characters);
    }

    private static string Selected(IReadOnlyList<string> arguments, Counts counts)
    {
        List<string> selected = [];
        if (arguments.Contains("-l", StringComparer.Ordinal))
        {
            selected.Add(counts.Lines.ToString());
        }

        if (arguments.Contains("-w", StringComparer.Ordinal))
        {
            selected.Add(counts.Words.ToString());
        }

        if (arguments.Contains("-c", StringComparer.Ordinal) || arguments.Contains("-m", StringComparer.Ordinal))
        {
            selected.Add(counts.Characters.ToString());
        }

        return selected.Count > 0
            ? string.Join(' ', selected)
            : $"{counts.Lines} {counts.Words} {counts.Characters}";
    }

    private readonly record struct Counts(int Lines, int Words, int Characters)
    {
        public Counts Plus(Counts other) =>
            new(Lines + other.Lines, Words + other.Words, Characters + other.Characters);
    }
}
