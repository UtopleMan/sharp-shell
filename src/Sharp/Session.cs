using Sharp.Shell;
using Sharp.Shell.Commands;
using Sharp.Shell.Execution;

namespace Sharp;

// One shell session: the owned command set, the classifier behind it, and the state that persists
// between lines.
//
// State persisting is the one deliberate difference from the sandboxed tool, where every bash call
// gets a fresh ShellState. A tool call is a one-shot; a shell is a conversation, so `cd` sticks.
internal sealed class Session
{
    // What this shell answers to: $0, and the prefix on every message it writes about itself.
    public const string SHELL_NAME = "shsh";

    private readonly ShellExecutor executor;
    private readonly ExplainingCommandApprover? explaining;
    private readonly ShellState state;
    private readonly SessionSettings settings;
    private readonly TextWriter output;
    private readonly TextWriter error;

    // ShellState starts at its root, which for an unconfined session is "/" — not where the user
    // ran the shell. A shell that opens somewhere other than your current directory is useless, so
    // the session moves there first.
    //
    // The two writers are arguments rather than Console: the binary passes Console.Out and
    // Console.Error, and a test passes a StringWriter and reads what the session said.
    public Session(SessionSettings settings, TextWriter output, TextWriter error)
    {
        this.settings = settings;
        this.output = output;
        this.error = error;
        state = new ShellState(settings.Root, shellName: SHELL_NAME, environment: InheritedEnvironment());

        explaining = settings.Explains ? new ExplainingCommandApprover(Policy(settings)) : null;
        executor = new ShellExecutor(
            AppletRegistry.CreateDefault(),
            settings.Strict ? new NotSupportedCommandExecutor() : new NativeTier(),
            explaining ?? Policy(settings));

        if (!state.TryChangeDirectory(settings.StartDirectory, out string failure))
        {
            error.WriteLine($"{SHELL_NAME}: {failure}");
        }
    }

    // A real shell starts with the environment it was launched with. --root confines the filesystem,
    // not the environment: a path variable is a value, and the sandbox is what decides which of those
    // paths may actually be reached.
    private static Dictionary<string, string> InheritedEnvironment()
    {
        Dictionary<string, string> inherited = new(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                inherited[name] = value;
            }
        }

        return inherited;
    }

    // The exit builtin, and the two options that end a shell: errexit on a failed command, nounset on
    // a variable nobody set. bash stops reading in all three cases, interactively as well, so a script
    // under `set -e` must not carry on to its next line here either.
    public bool WantsExit => state.ExitRequested || state.StoppedOnFailure;

    public int ExitStatus => state.ExitStatus;

    public string WorkingDirectory => state.WorkingDirectory;

    // The prompts. PS1 and PS2 when the session has them, and otherwise what this shell has always
    // printed: where you are, and a continuation marker.
    public string Prompt => Rendered("PS1") ?? $"{DirectoryLabel()}$ ";

    public string ContinuationPrompt => Rendered("PS2") ?? "> ";

    // ~/.shshenv on every start, ~/.shshrc only when there is someone to type at. That is the zsh
    // split and the reason there are two files: a script and a -c line want the environment, not the
    // aliases and the prompt.
    //
    // A missing file is silence. A broken one is reported with its name and the shell starts anyway —
    // an unusable shell is worse than a broken alias.
    public void LoadStartupFiles(bool interactive)
    {
        if (!settings.ReadsRcFiles)
        {
            return;
        }

        Load(".shshenv");

        if (interactive)
        {
            Load(".shshrc");
        }
    }

    private void Load(string name)
    {
        if (RcDirectory() is not { } home)
        {
            return;
        }

        string path = Path.Combine(home, name);

        if (!File.Exists(path))
        {
            return;
        }

        string text = File.ReadAllText(path);

        // Checked before running rather than after: a file that cannot be parsed must be reported by
        // name, not handed to a real shell the way an unparseable typed line is.
        if (CommandReader.SyntaxErrorIn(text) is { } reason)
        {
            error.WriteLine($"{SHELL_NAME}: {name}: {reason}");
            return;
        }

        ShellResult result = executor.Run(text, state, CancellationToken.None).Result;

        output.Write(result.Stdout);

        if (result.Stderr.Length > 0)
        {
            error.Write($"{SHELL_NAME}: {name}: {result.Stderr.TrimStart()}");
        }
    }

    // Where the rc files live. The setting exists so a test can point it at a temporary directory;
    // production leaves it null and $HOME answers.
    private string? RcDirectory() => settings.Home ?? HomeVariable();

    // What \w shortens against, which is the live variable rather than the setting: someone who changes
    // HOME expects the prompt to follow it.
    private string? HomeVariable() =>
        state.Variables.TryGetValue("HOME", out string home) && home.Length > 0 ? home : null;

    private string? Rendered(string name) =>
        state.Variables.TryGetValue(name, out string format)
            ? PromptRenderer.Render(format, state.WorkingDirectory, HomeVariable(), state.LastExitCode)
            : null;

    // What the prompt says when PS1 is unset: the working directory relative to the root, or the
    // root's own name when they are the same.
    private string DirectoryLabel()
    {
        string relative = Path.GetRelativePath(settings.Root, state.WorkingDirectory);

        return relative == "."
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(settings.Root))
            : relative;
    }

    public int Run(string line, CancellationToken cancellationToken)
    {
        if (line.Trim().Length == 0)
        {
            return state.LastExitCode;
        }

        explaining?.Forget();

        ShellRun run = executor.Run(line, state, cancellationToken);
        Explain(run.Classification);

        output.Write(run.Result.Stdout);
        error.Write(run.Result.Stderr);
        state.LastExitCode = run.Result.ExitCode;

        return run.Result.ExitCode;
    }

    // Strict mode is the sandboxed guest's configuration: no process can be started, so an unowned
    // name is refused at the dispatch point rather than escaped to. It is also what makes this
    // binary testable — with the fall-through on, a corpus case using an unimplemented construct
    // would be answered by a real shell and score as a pass, measuring nothing.
    private static ICommandApprover Policy(SessionSettings settings) =>
        settings.Strict ? new OwnedOnlyCommandApprover() : new AllowAllCommandApprover();

    private void Explain(Classification classification)
    {
        if (!settings.Explains)
        {
            return;
        }

        string tier = classification.Tier == ExecutionTier.Owned ? "owned" : "native";
        string programs = classification.UnownedPrograms.Count == 0
            ? string.Empty
            : $" {string.Join(' ', classification.UnownedPrograms)}";
        string mutates = classification.Mutates ? " mutates" : string.Empty;
        string reason = classification.Reason is null ? string.Empty : $" — {classification.Reason}";

        error.WriteLine($"[{tier}{programs}{mutates}]{reason}");

        foreach (string decision in explaining!.Decisions)
        {
            error.WriteLine(decision);
        }
    }
}
