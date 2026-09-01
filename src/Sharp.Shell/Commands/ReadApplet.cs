using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Reads one line and splits it across the named variables, the last one taking the remainder.
// Limitation worth knowing: this consumes from the stage's own input stream, so a later `read` in
// the same pipeline stage starts over rather than continuing where this one stopped.
public sealed class ReadApplet : IApplet
{
    public string Name => "read";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments, "-r");

    public AppletRun Run(AppletContext context)
    {
        string? line = TextStream.Lines(context.Input).FirstOrDefault();
        IReadOnlyList<string> names = FlagReader.Operands(context.Arguments);
        IReadOnlyList<string> targets = names.Count > 0 ? names : ["REPLY"];

        if (line is null)
        {
            foreach (string name in targets)
            {
                context.State.Variables[name] = string.Empty;
            }

            return AppletRun.Failed(1);
        }

        Assign(line, targets, context.State);
        return new AppletRun();
    }

    private static void Assign(string line, IReadOnlyList<string> names, ShellState state)
    {
        string[] pieces = line.Split([' ', '\t'], names.Count, StringSplitOptions.RemoveEmptyEntries);

        for (int index = 0; index < names.Count; index++)
        {
            state.Variables[names[index]] = index < pieces.Length ? pieces[index].Trim() : string.Empty;
        }
    }
}
