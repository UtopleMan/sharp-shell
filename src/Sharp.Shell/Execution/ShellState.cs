namespace Sharp.Shell.Execution;

// One shell invocation's mutable state: where it is, what its variables hold, what the last command
// returned. A fresh instance per bash call is the contract — the wasm instance is reused, the shell
// state is not, so a stray cd in one call cannot silently relocate a later one.
//
// The root is enforced here as well as by the wasm preopen, because this library also runs natively
// in tests and on the host's fall-through path, where no preopen exists.
public sealed class ShellState(string rootPath)
{
    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public string WorkingDirectory { get; private set; } = Path.GetFullPath(rootPath);

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

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

    // Command substitution runs against a copy, so a cd or an assignment inside $(...) does not
    // leak into the surrounding shell.
    public ShellState Fork()
    {
        ShellState copy = new(RootPath) { LastExitCode = LastExitCode };
        copy.TryChangeDirectory(WorkingDirectory, out _);
        foreach (KeyValuePair<string, string> variable in Variables)
        {
            copy.Variables[variable.Key] = variable.Value;
        }

        return copy;
    }

    public string Resolve(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(WorkingDirectory, path));

    public bool IsInsideRoot(string absolutePath)
    {
        string normalised = Path.TrimEndingDirectorySeparator(Path.GetFullPath(absolutePath));
        string root = Path.TrimEndingDirectorySeparator(RootPath);

        // The filesystem root is the case that needs care: TrimEndingDirectorySeparator leaves "/"
        // alone, so appending a separator gives "//" and nothing is ever inside it. Build the
        // prefix rather than concatenating blindly.
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return normalised == root || normalised.StartsWith(rootPrefix, StringComparison.Ordinal);
    }

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
}
