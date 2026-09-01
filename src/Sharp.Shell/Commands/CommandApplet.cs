using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// There is no PATH in this tier, so `command -v` answers about the owned set and nothing else.
// A name it does not know is not "somewhere on the system" — it is a name that escalates.
public sealed class CommandApplet(Func<IReadOnlyCollection<string>> ownedNames) : IApplet
{
    public string Name => "command";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments, "-v", "-V");

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        bool asksWhere = context.Arguments.Contains("-v", StringComparer.Ordinal)
            || context.Arguments.Contains("-V", StringComparer.Ordinal);

        if (!asksWhere || operands.Count == 0)
        {
            context.WriteError("command: usage: command -v name\n");
            return AppletRun.Failed(2);
        }

        return ownedNames().Contains(operands[0])
            ? new AppletRun { Output = TextStream.FromText($"{operands[0]}\n") }
            : AppletRun.Failed(1);
    }
}
