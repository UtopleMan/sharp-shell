using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Streams its operands lazily so `cat big | head -1` reads one line, not the whole file. A missing
// or out-of-workspace operand is reported and the remaining operands still run, as cat does.
public sealed class CatApplet : IApplet
{
    public string Name => "cat";

    public bool Mutates => false;

    public OperandPositions FileOperandPositions(IReadOnlyList<string> arguments) =>
        OperandPositions.Reading(FlagReader.PositionsOfOperands(arguments));

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);
        AppletRun run = new() { Output = TextStream.Empty };

        run.Output = operands.Count == 0 ? context.Input : ReadFiles(operands, context, run);
        return run;
    }

    private static IEnumerable<string> ReadFiles(IReadOnlyList<string> operands, AppletContext context, AppletRun run)
    {
        foreach (string operand in operands)
        {
            string absolute = context.State.Resolve(operand);

            if (!context.State.IsInsideRoot(absolute))
            {
                context.WriteError($"cat: {operand}: outside the workspace\n");
                run.ExitCode = 1;
                continue;
            }

            if (!File.Exists(absolute))
            {
                context.WriteError($"cat: {operand}: No such file or directory\n");
                run.ExitCode = 1;
                continue;
            }

            foreach (string chunk in FileChunks.Read(absolute))
            {
                yield return chunk;
            }
        }
    }
}
