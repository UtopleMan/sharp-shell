using Sharp.Shell.Lexing;

namespace Sharp.Shell.Parsing;

public abstract record ShellNode;

public sealed record Assignment(string Name, Word Value);

public enum RedirectionKind
{
    Input,
    Output,
    Append,
    HereDocument,
    DuplicateOutput,
    DuplicateInput,
    OutputAndError,
}

// HereDocumentBody is filled by the lexer's here-document capture; it is null for every other kind.
// Expands is false when the delimiter was quoted (<< 'EOF'), which makes the body literal.
public sealed record Redirection(
    RedirectionKind Kind,
    int FileDescriptor,
    Word? Target,
    string? HereDocumentBody = null,
    bool StripsLeadingTabs = false,
    bool Expands = true);

public sealed record SimpleCommand(
    IReadOnlyList<Assignment> Assignments,
    IReadOnlyList<Word> Words,
    IReadOnlyList<Redirection> Redirections) : ShellNode;

public sealed record PipelineNode(IReadOnlyList<ShellNode> Stages, bool Negated) : ShellNode;

public enum AndOrKind
{
    And,
    Or,
}

public sealed record AndOrNode(ShellNode Left, AndOrKind Kind, ShellNode Right) : ShellNode;

public sealed record SequenceNode(IReadOnlyList<ShellNode> Items) : ShellNode;

public sealed record SubshellNode(ShellNode Body) : ShellNode;

public sealed record BraceGroupNode(ShellNode Body) : ShellNode;

public sealed record IfNode(IReadOnlyList<ConditionalBranch> Branches, ShellNode? ElseBody) : ShellNode;

public sealed record ConditionalBranch(ShellNode Condition, ShellNode Body);

public sealed record WhileNode(ShellNode Condition, ShellNode Body, bool UntilForm) : ShellNode;

public sealed record ForNode(string Variable, IReadOnlyList<Word> Items, ShellNode Body) : ShellNode;

public sealed record CaseArm(IReadOnlyList<Word> Patterns, ShellNode Body);

public sealed record CaseNode(Word Subject, IReadOnlyList<CaseArm> Arms) : ShellNode;
