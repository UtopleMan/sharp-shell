using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Runs a file of shell commands against the *current* state, so a cd, an export or an alias inside it
// sticks. That is the whole point, and it is what makes an rc file possible — finding one is the
// host's job, running it is this.
public sealed class SourceApplet(string name) : IApplet
{
    public string Name => name;

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        FlagReader.PositionsOfOperands(arguments) is [int first, ..]
            ? new OperandPositions([first], [])
            : OperandPositions.None;

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count == 0)
        {
            context.WriteError($"{name}: filename argument required\n");
            return AppletRun.Failed(2);
        }

        return Read(context, operands[0]) is { } text
            ? Execute(context, text)
            : AppletRun.Failed(1);
    }

    private AppletRun Execute(AppletContext context, string text)
    {
        ShellResult result = context.RunShellText(text);

        if (result.Stderr.Length > 0)
        {
            context.WriteError(result.Stderr);
        }

        return new AppletRun { Output = TextStream.FromText(result.Stdout), ExitCode = result.ExitCode };
    }

    // A file that cannot be read says why. A silent success here would make a broken rc file look like
    // an empty one.
    private string? Read(AppletContext context, string operand)
    {
        string absolute = context.State.Resolve(operand);

        if (!context.State.IsInsideRoot(absolute))
        {
            context.WriteError($"{name}: {operand}: outside the workspace\n");
            return null;
        }

        if (!File.Exists(absolute))
        {
            context.WriteError($"{name}: {operand}: No such file or directory\n");
            return null;
        }

        try
        {
            return File.ReadAllText(absolute);
        }
        catch (IOException failure)
        {
            context.WriteError($"{name}: {operand}: {failure.Message}\n");
            return null;
        }
        catch (UnauthorizedAccessException failure)
        {
            context.WriteError($"{name}: {operand}: {failure.Message}\n");
            return null;
        }
    }
}
