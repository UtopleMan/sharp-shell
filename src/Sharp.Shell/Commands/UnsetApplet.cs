namespace Sharp.Shell.Commands;

// Removes a variable outright — value and export mark together. export -n is the one that keeps the
// value and drops only the mark.
public sealed class UnsetApplet : IApplet
{
    public string Name => "unset";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        foreach (string name in FlagReader.Operands(context.Arguments))
        {
            context.State.Variables.Remove(name);
        }

        return new AppletRun();
    }
}
