using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class MkdirApplet : IApplet
{
    public string Name => "mkdir";

    public bool Mutates => true;

    public IReadOnlyList<string> BundleableFlags => ["-p"];

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Writing(FlagReader.PositionsOfOperands(arguments));

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments, "-p");

    public AppletRun Run(AppletContext context)
    {
        bool createsParents = context.Arguments.Contains("-p", StringComparer.Ordinal);
        AppletRun run = new();

        foreach (string operand in FlagReader.Operands(context.Arguments))
        {
            if (!MutationGuard.TryResolve(operand, context, Name, out string absolute))
            {
                run.ExitCode = 1;
                continue;
            }

            if (!createsParents && !Directory.Exists(Path.GetDirectoryName(absolute)))
            {
                context.WriteError($"mkdir: {operand}: No such file or directory\n");
                run.ExitCode = 1;
                continue;
            }

            if (!createsParents && (Directory.Exists(absolute) || File.Exists(absolute)))
            {
                context.WriteError($"mkdir: {operand}: File exists\n");
                run.ExitCode = 1;
                continue;
            }

            Directory.CreateDirectory(absolute);
        }

        return run;
    }
}
