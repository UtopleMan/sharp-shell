using Sharp.Shell.Commands;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// One workspace-rooted shell per test, with the default applet set plus whatever a test adds.
internal sealed class ShellHarness : IDisposable
{
    // The shell's name is the host's to supply, so the harness supplies one of its own. A test that
    // asserted the library's default would still pass if the plumbing fell back to it.
    public const string SHELL_NAME = "test-shell";

    private readonly List<IApplet> extraApplets = [];

    public ShellHarness(ICommandExecutor? external = null, ICommandApprover? approver = null)
    {
        Root = Directory.CreateTempSubdirectory("sharp-shell").FullName;
        External = external ?? new NotSupportedCommandExecutor();
        Approver = approver ?? new AllowAllCommandApprover();
    }

    public string Root { get; }

    public ICommandExecutor External { get; }

    public ICommandApprover Approver { get; }

    // What a host would hand the shell at construction. Empty by default, because most tests are not
    // about the environment and an empty one is the guest's honest starting point.
    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

    public void Add(IApplet applet) => extraApplets.Add(applet);

    public string Write(string relativePath, string content)
    {
        string absolute = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);
        return absolute;
    }

    // Runs the owned tier directly, which is what most tests are exercising.
    public ShellResult Run(string commandLine) =>
        Executor().Execute(commandLine, State(Root), CancellationToken.None);

    // The tool tier roots the shell at the filesystem and starts it in the workspace: a line may
    // name anything, and the rule model — not the root — is what narrows it.
    public ShellResult RunFromTheFilesystemRoot(string commandLine) =>
        Executor().Execute(commandLine, State(Path.GetPathRoot(Root)!, Root), CancellationToken.None);

    // Goes through classification first, the way the host will.
    public ShellRun Classified(string commandLine) =>
        Executor().Run(commandLine, State(Root), CancellationToken.None);

    private ShellState State(string root, string? workingDirectory = null) =>
        new(root, workingDirectory, SHELL_NAME, Environment);

    private ShellExecutor Executor() =>
        new(new AppletRegistry([.. AppletRegistry.CreateDefault().Applets, .. extraApplets]), External, Approver);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
