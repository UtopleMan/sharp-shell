using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class MvApplet : IApplet
{
    public string Name => "mv";

    public bool Mutates => true;

    // Every operand but the last is a source and the last is the destination, which is what makes
    // `cp a b dir/` one write and two reads.
    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments)
    {
        IReadOnlyList<int> positions = FlagReader.PositionsOfOperands(arguments);

        return positions.Count < 2
            ? OperandPositions.None
            : new OperandPositions([.. positions.Take(positions.Count - 1)], [positions[^1]]);
    }

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments, "-f", "-n");

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count < 2)
        {
            context.WriteError("mv: usage: mv source... destination\n");
            return AppletRun.Failed(2);
        }

        if (!MutationGuard.TryResolve(operands[^1], context, Name, out string destination))
        {
            return AppletRun.Failed(1);
        }

        AppletRun run = new();
        foreach (string source in operands.Take(operands.Count - 1))
        {
            Move(source, destination, context, run);
        }

        return run;
    }

    private void Move(string source, string destination, AppletContext context, AppletRun run)
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
            File.Move(absolute, target, overwrite: true);
            return;
        }

        if (Directory.Exists(absolute))
        {
            Directory.Move(absolute, target);
            return;
        }

        context.WriteError($"mv: {source}: No such file or directory\n");
        run.ExitCode = 1;
    }
}
