using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Unlike basename and dirname, this resolves against the real filesystem, so it is confined.
public sealed class RealpathApplet : IApplet
{
    public string Name => "realpath";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-e", "-m"];

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments, "-e", "-m");

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count == 0)
        {
            context.WriteError("realpath: usage: realpath path...\n");
            return AppletRun.Failed(2);
        }

        AppletRun run = new();
        run.Output = Resolve(operands, context, run);
        return run;
    }

    private static IEnumerable<string> Resolve(IReadOnlyList<string> operands, AppletContext context, AppletRun run)
    {
        foreach (string operand in operands)
        {
            string absolute = context.State.Resolve(operand);

            if (!context.State.IsInsideRoot(absolute))
            {
                context.WriteError($"realpath: {operand}: outside the workspace\n");
                run.ExitCode = 1;
                continue;
            }

            yield return $"{absolute}\n";
        }
    }
}
