namespace Sharp.Shell.Commands.Awk;

// POSIX's comparison table in one line: numeric when neither side is a plain string, string
// otherwise. Everything interesting follows from which values count as plain strings —
// `AwkValue.ComparesNumerically` is where that is decided.
internal static class AwkComparison
{
    public static int Compare(AwkValue left, AwkValue right, string convertFormat) =>
        left.ComparesNumerically && right.ComparesNumerically
            ? left.ToNumber().CompareTo(right.ToNumber())
            : string.CompareOrdinal(left.ToStringWith(convertFormat), right.ToStringWith(convertFormat));
}
