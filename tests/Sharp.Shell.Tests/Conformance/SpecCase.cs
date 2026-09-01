namespace Sharp.Shell.Tests.Conformance;

// One `#### description` block from a vendored .test.sh, with the expectations that apply to bash.
//
// Each stream holds a *list* of acceptable values, not one: the corpus expresses alternatives with
// numbered qualifiers (## OK bash STDOUT: … ## OK-2 bash STDOUT: …), and a case passes when the
// actual output matches any of them. An empty list means the case asserts nothing on that stream.
public sealed record SpecCase(
    string File,
    int Index,
    string Description,
    string Body,
    IReadOnlyList<string> AcceptableStdout,
    IReadOnlyList<string> AcceptableStderr,
    IReadOnlyList<int> AcceptableStatus,
    bool IsUnparsed,
    string? UnparsedDirective)
{
    public string Id => $"{File}:{Index}";

    public override string ToString() => $"{Id} {Description}";
}
