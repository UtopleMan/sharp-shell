using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// `true` and `:` are the same command under two names: do nothing, succeed. `:` is not decoration —
// it is how shells spell an empty loop body, so without it `while true; do :; done` escalates.
public sealed class TrueApplet(string name = "true") : IApplet
{
    public string Name => name;

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagSupport.Supported;

    public AppletRun Run(AppletContext context) => new() { Output = TextStream.Empty };
}
