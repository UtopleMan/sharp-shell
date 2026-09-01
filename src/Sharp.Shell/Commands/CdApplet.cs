using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Confined to the workspace root. Every bash invocation starts there, so cd is per-line state.
public sealed class CdApplet : IApplet
{
    public string Name => "cd";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        string target = operands.Count == 0 ? context.State.RootPath : operands[0];

        if (context.State.TryChangeDirectory(target, out string error))
        {
            return new AppletRun();
        }

        context.WriteError($"{error}\n");
        return AppletRun.Failed(1);
    }
}
