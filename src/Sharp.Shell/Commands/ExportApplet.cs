using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Marks a variable as one a child process should see. The mark lives on the variable itself, so
// re-assigning an exported name keeps it exported.
public sealed class ExportApplet : IApplet
{
    private const string UNEXPORT = "-n";

    public string Name => "export";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, UNEXPORT);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> names = FlagReader.Operands(context.Arguments);

        if (names.Count == 0)
        {
            return Listing(context.State);
        }

        bool isUnexporting = context.Arguments.Contains(UNEXPORT, StringComparer.Ordinal);

        foreach (string name in names)
        {
            Apply(context.State.Variables, name, isUnexporting);
        }

        return new AppletRun();
    }

    private static void Apply(ShellVariables variables, string argument, bool isUnexporting)
    {
        int equals = argument.IndexOf('=', StringComparison.Ordinal);

        if (equals > 0)
        {
            variables[argument[..equals]] = argument[(equals + 1)..];
            argument = argument[..equals];
        }

        if (isUnexporting)
        {
            variables.Unexport(argument);
            return;
        }

        variables.Export(argument);
    }

    private static AppletRun Listing(ShellState state) =>
        new()
        {
            Output = state.ExportedVariables
                .OrderBy(variable => variable.Key, StringComparer.Ordinal)
                .Select(variable => $"export {variable.Key}={variable.Value}\n"),
        };
}
