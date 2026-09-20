using Xunit;

namespace Sharp.Shell.Tests.BlackBox;

// The rc files through the real binary, with HOME pointed at a temp directory so nothing here depends
// on the machine running the suite.
public sealed class SharpRcFileTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sharp-rc-binary-root").FullName;

    private readonly string home = Directory.CreateTempSubdirectory("sharp-rc-binary-home").FullName;

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(home, recursive: true);
    }

    private void WriteRcFile(string name, string content) =>
        File.WriteAllText(Path.Combine(home, name), content);

    private SharpResult RunCommand(string command, params string[] options)
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");

        return SharpBinary.Run(
            [.. options, "--root", root, "-c", command],
            root,
            standardInput: null,
            new Dictionary<string, string> { ["HOME"] = home });
    }

    private SharpResult RunPiped(string script, params string[] options)
    {
        Assert.SkipUnless(SharpBinary.IsAvailable, "sharp is not built");

        return SharpBinary.Run(
            [.. options, "--root", root],
            root,
            script,
            new Dictionary<string, string> { ["HOME"] = home });
    }

    [Fact]
    public void The_environment_file_reaches_a_single_command()
    {
        WriteRcFile(".shshenv", "MARKER=from-env\n");

        Assert.Equal("from-env\n", RunCommand("echo $MARKER").Stdout);
    }

    [Fact]
    public void The_interactive_file_does_not_reach_a_single_command()
    {
        WriteRcFile(".shshrc", "alias greet='echo hello'\n");

        Assert.Equal("[]\n", RunCommand("echo \"[$(alias)]\"").Stdout);
    }

    [Fact]
    public void The_environment_file_reaches_a_piped_script()
    {
        WriteRcFile(".shshenv", "MARKER=from-env\n");

        Assert.Equal("from-env\n", RunPiped("echo $MARKER\n").Stdout);
    }

    [Fact]
    public void Norc_skips_both_files()
    {
        WriteRcFile(".shshenv", "MARKER=from-env\n");
        WriteRcFile(".shshrc", "MARKER=from-rc\n");

        Assert.Equal("[]\n", RunCommand("echo \"[$MARKER]\"", "--norc").Stdout);
    }

    [Fact]
    public void A_broken_rc_file_is_named_and_the_shell_still_runs()
    {
        WriteRcFile(".shshenv", "echo unterminated '\n");

        SharpResult result = RunCommand("echo still-here");

        Assert.Equal("still-here\n", result.Stdout);
        Assert.Contains(".shshenv", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void A_missing_rc_file_says_nothing()
    {
        SharpResult result = RunCommand("echo quiet");

        Assert.Equal("quiet\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }
}
