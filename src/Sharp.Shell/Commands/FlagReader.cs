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
        IReadOnlyList<string> bundleableFlags) =>
        ExpandShortFlagBundles(arguments, bundleableFlags, out _);

    // The overload classification uses. Expanding a bundle changes the argument list's length, so an
    // operand's position in the expanded list no longer indexes the word that produced it. Reporting
    // where each expanded entry came from is what lets the classifier walk back to that word and ask
    // whether it was literal.
    public static IReadOnlyList<string> ExpandShortFlagBundles(
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> bundleableFlags,
        out IReadOnlyList<int> sourcePositions)
    {
        if (bundleableFlags.Count == 0 || !arguments.Any(argument => IsBundle(argument, bundleableFlags)))
        {
            sourcePositions = [.. Enumerable.Range(0, arguments.Count)];
            return arguments;
        }

        List<string> expanded = [];
        List<int> sources = [];
        sourcePositions = sources;

        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "--")
            {
                expanded.AddRange(arguments.Skip(index));
                sources.AddRange(Enumerable.Range(index, arguments.Count - index));
                return expanded;
            }

            if (!IsBundle(arguments[index], bundleableFlags))
            {
                expanded.Add(arguments[index]);
                sources.Add(index);
                continue;
            }

            foreach (char letter in arguments[index].Skip(1))
            {
                expanded.Add($"-{letter}");
                sources.Add(index);
            }
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

    public static IReadOnlyList<string> Operands(IReadOnlyList<string> arguments, params string[] flagsTakingAValue) =>
        [.. PositionsOfOperands(arguments, flagsTakingAValue).Select(position => arguments[position])];

    // The same walk, answering where rather than what. Classification needs the position so it can
    // reach the unexpanded word behind an operand; the applets need the text.
    public static IReadOnlyList<int> PositionsOfOperands(
        IReadOnlyList<string> arguments,
        params string[] flagsTakingAValue)
    {
        List<int> positions = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            if (argument == "--")
            {
                positions.AddRange(Enumerable.Range(index + 1, arguments.Count - index - 1));
                return positions;
            }

            if (!IsFlag(argument))
            {
                positions.Add(index);
                continue;
            }

            if (flagsTakingAValue.Contains(argument, StringComparer.Ordinal))
            {
                index++;
            }
        }

        return positions;
    }
}
