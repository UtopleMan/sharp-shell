namespace Sharp.Shell.Commands;

// bash's spelling: `set -o name`, `set +o name`, and the single letters for the two options that have
// them. Listing variables and setting positional parameters are not implemented, so `set` on its own
// and `set --` say so rather than doing something else.
public sealed class SetApplet : IApplet
{
    private const string LONG = "o";

    public string Name => "set";

    public bool Mutates => false;

    public FlagSupport CheckFlags(IReadOnlyList<string> arguments)
    {
        foreach (string argument in Switches(arguments))
        {
            if (NameOfSwitch(argument) is null)
            {
                return FlagSupport.Reject(argument);
            }
        }

        return FlagSupport.Supported;
    }

    public AppletRun Run(AppletContext context)
    {
        if (context.Arguments.Count == 0)
        {
            context.WriteError("set: listing variables is not supported\n");
            return AppletRun.Failed(2);
        }

        return Apply(context);
    }

    private static AppletRun Apply(AppletContext context)
    {
        for (int index = 0; index < context.Arguments.Count; index++)
        {
            string argument = context.Arguments[index];
            bool turningOn = argument[0] == '-';
            string letters = argument[1..];

            if (letters == LONG)
            {
                if (index + 1 == context.Arguments.Count)
                {
                    return new AppletRun { Output = Listing(context, turningOn) };
                }

                index++;

                if (!context.State.Options.TrySet(context.Arguments[index], turningOn))
                {
                    return OptionListing.Reject(context, "set", context.Arguments[index], "option name");
                }

                continue;
            }

            foreach (char letter in letters)
            {
                context.State.Options.TrySet(NameOfLetter(letter)!, turningOn);
            }
        }

        return new AppletRun();
    }

    private static IEnumerable<string> Listing(AppletContext context, bool turningOn) =>
        turningOn ? OptionListing.Table(context.State.Options) : OptionListing.Commands(context.State.Options);

    private static IEnumerable<string> Switches(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            yield return arguments[index];

            if (NameOfSwitch(arguments[index]) == LONG)
            {
                index++;
            }
        }
    }

    // A switch is `-o`, `+o`, or a bundle of single letters this shell has an option for. Anything
    // else — `--`, `-x`, a bare operand — is refused by name rather than approximated.
    private static string? NameOfSwitch(string argument)
    {
        if (argument.Length < 2 || argument[0] is not ('-' or '+') || argument == "--")
        {
            return null;
        }

        string letters = argument[1..];

        if (letters == LONG)
        {
            return LONG;
        }

        return letters.All(letter => NameOfLetter(letter) is not null) ? letters : null;
    }

    private static string? NameOfLetter(char letter) => letter switch
    {
        'e' => Execution.ShellOptions.ERREXIT,
        'u' => Execution.ShellOptions.NOUNSET,
        _ => null,
    };
}
