using System.Text.RegularExpressions;

namespace Sharp.Shell.Commands.Sed;

internal enum SedAddressKind
{
    Line,
    Last,
    Regex,
    Step,
    Zero,
}

// A regex address with a null Pattern is sed's empty `//`, which means "the last regular expression
// used" and can only be resolved while running.
internal sealed record SedAddress(SedAddressKind Kind, int Line = 0, int Step = 0, Regex? Pattern = null);

internal enum SedRangeEndKind
{
    None,
    Address,
    RelativeLines,
    NextMultiple,
}

internal sealed record SedRange(
    SedAddress Start,
    SedRangeEndKind EndKind = SedRangeEndKind.None,
    SedAddress? End = null,
    int EndValue = 0,
    bool IsNegated = false)
{
    public bool IsRange => EndKind != SedRangeEndKind.None;
}

internal enum SedReplacementKind
{
    Literal,
    WholeMatch,
    Group,
    UpperCaseRest,
    LowerCaseRest,
    UpperCaseNext,
    LowerCaseNext,
    EndCaseConversion,
}

// The replacement is kept as parts rather than as a .NET substitution string: sed spells the whole
// match `&` and a group `\1`, .NET spells them `$0` and `$1`, and GNU's `\U`/`\L`/`\u`/`\l`/`\E`
// have no .NET equivalent at all.
internal sealed record SedReplacementPart(SedReplacementKind Kind, string Text = "", int Group = 0);

internal sealed record SedProgram(
    IReadOnlyList<SedCommand> Commands,
    IReadOnlyDictionary<string, int> Labels,
    bool SuppressesAutoPrint);

// Two failure channels, because they lead to opposite behaviours. Error is "this is not sed" and
// becomes exit 2 with a message. UnsupportedConstruct is "this is sed we do not own" and escalates
// the whole command line to the native tier.
internal sealed record SedParseResult(SedProgram? Program, string? Error, string? UnsupportedConstruct)
{
    public static SedParseResult Parsed(SedProgram program) => new(program, null, null);

    public static SedParseResult Invalid(string error) => new(null, error, null);

    public static SedParseResult Unsupported(string construct) => new(null, null, construct);
}

internal sealed record SedParseOptions(bool IsExtendedRegex = false, bool AllowsGnuExtensions = true);
