using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Records the request on the shell state rather than throwing: an exception would have to cross
// every applet's lazy enumerator, and the executor already checks after each command.
public sealed class ExitApplet : IApplet
{
    public string Name => "exit";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        int status = operands.Count > 0 && int.TryParse(operands[0], out int requested)
            ? requested
            : context.State.LastExitCode;

        context.State.RequestExit(status);
        return AppletRun.Failed(status);
    }
}
