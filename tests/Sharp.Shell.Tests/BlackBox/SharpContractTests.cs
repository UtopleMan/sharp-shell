using Xunit;

namespace Sharp.Shell.Tests.BlackBox;

// The binary's own contract, exercised through a real process. None of this is reachable from the
// library tests: argument parsing, which stream each thing lands on, and the exit status the shell
// hands back to whoever invoked it.
public sealed class SharpContractTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sharp-contract").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private SharpResult Run(string command, params string[] options)
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        return SharpBinary.RunCommand(command, root, options);
    }

    [Fact]
    public void Dash_c_runs_one_command_and_writes_only_its_output()
    {
        SharpResult result = Run("echo hello");

        Assert.Equal("hello\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void The_exit_status_reaches_the_caller()
    {
        Assert.Equal(0, Run("true").ExitCode);
        Assert.Equal(1, Run("false").ExitCode);
        Assert.Equal(7, Run("exit 7").ExitCode);
    }

    [Fact]
    public void Stderr_and_stdout_stay_separate()
    {
        SharpResult result = Run("echo out; cat missing.txt");

        Assert.Equal("out\n", result.Stdout);
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_byte_exact_with_no_added_newline()
    {
        Assert.Equal("x", Run("printf '%s' x").Stdout);
    }

    // A command is not always a line. Reading a script a line at a time runs fragments: the body of
    // an `if` executes without its condition ever being evaluated, and a here-document's text is
    // taken for commands. Every case here spans lines on purpose, and each is asserted twice —
    // from a file and from a pipe — because those are two readers of the same input.
    [Theory]
    [InlineData("echo first\nfor i in a b; do echo $i; done\n", "first\na\nb\n")]
    [InlineData("greet() {\n  echo hi\n}\ngreet\n", "hi\n")]
    [InlineData("function greet {\n  echo hi\n}\ngreet\n", "hi\n")]
    [InlineData("show() {\n  echo $1\n}\nshow a\nshow b\n", "a\nb\n")]
    [InlineData("if false; then\n  echo taken\nfi\necho after\n", "after\n")]
    [InlineData("if true; then\n  echo taken\nelse\n  echo other\nfi\n", "taken\n")]
    [InlineData("f=notes.cs\nif [[ $f == *.cs ]]; then\n  echo cs\nfi\n", "cs\n")]
    [InlineData("for i in a b; do\n  echo $i\ndone\n", "a\nb\n")]
    [InlineData("i=0\nwhile [ $i -lt 2 ]; do\n  i=$((i+1))\ndone\necho $i\n", "2\n")]
    [InlineData("case x in\n  x) echo matched ;;\nesac\n", "matched\n")]
    [InlineData("cat << EOF\nbody line\nEOF\necho finished\n", "body line\nfinished\n")]
    [InlineData("echo a |\n  tr a-z A-Z\n", "A\n")]
    [InlineData("check() {\n  if [ -n \"$1\" ]; then\n    echo set\n  fi\n}\ncheck x\n", "set\n")]
    [InlineData("echo 'one\ntwo'\n", "one\ntwo\n")]
    [InlineData("echo foo\\\nbar\n", "foobar\n")]
    public void A_multi_line_command_is_read_whole(string script, string expected)
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        string path = Path.Combine(root, "run.sh");
        File.WriteAllText(path, script);

        SharpResult fromFile = SharpBinary.RunScript(path, root, "--strict");
        SharpResult fromPipe = SharpBinary.RunPiped(script, root, "--strict");

        Assert.Equal(expected, fromFile.Stdout);
        Assert.Equal(expected, fromPipe.Stdout);
        Assert.Equal(0, fromFile.ExitCode);
        Assert.Equal(0, fromPipe.ExitCode);
    }

    [Fact]
    public void A_piped_script_runs_the_same_way()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");

        SharpResult result = SharpBinary.RunPiped("echo piped\nexit 3\necho never\n", root);

        Assert.Equal("piped\n", result.Stdout);
        Assert.Equal(3, result.ExitCode);
    }

    // bash stops a non-interactive shell at its first syntax error: the offending command does not
    // run and neither does anything after it. Each case here was checked against real bash for both
    // the output and the status.
    [Theory]
    [InlineData("echo one\nfi\necho three\n", "unexpected 'fi'")]
    [InlineData("echo one\necho \"unterminated\necho three\n", "unterminated double quote")]
    [InlineData("echo one\nesac\necho three\n", "unexpected 'esac'")]
    [InlineData("echo one\necho >\necho three\n", "needs a target")]
    public void A_syntax_error_stops_the_script_where_bash_would(string script, string mentioned)
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        string path = Path.Combine(root, "bad.sh");
        File.WriteAllText(path, script);

        SharpResult result = SharpBinary.RunScript(path, root, "--strict");

        Assert.Equal("one\n", result.Stdout);
        Assert.Contains(mentioned, result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    // An unfinished command at the end of the input is a syntax error too — bash calls it
    // "unexpected end of file" and exits 2 — so it reports itself rather than vanishing.
    [Fact]
    public void A_script_that_ends_mid_command_stops_with_its_reason()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        string path = Path.Combine(root, "truncated.sh");
        File.WriteAllText(path, "echo start\nif true; then\n  echo body\n");

        SharpResult result = SharpBinary.RunScript(path, root, "--strict");

        Assert.Equal("start\n", result.Stdout);
        Assert.Contains("expected 'fi'", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    // The other half of the rule, and the reason a syntax error cannot simply mean "did not parse":
    // `trap` is valid bash this shell does not implement. bash runs straight past it, so this does
    // too — the line is handed over, not refused as malformed.
    [Fact]
    public void A_construct_this_shell_does_not_implement_does_not_stop_the_script()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        string path = Path.Combine(root, "unsupported.sh");
        File.WriteAllText(path, "echo one\ntrap 'x' EXIT\necho three\n");

        SharpResult result = SharpBinary.RunScript(path, root, "--strict");

        Assert.Equal("one\nthree\n", result.Stdout);
        Assert.Contains("trap", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Dash_c_reports_a_syntax_error_the_same_way()
    {
        SharpResult result = Run("fi");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("unexpected 'fi'", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void State_persists_between_lines_unlike_the_sandboxed_tool()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        SharpResult result = SharpBinary.RunPiped("x=kept\ncd sub\necho $x\npwd\n", root);

        Assert.Equal($"kept\n{Path.Combine(root, "sub")}\n", result.Stdout);
    }

    [Fact]
    public void Strict_mode_refuses_to_leave_the_owned_command_set()
    {
        SharpResult result = Run("git status", "--strict");

        Assert.Equal(126, result.ExitCode);
        Assert.Contains("git", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stdout);
    }

    [Fact]
    public void Explain_reports_the_tier_on_stderr_and_leaves_stdout_clean()
    {
        SharpResult result = Run("echo hi", "--explain");

        Assert.Equal("hi\n", result.Stdout);
        Assert.Contains("[owned]", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_names_the_program_that_forced_an_escape()
    {
        SharpResult result = Run("git status", "--explain", "--strict");

        Assert.Contains("[native git]", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void The_root_confines_the_shell()
    {
        SharpResult result = Run("cat /etc/passwd");

        Assert.Equal(string.Empty, result.Stdout);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void An_unknown_option_is_a_usage_error_not_a_crash()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");

        SharpResult result = SharpBinary.Run(["--nonsense"], root, standardInput: null);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("usage", result.Stderr, StringComparison.Ordinal);
    }
}
