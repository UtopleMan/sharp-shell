namespace Sharp.Shell.Commands.Awk;

// The parsed shape of an awk program. Immutable, so the interpreter can walk it without a copy and
// the classifier can ask questions of it — does it redirect output? — without running anything.

internal enum AwkBinaryOperator
{
    Or,
    And,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,
    Concatenate,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Power,
}

internal enum AwkUnaryOperator
{
    Negate,
    Plus,
    Not,
}

internal enum AwkAssignOperator
{
    Assign,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Power,
}

internal enum AwkRedirectionKind
{
    Truncate,
    Append,
}

internal abstract record AwkExpression;

internal sealed record AwkNumberLiteral(double Value) : AwkExpression;

internal sealed record AwkStringLiteral(string Value) : AwkExpression;

// A regular expression used as a value means `$0 ~ /re/`, which is what makes `/x/ && /y/` a pattern
// and `n = /x/` a zero or a one.
internal sealed record AwkRegexLiteral(string Pattern) : AwkExpression;

internal sealed record AwkVariable(string Name) : AwkExpression;

internal sealed record AwkField(AwkExpression Index) : AwkExpression;

internal sealed record AwkArrayElement(string Name, IReadOnlyList<AwkExpression> Subscripts) : AwkExpression;

internal sealed record AwkGrouping(AwkExpression Inner) : AwkExpression;

internal sealed record AwkAssignment(AwkExpression Target, AwkAssignOperator Operator, AwkExpression Value)
    : AwkExpression;

internal sealed record AwkTernary(AwkExpression Condition, AwkExpression WhenTrue, AwkExpression WhenFalse)
    : AwkExpression;

internal sealed record AwkBinary(AwkExpression Left, AwkBinaryOperator Operator, AwkExpression Right)
    : AwkExpression;

internal sealed record AwkUnary(AwkUnaryOperator Operator, AwkExpression Operand) : AwkExpression;

internal sealed record AwkIncrement(AwkExpression Target, bool IsPrefix, bool IsDecrement) : AwkExpression;

internal sealed record AwkMatchExpression(AwkExpression Subject, AwkExpression Pattern, bool IsNegated)
    : AwkExpression;

internal sealed record AwkMembership(IReadOnlyList<AwkExpression> Subscripts, string ArrayName) : AwkExpression;

// Only ever the left side of `in`: `(i, j) in counts` is a two-part subscript, not a pair of values.
internal sealed record AwkSubscriptList(IReadOnlyList<AwkExpression> Subscripts) : AwkExpression;

internal sealed record AwkCall(string Name, IReadOnlyList<AwkExpression> Arguments) : AwkExpression;

internal sealed record AwkBuiltinCall(AwkBuiltin Builtin, IReadOnlyList<AwkExpression> Arguments) : AwkExpression;

internal sealed record AwkRedirection(AwkRedirectionKind Kind, AwkExpression Target);

internal abstract record AwkStatement;

internal sealed record AwkBlock(IReadOnlyList<AwkStatement> Statements) : AwkStatement;

internal sealed record AwkExpressionStatement(AwkExpression Expression) : AwkStatement;

internal sealed record AwkIfStatement(AwkExpression Condition, AwkStatement Then, AwkStatement? Else)
    : AwkStatement;

internal sealed record AwkWhileStatement(AwkExpression Condition, AwkStatement Body) : AwkStatement;

internal sealed record AwkDoWhileStatement(AwkStatement Body, AwkExpression Condition) : AwkStatement;

internal sealed record AwkForStatement(
    AwkStatement? Initialiser,
    AwkExpression? Condition,
    AwkStatement? Update,
    AwkStatement Body) : AwkStatement;

internal sealed record AwkForInStatement(string VariableName, string ArrayName, AwkStatement Body) : AwkStatement;

internal sealed record AwkBreakStatement : AwkStatement;

internal sealed record AwkContinueStatement : AwkStatement;

internal sealed record AwkNextStatement : AwkStatement;

internal sealed record AwkNextFileStatement : AwkStatement;

internal sealed record AwkExitStatement(AwkExpression? Status) : AwkStatement;

internal sealed record AwkReturnStatement(AwkExpression? Value) : AwkStatement;

// `delete a` with no subscripts empties the whole array.
internal sealed record AwkDeleteStatement(string ArrayName, IReadOnlyList<AwkExpression> Subscripts) : AwkStatement;

internal sealed record AwkPrintStatement(IReadOnlyList<AwkExpression> Arguments, AwkRedirection? Redirection)
    : AwkStatement;

internal sealed record AwkPrintfStatement(IReadOnlyList<AwkExpression> Arguments, AwkRedirection? Redirection)
    : AwkStatement;

internal sealed record AwkEmptyStatement : AwkStatement;

internal abstract record AwkPattern;

internal sealed record AwkBeginPattern : AwkPattern;

internal sealed record AwkEndPattern : AwkPattern;

internal sealed record AwkExpressionPattern(AwkExpression Expression) : AwkPattern;

internal sealed record AwkRangePattern(AwkExpression From, AwkExpression To) : AwkPattern;

// A rule with no pattern runs for every record; a rule with no action prints the record.
internal sealed record AwkRule(AwkPattern? Pattern, AwkBlock? Action);

internal sealed record AwkFunction(string Name, IReadOnlyList<string> Parameters, AwkBlock Body);

internal sealed record AwkProgram(
    IReadOnlyList<AwkRule> Rules,
    IReadOnlyDictionary<string, AwkFunction> Functions);
