using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

public sealed class FalseApplet : IApplet
{
    public string Name => "false";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagSupport.Supported;

    public AppletRun Run(AppletContext context) => AppletRun.Failed(1);
}
