using Sharp;
using Xunit;

namespace Sharp.Shell.Tests;

// The binary reads a script a line at a time; a command is not always a line. This is the join,
// tested in-process where a coverage collector can see it — the black-box script tests prove the
// same behaviour through a real process.
//
// The distinction that matters throughout: *unfinished* is not *wrong*. `if true; then` wants
// another line; `fi` on its own is a syntax error and must run immediately, so it reports itself
// where it happened rather than swallowing the rest of the file waiting for a terminator.
public class CommandReaderTests
{
    [Theory]
    [InlineData("echo hi")]
    [InlineData("for i in a b; do echo $i; done")]
    [InlineData("x=1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# just a comment")]
    public void AWholeCommandOnOneLineIsCompleteAtOnce(string line)
    {
        CommandReader reader = new();

        Assert.True(reader.TryComplete(line, out string command));
        Assert.Equal($"{line}\n", command);
        Assert.False(reader.IsContinuing);
    }

    // Wrong, not unfinished: these run straight away so their error lands on the line that caused it.
    [Theory]
    [InlineData("fi")]
    [InlineData("done")]
    [InlineData("esac")]
    [InlineData("trap 'x' EXIT")]
    [InlineData("sleep 1 &")]
    [InlineData("echo >")]
    public void ALineThatIsWrongRatherThanUnfinishedIsCompleteAtOnce(string line)
    {
        CommandReader reader = new();

        Assert.True(reader.TryComplete(line, out _));
        Assert.False(reader.IsContinuing);
    }

    [Theory]
    [InlineData("if true; then", "  echo x", "fi")]
    [InlineData("if true; then", "  echo x", "else", "  echo y", "fi")]
    [InlineData("while true; do", "  echo x", "done")]
    [InlineData("for i in a b; do", "  echo $i", "done")]
    [InlineData("case x in", "  x) echo m ;;", "esac")]
    [InlineData("greet() {", "  echo hi", "}")]
    [InlineData("function greet {", "  echo hi", "}")]
    [InlineData("(", "  echo sub", ")")]
    [InlineData("[[ -f x", "]]")]
    [InlineData("echo a |", "  tr a-z A-Z")]
    [InlineData("echo a &&", "  echo b")]
    [InlineData("echo 'one", "two'")]
    [InlineData("echo \"one", "two\"")]
    [InlineData("cat << EOF", "body", "EOF")]
    [InlineData("echo foo\\", "bar")]
    [InlineData("echo a \\", "  b \\", "  c")]
    public void LinesAreJoinedUntilTheCommandIsWhole(params string[] lines)
    {
        CommandReader reader = new();

        foreach (string line in lines[..^1])
        {
            Assert.False(reader.TryComplete(line, out _));
            Assert.True(reader.IsContinuing);
        }

        Assert.True(reader.TryComplete(lines[^1], out string command));
        Assert.Equal(string.Concat(lines.Select(line => $"{line}\n")), command);
        Assert.False(reader.IsContinuing);
    }

    // The here-document is the case that parses cleanly while still being unfinished: `cat << EOF`
    // is a valid command waiting for a body, so completeness cannot be read off the parse alone.
    [Fact]
    public void AHereDocumentWaitsForItsDelimiter()
    {
        CommandReader reader = new();

        Assert.False(reader.TryComplete("cat << EOF", out _));
        Assert.False(reader.TryComplete("body line", out _));
        Assert.True(reader.TryComplete("EOF", out string command));
        Assert.Equal("cat << EOF\nbody line\nEOF\n", command);
    }

    [Fact]
    public void TheNextCommandStartsCleanAfterOneCompletes()
    {
        CommandReader reader = new();
        reader.TryComplete("if true; then", out _);
        reader.TryComplete("  echo x", out _);
        reader.TryComplete("fi", out _);

        Assert.True(reader.TryComplete("echo after", out string command));
        Assert.Equal("echo after\n", command);
    }

    [Fact]
    public void NothingIsReturnedWhileTheCommandIsUnfinished()
    {
        CommandReader reader = new();

        Assert.False(reader.TryComplete("if true; then", out string command));
        Assert.Equal(string.Empty, command);
    }

    // What the caller runs when the input ends mid-command, so the fragment reports its own
    // unterminated-construct error rather than disappearing.
    [Fact]
    public void AnUnfinishedCommandIsStillAvailableAtEndOfInput()
    {
        CommandReader reader = new();
        reader.TryComplete("if true; then", out _);
        reader.TryComplete("  echo x", out _);

        Assert.True(reader.IsContinuing);
        Assert.Equal("if true; then\n  echo x\n", reader.Pending);
    }

    [Fact]
    public void NestedConstructsCloseFromTheInsideOut()
    {
        CommandReader reader = new();
        string[] lines = ["check() {", "  if [ -n \"$1\" ]; then", "    echo set", "  fi", "}"];

        foreach (string line in lines[..^1])
        {
            Assert.False(reader.TryComplete(line, out _));
        }

        Assert.True(reader.TryComplete(lines[^1], out _));
    }

    // Malformed input, which stops a non-interactive shell where it stands.
    [Theory]
    [InlineData("fi")]
    [InlineData("esac")]
    [InlineData("done")]
    [InlineData("echo >")]
    [InlineData("echo 'unterminated")]
    [InlineData("if true; then\n  echo x\n")]
    public void MalformedInputIsASyntaxError(string command) =>
        Assert.NotNull(CommandReader.SyntaxErrorIn(command));

    // Valid bash this shell does not implement. Not malformed, so a script runs on past it — the
    // line goes to a real shell instead.
    [Theory]
    [InlineData("trap 'x' EXIT")]
    [InlineData("sleep 1 &")]
    [InlineData("jobs")]
    [InlineData("coproc x { y; }")]
    [InlineData("exec git status")]
    [InlineData("diff <(a) <(b)")]
    [InlineData("echo $PPID")]
    public void AnUnimplementedConstructIsNotASyntaxError(string command) =>
        Assert.Null(CommandReader.SyntaxErrorIn(command));

    [Theory]
    [InlineData("echo hi")]
    [InlineData("if true; then echo x; fi")]
    [InlineData("")]
    [InlineData("# comment")]
    public void AWholeCommandIsNotASyntaxError(string command) =>
        Assert.Null(CommandReader.SyntaxErrorIn(command));

    // Ctrl+C at a continuation prompt. Without this an unterminated quote traps the shell: every
    // line after it, `exit` included, is swallowed into a command that can never complete.
    [Fact]
    public void AbandoningThrowsAwayTheHalfTypedCommand()
    {
        CommandReader reader = new();
        reader.TryComplete("echo \"oops", out _);
        Assert.True(reader.IsContinuing);

        reader.Abandon();

        Assert.False(reader.IsContinuing);
        Assert.Equal(string.Empty, reader.Pending);
        Assert.True(reader.TryComplete("exit", out string command));
        Assert.Equal("exit\n", command);
    }
}
