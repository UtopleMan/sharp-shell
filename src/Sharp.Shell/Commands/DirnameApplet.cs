using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class DirnameApplet : IApplet
{
    public string Name => "dirname";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count == 0)
        {
            context.WriteError("dirname: usage: dirname path\n");
            return AppletRun.Failed(2);
        }

        return new AppletRun { Output = TextStream.FromText($"{PathText.Parent(operands[0])}\n") };
    }
}
