using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Path arithmetic, not filesystem access: basename operates on the string it is given, so it is
// not workspace-confined. Nothing it does can read or write anything.
public sealed class BasenameApplet : IApplet
{
    public string Name => "basename";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count == 0)
        {
            context.WriteError("basename: usage: basename path [suffix]\n");
            return AppletRun.Failed(2);
        }

        string name = PathText.LastSegment(operands[0]);

        if (operands.Count > 1 && name.Length > operands[1].Length && name.EndsWith(operands[1], StringComparison.Ordinal))
        {
            name = name[..^operands[1].Length];
        }

        return new AppletRun { Output = TextStream.FromText($"{name}\n") };
    }
}
