using Sharp.Shell.Parsing;
using Xunit;

namespace Sharp.Shell.Tests;

public class ParserTests
{
    private static ShellNode Parsed(string source)
    {
        ParseResult result = Parser.Parse(source);
        Assert.Null(result.UnsupportedReason);
        Assert.NotNull(result.Program);
        return result.Program!;
    }

    private static string Unsupported(string source)
    {
        ParseResult result = Parser.Parse(source);
        Assert.Null(result.Program);
        Assert.NotNull(result.UnsupportedReason);
        return result.UnsupportedReason!;
    }

    private static SimpleCommand OnlyCommand(string source) => Assert.IsType<SimpleCommand>(Parsed(source));

    [Fact]
    public void ParsesASimpleCommandIntoWords()
    {
        SimpleCommand command = OnlyCommand("echo hi there");

        Assert.Equal(["echo", "hi", "there"], command.Words.Select(word => word.LiteralText));
        Assert.Empty(command.Assignments);
        Assert.Empty(command.Redirections);
    }

    [Fact]
    public void LeadingAssignmentsAreSeparatedFromWords()
    {
        SimpleCommand command = OnlyCommand("x=1 y=2 echo hi");

        Assert.Equal(["x", "y"], command.Assignments.Select(assignment => assignment.Name));
        Assert.Equal(["1", "2"], command.Assignments.Select(assignment => assignment.Value.LiteralText));
        Assert.Equal(["echo", "hi"], command.Words.Select(word => word.LiteralText));
    }

    [Fact]
    public void AnAssignmentAfterTheCommandNameIsAnOrdinaryArgument()
    {
        SimpleCommand command = OnlyCommand("echo x=1");

        Assert.Empty(command.Assignments);
        Assert.Equal(["echo", "x=1"], command.Words.Select(word => word.LiteralText));
    }

    [Fact]
    public void ParsesRedirections()
    {
        SimpleCommand command = OnlyCommand("cmd < in > out 2>> log");

        Assert.Equal(
            [RedirectionKind.Input, RedirectionKind.Output, RedirectionKind.Append],
            command.Redirections.Select(redirection => redirection.Kind));
        Assert.Equal([0, 1, 2], command.Redirections.Select(redirection => redirection.FileDescriptor));
        Assert.Equal(["in", "out", "log"], command.Redirections.Select(redirection => redirection.Target!.LiteralText));
        Assert.Equal(["cmd"], command.Words.Select(word => word.LiteralText));
    }

    [Fact]
    public void ParsesAPipelineIntoStages()
    {
        PipelineNode pipeline = Assert.IsType<PipelineNode>(Parsed("a | b | c"));

        Assert.Equal(3, pipeline.Stages.Count);
        Assert.False(pipeline.Negated);
    }

    [Fact]
    public void BangNegatesThePipeline()
    {
        PipelineNode pipeline = Assert.IsType<PipelineNode>(Parsed("! a | b"));

        Assert.True(pipeline.Negated);
        Assert.Equal(2, pipeline.Stages.Count);
    }

    [Fact]
    public void AndOrIsLeftAssociative()
    {
        AndOrNode root = Assert.IsType<AndOrNode>(Parsed("a && b || c"));

        Assert.Equal(AndOrKind.Or, root.Kind);
        AndOrNode left = Assert.IsType<AndOrNode>(root.Left);
        Assert.Equal(AndOrKind.And, left.Kind);
        Assert.Equal(["a"], Assert.IsType<SimpleCommand>(left.Left).Words.Select(word => word.LiteralText));
    }

    [Fact]
    public void SemicolonsAndNewlinesSequenceCommands()
    {
        SequenceNode sequence = Assert.IsType<SequenceNode>(Parsed("a; b\nc"));

        Assert.Equal(3, sequence.Items.Count);
    }

    [Fact]
    public void TrailingSeparatorsDoNotProduceEmptyItems()
    {
        SequenceNode sequence = Assert.IsType<SequenceNode>(Parsed("a;\n\nb;\n"));

        Assert.Equal(2, sequence.Items.Count);
    }

    [Fact]
    public void EmptyInputParsesToAnEmptySequence()
    {
        SequenceNode sequence = Assert.IsType<SequenceNode>(Parsed("   \n  "));

        Assert.Empty(sequence.Items);
    }

    [Fact]
    public void ALexErrorSurfacesAsAnUnsupportedReason()
    {
        Assert.Contains("unterminated", Unsupported("echo 'oops"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sleep 1 &", "background")]
    [InlineData("diff <(a) <(b)", "process substitution")]
    [InlineData("cat >(tee log)", "process substitution")]
    [InlineData("echo $PPID", "$PPID")]
    [InlineData("trap 'x' EXIT", "trap")]
    [InlineData("jobs", "jobs")]
    [InlineData("fg %1", "fg")]
    [InlineData("bg %1", "bg")]
    [InlineData("wait", "wait")]
    [InlineData("disown", "disown")]
    [InlineData("coproc x { y; }", "coproc")]
    [InlineData("exec git status", "exec")]
    public void ConstructsWithNoMeaningWithoutProcessesAreUnsupported(string source, string mentioned)
    {
        Assert.Contains(mentioned, Unsupported(source), StringComparison.Ordinal);
    }

    [Fact]
    public void BareExecWithNoProgramIsNotRejected()
    {
        Assert.Equal(["exec"], OnlyCommand("exec").Words.Select(word => word.LiteralText));
    }

    [Theory]
    [InlineData("if true; then echo y; fi", typeof(IfNode))]
    [InlineData("while true; do echo y; done", typeof(WhileNode))]
    [InlineData("until false; do echo y; done", typeof(WhileNode))]
    [InlineData("for i in a b; do echo $i; done", typeof(ForNode))]
    [InlineData("case x in x) echo m;; esac", typeof(CaseNode))]
    [InlineData("( echo sub )", typeof(SubshellNode))]
    [InlineData("{ echo group; }", typeof(BraceGroupNode))]
    public void ParsesEachCompoundCommand(string source, Type expected)
    {
        Assert.IsType(expected, Parsed(source));
    }

    [Theory]
    [InlineData("select x in a; do echo $x; done", typeof(SelectNode))]
    [InlineData("f() { echo x; }", typeof(FunctionDefinition))]
    [InlineData("function f { echo x; }", typeof(FunctionDefinition))]
    [InlineData("function f() { echo x; }", typeof(FunctionDefinition))]
    [InlineData("[[ -f x ]]", typeof(ConditionNode))]
    public void ParsesTheConstructsThatNeedNoProcessModel(string source, Type expected)
    {
        Assert.IsType(expected, Parsed(source));
    }

    [Theory]
    [InlineData("if true; then echo y")]
    [InlineData("while true; do echo y")]
    [InlineData("case x in x) echo m")]
    [InlineData("( echo sub")]
    public void AnUnterminatedCompoundIsAParseFailure(string source)
    {
        Assert.NotEmpty(Unsupported(source));
    }

    [Fact]
    public void APipeWithNothingAfterItIsAParseError()
    {
        Assert.NotEmpty(Unsupported("echo hi |"));
    }
}
