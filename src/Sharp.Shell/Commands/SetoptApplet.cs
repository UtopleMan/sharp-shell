namespace Sharp.Shell.Commands;

// zsh's spelling of the same table, registered twice: setopt turns names on, unsetopt turns them off.
// Bare `setopt` lists what is on, as zsh does.
public sealed class SetoptApplet(string name, bool turningOn) : IApplet
{
    public string Name => name;

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments) => FlagReader.RejectUnknownFlags(arguments);

    public AppletRun Run(AppletContext context)
    {
        IReadOnlyList<string> names = FlagReader.Operands(context.Arguments);

        if (names.Count == 0)
        {
            return new AppletRun { Output = OptionListing.NamesThatAreOn(context.State.Options) };
        }

        foreach (string option in names)
        {
            if (!context.State.Options.TrySet(option, turningOn))
            {
                return OptionListing.Reject(context, name, option, "option name");
            }
        }

        return new AppletRun();
    }
}
