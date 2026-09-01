using System.Text;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// -l is deliberately refused rather than approximated. Its columns carry permissions, owner, size
// and a locale-shaped date; inventing a plausible-looking layout is exactly the invisible wrong
// output Rule 1 exists to prevent, so `ls -l` escalates to a real shell instead.
public sealed class LsApplet : IApplet
{
    public string Name => "ls";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-a", "-A", "-1", "-R", "-d"];

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-a", "-A", "-1", "-R", "-d");

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        bool showsHidden = context.Arguments.Contains("-a", StringComparer.Ordinal)
            || context.Arguments.Contains("-A", StringComparer.Ordinal);
        bool recurses = context.Arguments.Contains("-R", StringComparer.Ordinal);

        AppletRun run = new();
        run.Output = List(operands.Count == 0 ? ["."] : operands, showsHidden, recurses, context, run);
        return run;
    }

    private static IEnumerable<string> List(
        IReadOnlyList<string> operands,
        bool showsHidden,
        bool recurses,
        AppletContext context,
        AppletRun run)
    {
        foreach (string operand in operands)
        {
            string absolute = context.State.Resolve(operand);

            if (!context.State.IsInsideRoot(absolute))
            {
                context.WriteError($"ls: {operand}: outside the workspace\n");
                run.ExitCode = 2;
                continue;
            }

            if (File.Exists(absolute))
            {
                yield return $"{operand}\n";
                continue;
            }

            if (!Directory.Exists(absolute))
            {
                context.WriteError($"ls: {operand}: No such file or directory\n");
                run.ExitCode = 2;
                continue;
            }

            foreach (string chunk in ListDirectory(absolute, operand, showsHidden, recurses, first: true))
            {
                yield return chunk;
            }
        }
    }

    // GNU's -R layout: each directory gets a "path:" header and a blank line between blocks.
    private static IEnumerable<string> ListDirectory(
        string absolute,
        string display,
        bool showsHidden,
        bool recurses,
        bool first)
    {
        List<string> names = [.. Directory.GetFileSystemEntries(absolute).Select(Path.GetFileName).OfType<string>()];
        names.RemoveAll(name => !showsHidden && name.StartsWith('.'));
        names.Sort(StringComparer.Ordinal);

        if (recurses)
        {
            yield return first ? $"{display}:\n" : $"\n{display}:\n";
        }

        StringBuilder block = new();
        if (showsHidden)
        {
            block.Append(".\n..\n");
        }

        foreach (string name in names)
        {
            block.Append(name).Append('\n');
        }

        yield return block.ToString();

        if (!recurses)
        {
            yield break;
        }

        foreach (string name in names.Where(name => Directory.Exists(Path.Combine(absolute, name))))
        {
            foreach (string chunk in ListDirectory(
                Path.Combine(absolute, name),
                $"{display}/{name}",
                showsHidden,
                recurses,
                first: false))
            {
                yield return chunk;
            }
        }
    }
}
