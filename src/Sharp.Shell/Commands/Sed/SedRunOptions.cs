namespace Sharp.Shell.Commands.Sed;

internal sealed record SedRunOptions(
    bool AutoPrints = true,
    char Separator = '\n',
    string FileName = "-",
    int ListWidth = 70,
    bool PrintsPatternSpaceOnMissingNextLine = true);
