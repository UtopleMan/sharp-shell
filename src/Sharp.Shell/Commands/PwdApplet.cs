using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class PwdApplet : IApplet
{
    public string Name => "pwd";

    public bool Mutates => false;

    public IReadOnlyList<string> BundleableFlags => ["-P", "-L"];

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments, "-P", "-L");

    public AppletRun Run(AppletContext context) =>
        new() { Output = TextStream.FromText($"{context.State.WorkingDirectory}\n") };
}
