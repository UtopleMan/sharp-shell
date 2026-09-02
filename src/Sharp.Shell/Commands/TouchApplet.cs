using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class TouchApplet : IApplet
{
    public string Name => "touch";

    public bool Mutates => true;

    public IReadOnlyList<string> BundleableFlags => ["-a", "-m"];

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Writing(FlagReader.PositionsOfOperands(arguments));

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments, "-a", "-m");

    public AppletRun Run(AppletContext context)
    {
        AppletRun run = new();

        foreach (string operand in FlagReader.Operands(context.Arguments))
        {
            if (!MutationGuard.TryResolve(operand, context, Name, out string absolute))
            {
                run.ExitCode = 1;
                continue;
            }

            if (File.Exists(absolute) || Directory.Exists(absolute))
            {
                File.SetLastWriteTimeUtc(absolute, DateTime.UtcNow);
                continue;
            }

            if (!Directory.Exists(Path.GetDirectoryName(absolute)))
            {
                context.WriteError($"touch: {operand}: No such file or directory\n");
                run.ExitCode = 1;
                continue;
            }

            File.WriteAllText(absolute, string.Empty);
        }

        return run;
    }
}
