namespace Sharp.Shell.Commands;

// Shared argument walking. Applets are conservative by contract, so the common shape is "these
// flags and no others" — expressed once here rather than re-derived in every applet.
internal static class FlagReader
{
    public static bool IsFlag(string argument) => argument.Length > 1 && argument[0] == '-';

    // POSIX short flags bundle: `grep -rn` means `-r -n`. Without this every applet refuses its own
    // supported flags whenever they arrive bundled, and Rule 1 escalates a command the sandbox can
    // perfectly well run. Measured against this project's own stored sessions, bundling alone was
    // 36% of all escalations.
    //
    // Expansion is per applet, driven by IApplet.BundleableFlags, and never a general rule: `find`
    // spells its options `-name` and `-maxdepth`, which are single-dash words, not bundles. A group
    // expands only when *every* letter in it is a single-character flag the applet implements, so
    // `ls -la` stays whole (ls does not implement -l) and is refused as it should be. Long options,
    // digits (-A6, -5) and anything after `--` are untouched.
    public static IReadOnlyList<string> ExpandShortFlagBundles(
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> bundleableFlags)
    {
        if (bundleableFlags.Count == 0 || !arguments.Any(argument => IsBundle(argument, bundleableFlags)))
        {
            return arguments;
        }

        List<string> expanded = [];

        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "--")
            {
                expanded.AddRange(arguments.Skip(index));
                return expanded;
            }

            if (!IsBundle(arguments[index], bundleableFlags))
            {
                expanded.Add(arguments[index]);
                continue;
            }

            expanded.AddRange(arguments[index].Skip(1).Select(letter => $"-{letter}"));
        }

        return expanded;
    }

    private static bool IsBundle(string argument, IReadOnlyList<string> bundleableFlags) =>
        argument.Length > 2
        && argument[0] == '-'
        && argument[1] != '-'
        && argument.Skip(1).All(letter => bundleableFlags.Contains($"-{letter}", StringComparer.Ordinal));

    public static FlagSupport RejectUnknownFlags(IReadOnlyList<string> arguments, params string[] supported)
    {
        foreach (string argument in arguments)
        {
            if (argument == "--")
            {
                return FlagSupport.Supported;
            }

            if (IsFlag(argument) && !supported.Contains(argument, StringComparer.Ordinal))
            {
                return FlagSupport.Reject(argument);
            }
        }

        return FlagSupport.Supported;
    }

    public static IReadOnlyList<string> Operands(IReadOnlyList<string> arguments, params string[] flagsTakingAValue)
    {
        List<string> operands = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            if (argument == "--")
            {
                operands.AddRange(arguments.Skip(index + 1));
                return operands;
            }

            if (!IsFlag(argument))
            {
                operands.Add(argument);
                continue;
            }

            if (flagsTakingAValue.Contains(argument, StringComparer.Ordinal))
            {
                index++;
            }
        }

        return operands;
    }
}
