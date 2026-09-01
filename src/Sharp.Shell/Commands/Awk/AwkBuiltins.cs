namespace Sharp.Shell.Commands.Awk;

internal enum AwkBuiltin
{
    Length,
    Substr,
    Index,
    Split,
    Sub,
    Gsub,
    Match,
    Sprintf,
    Sin,
    Cos,
    Atan2,
    Exp,
    Log,
    Sqrt,
    Int,
    Rand,
    Srand,
    Tolower,
    Toupper,
    Close,
    Fflush,
}

// The names awk reserves. The gawk-only ones are recognised so the parser can refuse them by name
// rather than let them parse as calls to functions the program never defined, and `system` is
// recognised so it can be refused as a shell escape rather than as a typo.
internal static class AwkBuiltins
{
    private static readonly Dictionary<string, AwkBuiltin> Supported = new(StringComparer.Ordinal)
    {
        ["length"] = AwkBuiltin.Length,
        ["substr"] = AwkBuiltin.Substr,
        ["index"] = AwkBuiltin.Index,
        ["split"] = AwkBuiltin.Split,
        ["sub"] = AwkBuiltin.Sub,
        ["gsub"] = AwkBuiltin.Gsub,
        ["match"] = AwkBuiltin.Match,
        ["sprintf"] = AwkBuiltin.Sprintf,
        ["sin"] = AwkBuiltin.Sin,
        ["cos"] = AwkBuiltin.Cos,
        ["atan2"] = AwkBuiltin.Atan2,
        ["exp"] = AwkBuiltin.Exp,
        ["log"] = AwkBuiltin.Log,
        ["sqrt"] = AwkBuiltin.Sqrt,
        ["int"] = AwkBuiltin.Int,
        ["rand"] = AwkBuiltin.Rand,
        ["srand"] = AwkBuiltin.Srand,
        ["tolower"] = AwkBuiltin.Tolower,
        ["toupper"] = AwkBuiltin.Toupper,
        ["close"] = AwkBuiltin.Close,
        ["fflush"] = AwkBuiltin.Fflush,
    };

    private static readonly HashSet<string> Refused = new(StringComparer.Ordinal)
    {
        "system",
        "gensub",
        "asort",
        "asorti",
        "systime",
        "strftime",
        "mktime",
        "patsplit",
    };

    private static readonly HashSet<string> RefusedVariables = new(StringComparer.Ordinal)
    {
        "IGNORECASE",
        "RT",
        "FIELDWIDTHS",
        "FPAT",
        "PROCINFO",
    };

    public static bool IsBuiltin(string name) => Supported.ContainsKey(name) || Refused.Contains(name);

    public static bool IsRefusedBuiltin(string name) => Refused.Contains(name);

    public static bool IsRefusedVariable(string name) => RefusedVariables.Contains(name);

    public static bool TryResolve(string name, out AwkBuiltin builtin) => Supported.TryGetValue(name, out builtin);

    // Every builtin's argument count, as POSIX gives it. A call outside the range is a parse error,
    // which is what awk reports too.
    public static (int Minimum, int Maximum) ArityOf(AwkBuiltin builtin) => builtin switch
    {
        AwkBuiltin.Length => (0, 1),
        AwkBuiltin.Substr => (2, 3),
        AwkBuiltin.Index => (2, 2),
        AwkBuiltin.Split => (2, 3),
        AwkBuiltin.Sub or AwkBuiltin.Gsub => (2, 3),
        AwkBuiltin.Match => (2, 2),
        AwkBuiltin.Sprintf => (1, int.MaxValue),
        AwkBuiltin.Atan2 => (2, 2),
        AwkBuiltin.Sin or AwkBuiltin.Cos or AwkBuiltin.Exp or AwkBuiltin.Log
            or AwkBuiltin.Sqrt or AwkBuiltin.Int => (1, 1),
        AwkBuiltin.Rand => (0, 0),
        AwkBuiltin.Srand => (0, 1),
        AwkBuiltin.Tolower or AwkBuiltin.Toupper => (1, 1),
        AwkBuiltin.Close => (1, 1),
        AwkBuiltin.Fflush => (0, 1),
        _ => (0, int.MaxValue),
    };
}
