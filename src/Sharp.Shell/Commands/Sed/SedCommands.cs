using System.Text.RegularExpressions;

namespace Sharp.Shell.Commands.Sed;

// The program is flat rather than nested: `b` and `t` jump to a label anywhere in the script,
// including into and out of a block, so execution needs an instruction list with indices. A block
// keeps the index just past its body and jumps there when its address does not match.
internal abstract record SedCommand(SedRange? Range);

internal sealed record SedSubstitute(
    SedRange? Range,
    Regex? Pattern,
    IReadOnlyList<SedReplacementPart> Replacement,
    bool IsGlobal,
    int Occurrence,
    bool Prints,
    string? WriteFile) : SedCommand(Range);

internal sealed record SedTransliterate(SedRange? Range, string From, string To) : SedCommand(Range);

internal sealed record SedPrint(SedRange? Range, bool FirstLineOnly) : SedCommand(Range);

internal sealed record SedDelete(SedRange? Range, bool FirstLineOnly) : SedCommand(Range);

internal sealed record SedNextLine(SedRange? Range, bool Appends) : SedCommand(Range);

internal enum SedHoldAction
{
    CopyToHold,
    AppendToHold,
    CopyFromHold,
    AppendFromHold,
    Exchange,
}

internal sealed record SedHold(SedRange? Range, SedHoldAction Action) : SedCommand(Range);

internal enum SedBranchCondition
{
    Always,
    IfSubstituted,
    IfNotSubstituted,
}

internal sealed record SedBranch(SedRange? Range, string Label, SedBranchCondition Condition) : SedCommand(Range);

internal sealed record SedLabel(string Name) : SedCommand((SedRange?)null);

internal sealed record SedLineNumber(SedRange? Range) : SedCommand(Range);

internal enum SedTextPlacement
{
    Append,
    Insert,
    Change,
}

internal sealed record SedText(SedRange? Range, SedTextPlacement Placement, string Text) : SedCommand(Range);

internal sealed record SedReadFile(SedRange? Range, string Path, bool OneLineOnly) : SedCommand(Range);

internal sealed record SedWriteFile(SedRange? Range, string Path, bool FirstLineOnly) : SedCommand(Range);

internal sealed record SedList(SedRange? Range, int Width) : SedCommand(Range);

internal sealed record SedQuit(SedRange? Range, int ExitCode, bool Silent) : SedCommand(Range);

internal sealed record SedZap(SedRange? Range) : SedCommand(Range);

internal sealed record SedFileName(SedRange? Range) : SedCommand(Range);

internal sealed record SedBlockStart(SedRange? Range, int EndIndex) : SedCommand(Range);

internal sealed record SedNoOperation(SedRange? Range) : SedCommand(Range);
