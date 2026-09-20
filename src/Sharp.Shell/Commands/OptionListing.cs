using Sharp.Shell.Execution;

namespace Sharp.Shell.Commands;

// How the option builtins agree on words. Three spellings reach one table, so the wording each of
// them prints and the error each of them reports live together rather than drifting apart in three
// applets.
internal static class OptionListing
{
    // bash pads the name to fifteen characters and follows it with a tab. A script that parses
    // `set -o` expects to find exactly that.
    private const int NAME_WIDTH = 15;

    public static IEnumerable<string> Table(ShellOptions options) =>
        options.All.Select(option => $"{option.Key.PadRight(NAME_WIDTH)}\t{OnOrOff(option.Value)}\n");

    public static IEnumerable<string> NamesThatAreOn(ShellOptions options) =>
        options.All.Where(option => option.Value).Select(option => $"{option.Key}\n");

    // What `set +o` prints: the commands that would put the table back the way it is.
    public static IEnumerable<string> Commands(ShellOptions options) =>
        options.All.Select(option => $"set {(option.Value ? "-o" : "+o")} {option.Key}\n");

    private static string OnOrOff(bool isSet) => isSet ? "on" : "off";

    public static AppletRun Reject(AppletContext context, string applet, string name, string what)
    {
        context.WriteError($"{applet}: {name}: invalid {what}\n");
        return AppletRun.Failed(1);
    }
}
