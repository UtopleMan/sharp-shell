using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// Rule 1, conservative applets: a flag an applet does not implement is a refusal, never a
// best-effort approximation. Wrong output is invisible; escalation is merely a prompt.
public sealed record FlagSupport(bool IsSupported, string? UnsupportedFlag)
{
    public static FlagSupport Supported { get; } = new(true, null);

    public static FlagSupport Reject(string flag) => new(false, flag);
}

public sealed record AppletContext(
    IReadOnlyList<string> Arguments,
    IEnumerable<string> Input,
    ShellState State,
    Action<string> WriteError,
    CancellationToken CancellationToken);

// Output is enumerated lazily by whoever consumes it, so ExitCode is only final once the
// enumeration has been drained. An applet that reports a per-operand failure mid-stream needs the
// run object to exist before it builds its iterator, which is why Output is settable rather than
// init-only: `AppletRun run = new(); run.Output = Stream(run);`.
public sealed class AppletRun
{
    public IEnumerable<string> Output { get; set; } = TextStream.Empty;

    public int ExitCode { get; set; }

    public static AppletRun Failed(int exitCode) => new() { ExitCode = exitCode };
}

public interface IApplet
{
    string Name { get; }

    // Whether running this can change the workspace. Drives the host's prompt policy: the sandbox
    // bounds where a command can act, not what it does, so `rm -rf .` is fully confined and still
    // destroys the work.
    bool Mutates { get; }

    // The single-character flags this applet implements, and therefore the ones that may arrive
    // bundled (`grep -rn`). Empty means "never split a group", which is right for applets whose
    // options are single-dash words — find's -name, -maxdepth — and for test's operators.
    IReadOnlyList<string> BundleableFlags => [];

    // Whether *this invocation* can change the workspace. sed reads with -n '1,50p' and writes
    // with -i, and the prompt policy needs the answer for the command actually being run.
    bool MutatesWith(IReadOnlyList<string> arguments) => Mutates;

    // Indices, into the arguments following the command name, of words that are program text rather
    // than data. Classification sees a word before it is expanded, so `sed "s/$x/y/"` arrives as
    // `s//y/` — a valid script that is not the one that will run. Only the declared positions have to
    // be literal, which is what makes `sed -n '1,5p' "$file"` checkable and `sed "$script" f` not.
    IReadOnlyList<int> ProgramTextArguments(IReadOnlyList<string> arguments) => [];

    FlagSupport CheckFlags(IReadOnlyList<string> arguments);

    AppletRun Run(AppletContext context);
}
