using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Confined to the workspace root. Every bash invocation starts there, so cd is per-line state.
public sealed class CdApplet : IApplet
{
    private const string PREVIOUS = "-";

    public string Name => "cd";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> operands = FlagReader.Operands(context.Arguments);

        if (operands.Count == 0)
        {
            return Change(context, context.State.RootPath, announce: false);
        }

        return operands[0] == PREVIOUS ? Return(context) : Enter(context, operands[0]);
    }

    private static AppletRun Return(AppletContext context)
    {
        if (!context.State.Variables.TryGetValue("OLDPWD", out string previous) || previous.Length == 0)
        {
            context.WriteError("cd: OLDPWD not set\n");
            return AppletRun.Failed(1);
        }

        return Change(context, previous, announce: true);
    }

    // The directory you land in is announced whenever it is not the one you typed — after `cd -`, and
    // after a CDPATH search found the target somewhere else. bash does the same, for the same reason.
    private static AppletRun Enter(AppletContext context, string target) =>
        FoundOnCdPath(context.State, target) is { } found
            ? Change(context, found, announce: true)
            : Change(context, target, announce: false);

    private static AppletRun Change(AppletContext context, string target, bool announce)
    {
        string previous = context.State.WorkingDirectory;

        if (!context.State.TryChangeDirectory(target, out string error))
        {
            context.WriteError($"{error}\n");
            return AppletRun.Failed(1);
        }

        if (context.State.Options.AutoPushd)
        {
            context.State.DirectoryStack.Insert(0, previous);
        }

        return announce
            ? new AppletRun { Output = TextStream.FromText($"{context.State.WorkingDirectory}\n") }
            : new AppletRun();
    }

    // CDPATH answers only for a target that names no directory of its own: an absolute path and an
    // explicitly relative one — `.`, `..`, `./x`, `../x` — mean what they say.
    private static string? FoundOnCdPath(ShellState state, string target)
    {
        if (!state.Variables.TryGetValue("CDPATH", out string search) || IsSelfDescribing(target))
        {
            return null;
        }

        foreach (string directory in search.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, target);

            if (Directory.Exists(state.Resolve(candidate)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsSelfDescribing(string target) =>
        Path.IsPathRooted(target)
        || target is "." or ".."
        || target.StartsWith("./", StringComparison.Ordinal)
        || target.StartsWith("../", StringComparison.Ordinal);
}
