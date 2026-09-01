using Sharp.Shell.Commands;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// An unbounded producer that counts what it actually yielded. A pipeline that stops its consumer
// must stop this applet too; a pipeline that buffers instead will run the counter away.
internal sealed class CountingApplet : IApplet
{
    public string Name => "counting";

    public bool Mutates => false;

    public int Produced { get; private set; }

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagSupport.Supported;

    public AppletRun Run(AppletContext context)
    {
        AppletRun run = new() { Output = Produce() };
        return run;

        IEnumerable<string> Produce()
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                Produced++;
                yield return $"line {Produced}\n";
            }
        }
    }
}
