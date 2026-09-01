namespace Sharp.Shell.Text;

// awk's view of a translated pattern. Where sed refuses an alternation whose branches can match
// different lengths — because .NET would answer leftmost-first and POSIX wants leftmost-longest —
// awk translates it anyway and sets RequiresLongestScan, because an applet that escalates on
// `/foo|foobar/` would escalate on most real awk programs.
//
// Never throws: an untranslatable pattern is a value, so the caller can turn it into a refusal.
public sealed record PosixRegexProgram(
    string? Pattern,
    bool RequiresLongestScan,
    string? RefusalReason)
{
    public bool IsTranslated => Pattern is not null;

    // Present exactly when RequiresLongestScan is set: the fully anchored probes that recover the
    // POSIX extent once .NET has reported where the match starts.
    internal LongestMatchFinder? Extent { get; init; }

    public static PosixRegexProgram Refuse(string reason) => new(null, false, reason);

    internal static PosixRegexProgram Translated(string pattern, LongestMatchFinder? extent) =>
        new(pattern, extent is not null, null) { Extent = extent };
}
