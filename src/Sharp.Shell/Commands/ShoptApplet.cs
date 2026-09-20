namespace Sharp.Shell.Commands;

// bash's other spelling of the same table. -s sets, -u clears, -q answers through the exit status
// without printing, and bare `shopt` lists.
public sealed class ShoptApplet : IApplet
{
    private const string SET = "-s";

    private const string UNSET = "-u";

    private const string QUERY = "-q";

    private const string APPLET = "shopt";

    public string Name => "shopt";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) =>
        FlagReader.RejectUnknownFlags(arguments, SET, UNSET, QUERY);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> names = FlagReader.Operands(context.Arguments);

        if (names.Count == 0)
        {
            return new AppletRun { Output = OptionListing.Table(context.State.Options) };
        }

        return context.Arguments.Contains(QUERY, StringComparer.Ordinal)
            ? Query(context, names)
            : Switch(context, names, turningOn: !context.Arguments.Contains(UNSET, StringComparer.Ordinal));
    }

    private static AppletRun Query(AppletContext context, IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            if (!context.State.Options.IsKnown(name))
            {
                return OptionListing.Reject(context, APPLET, name, "shell option name");
            }

            if (!context.State.Options.IsSet(name))
            {
                return AppletRun.Failed(1);
            }
        }

        return new AppletRun();
    }

    private static AppletRun Switch(AppletContext context, IReadOnlyList<string> names, bool turningOn)
    {
        foreach (string name in names)
        {
            if (!context.State.Options.TrySet(name, turningOn))
            {
                return OptionListing.Reject(context, APPLET, name, "shell option name");
            }
        }

        return new AppletRun();
    }
}
