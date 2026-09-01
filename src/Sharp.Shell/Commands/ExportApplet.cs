using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// There is no separate environment inside the sandbox: the guest starts with nothing and a
// variable is a variable. export therefore assigns and otherwise does nothing, which is the honest
// behaviour rather than a fake export list.
public sealed class ExportApplet : IApplet
{
    public string Name => "export";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        foreach (string argument in FlagReader.Operands(context.Arguments))
        {
            int equals = argument.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                context.State.Variables[argument[..equals]] = argument[(equals + 1)..];
            }
        }

        return new AppletRun();
    }
}
