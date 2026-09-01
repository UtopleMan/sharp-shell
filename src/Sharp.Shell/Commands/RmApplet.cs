using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class RmApplet : IApplet
{
    public string Name => "rm";

    public bool Mutates => true;

    public IReadOnlyList<string> BundleableFlags => ["-r", "-R", "-f"];

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, "-r", "-R", "-f", "-rf", "-fr");

    public AppletRun Run(AppletContext context)
    {
        bool recurses = context.Arguments.Any(argument => argument is "-r" or "-R" or "-rf" or "-fr");
        bool forces = context.Arguments.Any(argument => argument is "-f" or "-rf" or "-fr");
        AppletRun run = new();

        foreach (string operand in FlagReader.Operands(context.Arguments))
        {
            Remove(operand, context, run, recurses, forces);
        }

        return run;
    }

    private void Remove(string operand, AppletContext context, AppletRun run, bool recurses, bool forces)
    {
        if (!MutationGuard.TryResolve(operand, context, Name, out string absolute))
        {
            run.ExitCode = 1;
            return;
        }

        if (File.Exists(absolute))
        {
            File.Delete(absolute);
            return;
        }

        if (Directory.Exists(absolute))
        {
            RemoveDirectory(operand, absolute, context, run, recurses);
            return;
        }

        if (forces)
        {
            return;
        }

        context.WriteError($"rm: {operand}: No such file or directory\n");
        run.ExitCode = 1;
    }

    private static void RemoveDirectory(string operand, string absolute, AppletContext context, AppletRun run, bool recurses)
    {
        if (!recurses)
        {
            context.WriteError($"rm: {operand}: is a directory\n");
            run.ExitCode = 1;
            return;
        }

        Directory.Delete(absolute, recursive: true);
    }
}
