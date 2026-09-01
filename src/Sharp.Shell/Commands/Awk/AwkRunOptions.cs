namespace Sharp.Shell.Commands.Awk;

// What the command line told the interpreter, separated from the program itself so the interpreter
// can be driven directly by a test.
internal sealed record AwkRunOptions(
    string? FieldSeparator,
    IReadOnlyList<string> Assignments,
    IReadOnlyList<string> Operands,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    public static AwkRunOptions Empty { get; } =
        new(null, [], [], new Dictionary<string, string>(StringComparer.Ordinal));
}
