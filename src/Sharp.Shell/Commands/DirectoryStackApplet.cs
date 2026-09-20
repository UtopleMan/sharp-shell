using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// pushd, popd and dirs over one stack on the state. The working directory is the stack's first entry,
// which is why all three print the same thing: the directory you are in followed by the ones you saved.
public sealed class DirectoryStackApplet(string name) : IApplet
{
    public string Name => name;

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context) => name switch
    {
        "pushd" => Push(context),
        "popd" => Pop(context),
        _ => List(context),
    };

    // bash's `dirs` takes options and no operands, and fails on one rather than ignoring it.
    private static AppletRun List(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count > 0)
        {
            context.WriteError($"dirs: {operands[0]}: invalid argument\n");
            return AppletRun.Failed(1);
        }

        return Listing(context.State);
    }

    private static AppletRun Push(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count == 0)
        {
            context.WriteError("pushd: no other directory\n");
            return AppletRun.Failed(1);
        }

        string previous = context.State.WorkingDirectory;

        if (!context.State.TryChangeDirectory(operands[0], out string error))
        {
            context.WriteError($"pushd: {error["cd: ".Length..]}\n");
            return AppletRun.Failed(1);
        }

        context.State.DirectoryStack.Insert(0, previous);
        return Listing(context.State);
    }

    private static AppletRun Pop(AppletContext context)
    {
        List<string> stack = context.State.DirectoryStack;

        if (stack.Count == 0)
        {
            context.WriteError("popd: directory stack empty\n");
            return AppletRun.Failed(1);
        }

        if (!context.State.TryChangeDirectory(stack[0], out string error))
        {
            context.WriteError($"popd: {error["cd: ".Length..]}\n");
            return AppletRun.Failed(1);
        }

        stack.RemoveAt(0);
        return Listing(context.State);
    }

    private static AppletRun Listing(ShellState state) =>
        new() { Output = TextStream.FromText($"{string.Join(' ', Shortened(state))}\n") };

    // bash writes the home directory as ~, which is what makes a long stack readable.
    private static IEnumerable<string> Shortened(ShellState state)
    {
        string? home = state.Variables.TryGetValue("HOME", out string value) && value.Length > 0 ? value : null;

        return new[] { state.WorkingDirectory }
            .Concat(state.DirectoryStack)
            .Select(directory => home is not null && directory == home ? "~" : Relative(directory, home));
    }

    private static string Relative(string directory, string? home) =>
        home is not null && directory.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? $"~{directory[home.Length..]}"
            : directory;
}
