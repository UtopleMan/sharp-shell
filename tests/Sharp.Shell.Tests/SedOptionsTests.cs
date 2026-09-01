using Sharp.Shell.Commands.Sed;
using Xunit;

namespace Sharp.Shell.Tests;

// The long-option table is the half of sed's command line agents almost never write, so nothing else
// in the suite reaches it. Every arm is pinned here against GNU sed's spelling and its effect.
public class SedOptionsTests
{
    [Theory]
    [InlineData("--quiet")]
    [InlineData("--silent")]
    public void QuietSuppressesAutoPrint(string flag)
    {
        Assert.True(SedOptions.Parse([flag, "p"]).SuppressesAutoPrint);
    }

    [Fact]
    public void ExpressionWithEqualsCarriesTheScript()
    {
        SedOptions options = SedOptions.Parse(["--expression=s/a/b/"]);

        Assert.Equal("s/a/b/", options.InlineScript);
    }

    [Fact]
    public void ExpressionTakesTheNextArgumentAsValue()
    {
        SedOptions options = SedOptions.Parse(["--expression", "s/a/b/"]);

        Assert.Equal("s/a/b/", options.InlineScript);
    }

    [Fact]
    public void SeveralExpressionsJoinAsOneScript()
    {
        SedOptions options = SedOptions.Parse(["--expression=s/a/b/", "--expression=s/b/c/"]);

        Assert.Equal("s/a/b/\ns/b/c/", options.InlineScript);
    }

    [Fact]
    public void FileRecordsAScriptFileSource()
    {
        SedOptions options = SedOptions.Parse(["--file=x.sed"]);

        Assert.True(options.HasScriptFile);
        Assert.Null(options.InlineScript);
    }

    [Fact]
    public void FileTakesTheNextArgumentAsValue()
    {
        SedOptions options = SedOptions.Parse(["--file", "x.sed", "in.txt"]);

        Assert.True(options.HasScriptFile);
        Assert.Equal(["in.txt"], options.InputFiles);
    }

    [Fact]
    public void ExtendedRegexIsRecognised()
    {
        Assert.True(SedOptions.Parse(["--regexp-extended", "s/a/b/"]).IsExtendedRegex);
    }

    [Fact]
    public void SeparateIsRecognised()
    {
        Assert.True(SedOptions.Parse(["--separate", "-n", "$p"]).IsSeparate);
    }

    [Fact]
    public void PosixIsRecognised()
    {
        Assert.True(SedOptions.Parse(["--posix", "s/a/b/"]).IsPosix);
    }

    [Fact]
    public void UnbufferedIsAcceptedWithNoEffect()
    {
        SedOptions options = SedOptions.Parse(["--unbuffered", "s/a/b/"]);

        Assert.Null(options.Error);
        Assert.Null(options.UnsupportedFlag);
        Assert.Equal("s/a/b/", options.InlineScript);
    }

    [Theory]
    [InlineData("--null-data")]
    [InlineData("--zero-terminated")]
    public void NullSeparationIsRecognised(string flag)
    {
        Assert.True(SedOptions.Parse([flag, "p"]).IsNullSeparated);
    }

    [Fact]
    public void InPlaceWithoutSuffixIsBare()
    {
        SedOptions options = SedOptions.Parse(["--in-place", "s/a/b/"]);

        Assert.True(options.IsInPlace);
        Assert.Equal(string.Empty, options.InPlaceSuffix);
    }

    [Fact]
    public void InPlaceWithSuffixKeepsIt()
    {
        Assert.Equal(".bak", SedOptions.Parse(["--in-place=.bak", "s/a/b/"]).InPlaceSuffix);
    }

    [Fact]
    public void LineLengthIsRead()
    {
        Assert.Equal(5, SedOptions.Parse(["--line-length=5", "l"]).LineWidth);
    }

    [Fact]
    public void LineLengthTakesTheNextArgumentAsValue()
    {
        Assert.Equal(5, SedOptions.Parse(["--line-length", "5", "l"]).LineWidth);
    }

    [Fact]
    public void AnUnreadableLineLengthIsAnError()
    {
        SedOptions options = SedOptions.Parse(["--line-length=wide", "l"]);

        Assert.Equal("invalid line length: wide", options.Error);
    }

    [Theory]
    [InlineData("--expression")]
    [InlineData("--file")]
    [InlineData("--line-length")]
    public void ALongOptionMissingItsValueIsAnErrorNotAThrow(string flag)
    {
        SedOptions options = SedOptions.Parse([flag]);

        Assert.Equal($"option '{flag}' requires an argument", options.Error);
    }

    [Fact]
    public void UnknownLongOptionIsUnsupportedNotFatal()
    {
        SedOptions options = SedOptions.Parse(["--follow-symlinks", "p"]);

        Assert.Null(options.Error);
        Assert.Contains("--follow-symlinks", options.UnsupportedFlag);
    }

    [Fact]
    public void AnUnsupportedLongOptionStopsTheRead()
    {
        SedOptions options = SedOptions.Parse(["--sandbox", "--quiet", "p"]);

        Assert.False(options.SuppressesAutoPrint);
    }

    [Fact]
    public void DoubleDashEndsOptions()
    {
        SedOptions options = SedOptions.Parse(["--", "-n"]);

        Assert.Equal("-n", options.InlineScript);
        Assert.False(options.SuppressesAutoPrint);
    }
}
