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
public enum UnwindReason
{
    None,
    Exit,
    Refusal,
    Failure,
}

public sealed class ShellState(
    string rootPath,
    string? workingDirectory = null,
    string? shellName = null,
    IReadOnlyDictionary<string, string>? environment = null)
{
    // The name this shell answers to. The host supplies it: the binary is shsh, duetui's guest is
    // something else, and the core has no business naming either. A host that supplies nothing gets
    // this library's own name rather than a third product's.
    private const string DEFAULT_SHELL_NAME = "sharp-shell";

    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public string ShellName { get; } = shellName ?? DEFAULT_SHELL_NAME;

    public string WorkingDirectory { get; private set; } = StartingDirectory(rootPath, workingDirectory);

    public ShellVariables Variables { get; } = ShellVariables.SeededWith(environment);

    // What a child process receives. The shell decides which variables are handed over; the host
    // decides what a child actually gets.
    public IReadOnlyDictionary<string, string> ExportedVariables => Variables.Exported;

    // The options table, per state and surviving a fork the way the variables do.
    public ShellOptions Options { get; } = new();

    // Aliases, by name. A substitution at the command position rather than a variable or a function,
    // and part of what one session accumulates, so it forks with the rest of the state.
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

    // The directories pushd saved, most recent first. The working directory is not in here — it is
    // what `dirs` prints in front of them, which is how bash numbers the stack.
    public List<string> DirectoryStack { get; } = [];

    // Shell functions, by name. They live on the state rather than the executor because they are
    // part of what one session accumulates, exactly as variables are.
    public Dictionary<string, ShellNode> Functions { get; } = new(StringComparer.Ordinal);

    // $1, $2, $# — empty outside a function, because this shell is never given script arguments.
    public IReadOnlyList<string> PositionalArguments { get; private set; } = [];

    public void SetPositionalArguments(IReadOnlyList<string> arguments) => PositionalArguments = arguments;

    public int LastExitCode { get; set; }

    // Why this run is on its way out, if it is. One mechanism with three reasons, not a boolean per
    // reason: every construct that can continue to a next command asks IsUnwinding, and a new reason
    // must not turn that question into a list of them.
    private UnwindReason unwinding;

    // Whether the run is on its way out, by any route. Every construct that can continue to a next
    // command checks this before doing so.
    public bool IsUnwinding => unwinding != UnwindReason.None;

    // Set by the exit builtin. The executor checks it after each command rather than an exception
    // being thrown across every applet's lazy enumerator.
    public bool ExitRequested => unwinding == UnwindReason.Exit;

    // Set when the host's approver denies a command. It unwinds the run the way an exit does, and
    // for the same reason: a refusal that merely returned non-zero would let `denied || fallback`
    // route around it into a branch nobody approved.
    public bool RefusalRequested => unwinding == UnwindReason.Refusal;

    // Set when errexit stopped the run, or when nounset met a variable nobody had set. bash ends the
    // whole shell for both — an interactive one included — so a host reading this should too.
    public bool StoppedOnFailure => unwinding == UnwindReason.Failure;

    public int ExitStatus { get; private set; }

    public string? RefusalReason { get; private set; }

    public void RequestExit(int status)
    {
        unwinding = UnwindReason.Exit;
        ExitStatus = status;
    }

    public void RequestRefusal(string reason)
    {
        unwinding = UnwindReason.Refusal;
        RefusalReason = reason;
    }

    // A command that failed under errexit. It is a third reason for the same unwind rather than a
    // mechanism of its own, which is what keeps `set -e`, `exit` and a refusal from interleaving.
    public void RequestFailure(int status)
    {
        unwinding = UnwindReason.Failure;
        ExitStatus = status;
    }

    // The unwind belongs to one run. Without this a session that refused a single command went
    // silently dead: every later line expanded its words, saw the refusal still set and ran nothing.
    public void BeginRun()
    {
        unwinding = UnwindReason.None;
        ExitStatus = 0;
        RefusalReason = null;
    }

    // Command substitution runs against a copy, so a cd or an assignment inside $(...) does not
    // leak into the surrounding shell.
    public ShellState Fork()
    {
        ShellState copy = new(RootPath, WorkingDirectory, ShellName) { LastExitCode = LastExitCode };
        Variables.CopyTo(copy.Variables);
        Options.CopyToSubshell(copy.Options);

        foreach (KeyValuePair<string, string> alias in Aliases)
        {
            copy.Aliases[alias.Key] = alias.Value;
        }

        copy.DirectoryStack.AddRange(DirectoryStack);

        foreach (KeyValuePair<string, ShellNode> function in Functions)
        {
            copy.Functions[function.Key] = function.Value;
        }

        copy.SetPositionalArguments(PositionalArguments);

        return copy;
    }

    // Every message the shell itself writes carries its own name, the way bash and zsh do.
    public string Message(string text) => $"{ShellName}: {text}\n";

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

        string previous = WorkingDirectory;
        WorkingDirectory = target;
        Variables["OLDPWD"] = previous;
        Variables["PWD"] = target;
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
