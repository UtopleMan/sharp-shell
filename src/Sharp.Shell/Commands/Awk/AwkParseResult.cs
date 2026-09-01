namespace Sharp.Shell.Commands.Awk;

// Never throws. A construct this dialect will not implement is an UnsupportedConstruct that names it,
// so classification can escalate the whole command line instead of the applet answering differently
// from the awk the author meant; a malformed program is an Error, which awk reports itself.
internal sealed record AwkParseResult(AwkProgram? Program, string? UnsupportedConstruct, string? Error)
{
    public static AwkParseResult Parsed(AwkProgram program) => new(program, null, null);

    public static AwkParseResult Unsupported(string construct) => new(null, construct, null);

    public static AwkParseResult Failed(string error) => new(null, null, error);
}
