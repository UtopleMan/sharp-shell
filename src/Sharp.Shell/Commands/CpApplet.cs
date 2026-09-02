using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class CpApplet : IApplet
{
    public string Name => "cp";

    public bool Mutates => true;

    public IReadOnlyList<string> BundleableFlags => ["-r", "-R", "-f", "-p"];

    // Every operand but the last is a source and the last is the destination, which is what makes
    // `cp a b dir/` one write and two reads.
    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments)
    {
        IReadOnlyList<int> positions = FlagReader.PositionsOfOperands(arguments);

        return positions.Count < 2
            ? OperandPositions.None
            : new OperandPositions([.. positions.Take(positions.Count - 1)], [positions[^1]]);
    }

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-r", "-R", "-f", "-p");

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        bool recurses = context.Arguments.Any(argument => argument is "-r" or "-R");

        if (operands.Count < 2)
        {
            context.WriteError("cp: usage: cp source... destination\n");
            return AppletRun.Failed(2);
        }

        if (!MutationGuard.TryResolve(operands[^1], context, Name, out string destination))
        {
            return AppletRun.Failed(1);
        }

        AppletRun run = new();
        foreach (string source in operands.Take(operands.Count - 1))
        {
            Copy(source, destination, recurses, context, run);
        }

        return run;
    }

    private void Copy(string source, string destination, bool recurses, AppletContext context, AppletRun run)
    {
        if (!MutationGuard.TryResolve(source, context, Name, out string absolute))
        {
            run.ExitCode = 1;
            return;
        }

        string target = Directory.Exists(destination)
            ? Path.Combine(destination, Path.GetFileName(absolute))
            : destination;

        if (File.Exists(absolute))
        {
            File.Copy(absolute, target, overwrite: true);
            return;
        }

        if (Directory.Exists(absolute))
        {
            CopyDirectory(source, absolute, target, recurses, context, run);
            return;
        }

        context.WriteError($"cp: {source}: No such file or directory\n");
        run.ExitCode = 1;
    }

    private static void CopyDirectory(
        string source,
        string absolute,
        string target,
        bool recurses,
        AppletContext context,
        AppletRun run)
    {
        if (!recurses)
        {
            context.WriteError($"cp: {source}: is a directory\n");
            run.ExitCode = 1;
            return;
        }

        Directory.CreateDirectory(target);

        foreach (string entry in Directory.GetFileSystemEntries(absolute))
        {
            string name = Path.GetFileName(entry);

            if (Directory.Exists(entry))
            {
                CopyDirectory(name, entry, Path.Combine(target, name), recurses: true, context, run);
                continue;
            }

            File.Copy(entry, Path.Combine(target, name), overwrite: true);
        }
    }
}
