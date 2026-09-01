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

    [Fact]
    public void A_script_file_runs_line_by_line()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");
        string script = Path.Combine(root, "run.sh");
        File.WriteAllText(script, "echo first\nfor i in a b; do echo $i; done\n");

        SharpResult result = SharpBinary.RunScript(script, root);

        Assert.Equal("first\na\nb\n", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void A_piped_script_runs_the_same_way()
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");

        SharpResult result = SharpBinary.RunPiped("echo piped\nexit 3\necho never\n", root);

        Assert.Equal("piped\n", result.Stdout);
        Assert.Equal(3, result.ExitCode);
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

        Assert.Equal(127, result.ExitCode);
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
