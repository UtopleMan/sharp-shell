using Sharp.Shell.Execution;
using Xunit;

namespace Sharp.Shell.Tests;

public class TextStreamTests
{
    [Theory]
    [InlineData("abc\ndef", new[] { "abc", "def" })]
    [InlineData("abc\n", new[] { "abc" })]
    [InlineData("abc", new[] { "abc" })]
    [InlineData("", new string[0])]
    [InlineData("\n", new[] { "" })]
    [InlineData("a\n\nb", new[] { "a", "", "b" })]
    public void SplitsLinesWithoutInventingATrailingOne(string text, string[] expected)
    {
        Assert.Equal(expected, TextStream.Lines(TextStream.FromText(text)));
    }

    [Fact]
    public void SplitsLinesAcrossChunkBoundaries()
    {
        Assert.Equal(["ab", "cd"], TextStream.Lines(["a", "b\nc", "d"]));
    }

    [Fact]
    public void FromLinesReattachesTheNewlines()
    {
        Assert.Equal("a\nb\n", TextStream.Collect(TextStream.FromLines(["a", "b"])));
    }

    [Fact]
    public void LinesIsLazy()
    {
        int produced = 0;

        IEnumerable<string> Source()
        {
            while (true)
            {
                produced++;
                yield return "line\n";
            }
        }

        Assert.Equal(["line", "line"], TextStream.Lines(Source()).Take(2));
        Assert.True(produced <= 3, $"the producer ran {produced} times for two consumed lines");
    }
}
