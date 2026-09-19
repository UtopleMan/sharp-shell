using Sharp.Shell.Parsing;

namespace Sharp.Shell.Execution;

// One shell invocation's mutable state: where it is, what its variables hold, what the last command
// returned. A fresh instance per bash call is the contract — the wasm instance is reused, the shell
// state is not, so a stray cd in one call cannot silently relocate a later one.
//
// The root is where the shell may reach; the working directory is where it starts. Under the tool
// tier the two differ: the root is the filesystem root, so a line can name anything and the rule
// model decides, while the working directory is the workspace, so relative paths still resolve
// inside the project.
public sealed class ShellState(string rootPath, string? workingDirectory = null)
{
    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public string WorkingDirectory { get; private set; } = StartingDirectory(rootPath, workingDirectory);

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    // Shell functions, by name. They live on the state rather than the executor because they are
    // part of what one session accumulates, exactly as variables are.
    public Dictionary<string, ShellNode> Functions { get; } = new(StringComparer.Ordinal);

    // $1, $2, $# — empty outside a function, because this shell is never given script arguments.
    public IReadOnlyList<string> PositionalArguments { get; private set; } = [];

    public void SetPositionalArguments(IReadOnlyList<string> arguments) => PositionalArguments = arguments;

    public int LastExitCode { get; set; }

    // Set by the exit builtin. The executor checks it after each command rather than an exception
    // being thrown across every applet's lazy enumerator.
    public bool ExitRequested { get; private set; }

    public int ExitStatus { get; private set; }

    public void RequestExit(int status)
    {
        ExitRequested = true;
        ExitStatus = status;
    }

    // Set when the host's approver denies a command. It unwinds the run the way an exit does, and
    // for the same reason: a refusal that merely returned non-zero would let `denied || fallback`
    // route around it into a branch nobody approved.
    public bool RefusalRequested { get; private set; }

    public string? RefusalReason { get; private set; }

    public void RequestRefusal(string reason)
    {
        RefusalRequested = true;
        RefusalReason = reason;
    }

    // Whether the run is on its way out, by either route. Every construct that can continue to a
    // next command checks this before doing so.
    public bool IsUnwinding => ExitRequested || RefusalRequested;

    // Command substitution runs against a copy, so a cd or an assignment inside $(...) does not
    // leak into the surrounding shell.
    public ShellState Fork()
    {
        ShellState copy = new(RootPath, WorkingDirectory) { LastExitCode = LastExitCode };
        foreach (KeyValuePair<string, string> variable in Variables)
        {
            copy.Variables[variable.Key] = variable.Value;
        }

        foreach (KeyValuePair<string, ShellNode> function in Functions)
        {
            copy.Functions[function.Key] = function.Value;
        }

        copy.SetPositionalArguments(PositionalArguments);

        return copy;
    }

    public string Resolve(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(WorkingDirectory, path));

    public bool IsInsideRoot(string absolutePath) => IsInside(RootPath, absolutePath);

    public bool TryChangeDirectory(string path, out string error)
    {
        string target = Resolve(path);

        if (!IsInsideRoot(target))
        {
            error = $"cd: {path}: outside the workspace";
            return false;
        }

        if (!Directory.Exists(target))
        {
            error = $"cd: {path}: No such file or directory";
            return false;
        }

        WorkingDirectory = target;
        error = string.Empty;
        return true;
    }

    private static string StartingDirectory(string rootPath, string? workingDirectory)
    {
        string root = Path.GetFullPath(rootPath);

        if (workingDirectory is null)
        {
            return root;
        }

        string start = Path.GetFullPath(workingDirectory);

        if (!IsInside(root, start))
        {
            throw new ArgumentException(
                $"The working directory '{start}' is outside the root '{root}'.",
                nameof(workingDirectory));
        }

        return start;
    }

    private static bool IsInside(string rootPath, string absolutePath)
    {
        string normalised = Path.TrimEndingDirectorySeparator(Path.GetFullPath(absolutePath));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        // The filesystem root is the case that needs care: TrimEndingDirectorySeparator leaves "/"
        // alone, so appending a separator gives "//" and nothing is ever inside it. Build the
        // prefix rather than concatenating blindly.
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return normalised == root || normalised.StartsWith(rootPrefix, StringComparison.Ordinal);
    }
}
