using Sharp;
using Xunit;

namespace Sharp.Shell.Tests;

// The binary's command line. The black-box tests prove the whole process honours these; this class
// pins the parse itself, including the arms no black-box case reaches because they end in usage.
public class SharpOptionsTests
{
    [Fact]
    public void NoArgumentsMeansAnUnconfinedInteractiveSession()
    {
        Options options = Options.Parse([]);

        Assert.Null(options.Error);
        Assert.Equal("/", options.Root);
        Assert.Equal(Directory.GetCurrentDirectory(), options.StartDirectory);
        Assert.Null(options.Command);
        Assert.Null(options.ScriptPath);
        Assert.False(options.Explains);
        Assert.False(options.Strict);
    }

    [Fact]
    public void DashCTakesTheNextArgumentAsTheCommand()
    {
        Assert.Equal("echo hi", Options.Parse(["-c", "echo hi"]).Command);
    }

    [Fact]
    public void DashCWithoutACommandFallsThroughToTheScriptArm()
    {
        Options options = Options.Parse(["-c"]);

        Assert.NotNull(options.Error);
        Assert.Contains("unknown option '-c'", options.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RootIsMadeAbsoluteAndBecomesTheStartDirectory()
    {
        string temporary = Directory.CreateTempSubdirectory("sharp-options").FullName;

        try
        {
            Options options = Options.Parse(["--root", temporary]);

            Assert.Equal(Path.GetFullPath(temporary), options.Root);
            Assert.Equal(options.Root, options.StartDirectory);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Theory]
    [InlineData("--explain")]
    [InlineData("--strict")]
    public void TheSwitchesAreRead(string flag)
    {
        Options options = Options.Parse([flag, "-c", "echo hi"]);

        Assert.Null(options.Error);
        Assert.True(flag == "--explain" ? options.Explains : options.Strict);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void HelpIsReportedAsTheUsageError(string flag)
    {
        Assert.Equal(Options.Usage, Options.Parse([flag]).Error);
    }

    [Fact]
    public void AnUnknownOptionNamesItselfAndTheUsage()
    {
        string? error = Options.Parse(["--nonsense"]).Error;

        Assert.Contains("unknown option '--nonsense'", error, StringComparison.Ordinal);
        Assert.Contains(Options.Usage, error, StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptThatIsNotThereIsReported()
    {
        Assert.Contains("no such file", Options.Parse(["absent.sh"]).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AScriptThatExistsIsAccepted()
    {
        string directory = Directory.CreateTempSubdirectory("sharp-options").FullName;
        string script = Path.Combine(directory, "run.sh");
        File.WriteAllText(script, "echo hi\n");

        try
        {
            Options options = Options.Parse([script]);

            Assert.Null(options.Error);
            Assert.Equal(script, options.ScriptPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SettingsCarryWhatTheSessionNeeds()
    {
        SessionSettings settings = Options.Parse(["--explain", "--strict", "-c", "echo hi"]).Settings;

        Assert.True(settings.Explains);
        Assert.True(settings.Strict);
        Assert.Equal("/", settings.Root);
    }
}
