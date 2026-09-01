using Sharp.Shell.Commands;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// One workspace-rooted shell per test, with the default applet set plus whatever a test adds.
internal sealed class ShellHarness : IDisposable
{
    private readonly List<IApplet> extraApplets = [];

    public ShellHarness(ICommandExecutor? external = null)
    {
        Root = Directory.CreateTempSubdirectory("duetui-shell").FullName;
        External = external ?? new NotSupportedCommandExecutor();
    }

    public string Root { get; }

    public ICommandExecutor External { get; }

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
        Executor().Execute(commandLine, new ShellState(Root), CancellationToken.None);

    // Goes through classification first, the way the host will.
    public ShellRun Classified(string commandLine) =>
        Executor().Run(commandLine, new ShellState(Root), CancellationToken.None);

    private ShellExecutor Executor() =>
        new(new AppletRegistry([.. AppletRegistry.CreateDefault().Applets, .. extraApplets]), External);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
