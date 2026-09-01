using System.Text;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// echo takes no real flags: anything it does not recognise is text, which is why CheckFlags never
// refuses. -n and -e are the two bash honours.
public sealed class EchoApplet : IApplet
{
    public string Name => "echo";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagSupport.Supported;

    public AppletRun Run(AppletContext context)
    {
        bool suppressNewline = false;
        bool interpretEscapes = false;
        int first = 0;

        while (first < context.Arguments.Count && IsOption(context.Arguments[first]))
        {
            suppressNewline |= context.Arguments[first].Contains('n', StringComparison.Ordinal);
            interpretEscapes |= context.Arguments[first].Contains('e', StringComparison.Ordinal);
            first++;
        }

        string text = string.Join(' ', context.Arguments.Skip(first));
        StringBuilder output = new(interpretEscapes ? Escapes.InterpretEchoText(text) : text);
        if (!suppressNewline)
        {
            output.Append('\n');
        }

        return new AppletRun { Output = TextStream.FromText(output.ToString()) };
    }

    private static bool IsOption(string argument) =>
        argument.Length > 1
        && argument[0] == '-'
        && argument.Skip(1).All(character => character is 'n' or 'e' or 'E');
}
