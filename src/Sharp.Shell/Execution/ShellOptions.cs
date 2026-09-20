namespace Sharp.Shell.Execution;

// The options table. One flag has one home and three spellings reach it — bash's `set -o` and
// `shopt`, and zsh's `setopt` — because an rc file written in either dialect has to switch the same
// thing. bash keeps two tables and a name belongs to exactly one of them; here there is one table, so
// `shopt -s errexit` works and so does `set -o autocd`.
public sealed class ShellOptions
{
    public const string ERREXIT = "errexit";

    public const string NOUNSET = "nounset";

    public const string PIPEFAIL = "pipefail";

    public const string AUTOCD = "autocd";

    public const string AUTOPUSHD = "autopushd";

    // Read by an interactive host, not by the executor. They are stored here so a line editor has
    // somewhere to read them from rather than inventing a second settings surface.
    public const string HISTIGNOREDUPS = "histignoredups";

    public const string HISTIGNORESPACE = "histignorespace";

    public const string NOFLOWCONTROL = "noflowcontrol";

    private readonly Dictionary<string, bool> byName = new(StringComparer.Ordinal)
    {
        [ERREXIT] = false,
        [NOUNSET] = false,
        [PIPEFAIL] = false,
        [AUTOCD] = false,
        [AUTOPUSHD] = false,
        [HISTIGNOREDUPS] = false,
        [HISTIGNORESPACE] = false,
        [NOFLOWCONTROL] = false,
    };

    public bool ErrExit => byName[ERREXIT];

    public bool NoUnset => byName[NOUNSET];

    public bool PipeFail => byName[PIPEFAIL];

    public bool AutoCd => byName[AUTOCD];

    public bool AutoPushd => byName[AUTOPUSHD];

    public bool IsSet(string name) => byName.TryGetValue(name, out bool value) && value;

    public bool IsKnown(string name) => byName.ContainsKey(name);

    // False for a name the table does not have. An option nobody implements must be an error rather
    // than a flag that silently does nothing.
    public bool TrySet(string name, bool value)
    {
        if (!byName.ContainsKey(name))
        {
            return false;
        }

        byName[name] = value;
        return true;
    }

    // In name order, so a listing is stable wherever it is printed.
    public IReadOnlyList<KeyValuePair<string, bool>> All =>
        [.. byName.OrderBy(option => option.Key, StringComparer.Ordinal)];

    // What a command substitution inherits. Everything but errexit, which bash deliberately does not
    // pass into a `$( )` — the corpus pins it: with `set -e`, `echo $(echo one; false; echo two)`
    // prints "one two" and the outer shell keeps going. bash calls the opposite `inherit_errexit`, and
    // it is off by default.
    public void CopyToSubshell(ShellOptions destination)
    {
        foreach (KeyValuePair<string, bool> option in byName)
        {
            destination.byName[option.Key] = option.Value;
        }

        destination.byName[ERREXIT] = false;
    }
}
