using System.Text;
using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// The format string is this applet's flag surface, so an unimplemented conversion is refused before
// anything runs rather than printed wrong.
public sealed class PrintfApplet : IApplet
{
    private const string SupportedConversions = "sd%";

    public string Name => "printf";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return FlagSupport.Supported;
        }

        string format = arguments[0];
        for (int index = 0; index < format.Length - 1; index++)
        {
            if (format[index] == '%' && !SupportedConversions.Contains(format[index + 1], StringComparison.Ordinal))
            {
                return FlagSupport.Reject($"%{format[index + 1]}");
            }

            if (format[index] == '%')
            {
                index++;
            }
        }

        return FlagSupport.Supported;
    }

    public AppletRun Run(AppletContext context)
    {
        if (context.Arguments.Count == 0)
        {
            context.WriteError("printf: usage: printf format [arguments]\n");
            return AppletRun.Failed(2);
        }

        string format = Escapes.Interpret(context.Arguments[0]);
        IReadOnlyList<string> operands = [.. context.Arguments.Skip(1)];

        return new AppletRun { Output = TextStream.FromText(Format(format, operands)) };
    }

    // bash repeats the format until every operand is consumed; with no operands it runs exactly once.
    private static string Format(string format, IReadOnlyList<string> operands)
    {
        StringBuilder output = new();
        int consumed = 0;

        do
        {
            consumed = AppendOnce(output, format, operands, consumed);
        }
        while (consumed < operands.Count && consumed > 0);

        return output.ToString();
    }

    private static int AppendOnce(StringBuilder output, string format, IReadOnlyList<string> operands, int next)
    {
        for (int index = 0; index < format.Length; index++)
        {
            if (format[index] != '%' || index + 1 >= format.Length)
            {
                output.Append(format[index]);
                continue;
            }

            char conversion = format[++index];
            if (conversion == '%')
            {
                output.Append('%');
                continue;
            }

            string operand = next < operands.Count ? operands[next++] : string.Empty;
            output.Append(conversion == 'd' ? FormatInteger(operand) : operand);
        }

        return next;
    }

    private static string FormatInteger(string operand) =>
        long.TryParse(operand, out long value) ? value.ToString() : "0";
}
