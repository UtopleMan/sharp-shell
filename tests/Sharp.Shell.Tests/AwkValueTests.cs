using Sharp.Shell.Commands.Awk;
using Xunit;

namespace Sharp.Shell.Tests;

// Where every awk clone goes wrong: the same characters compare one way when the program wrote them
// and another way when the input did. Every expectation below was checked against `/usr/bin/awk` and
// `gawk --posix`, which agree on all of them.
public class AwkValueTests
{
    private const string DefaultConvertFormat = "%.6g";

    public static TheoryData<string, string, string> ComparisonMatrix => new()
    {
        { "number:10", "string:9", "<" },
        { "number:10", "input:9", ">" },
        { "string:10", "input:9", "<" },
        { "string:10", "string:9", "<" },
        { "input:10", "input:9", ">" },
        { "input:10", "number:10", "=" },
        { "uninitialized:", "number:0", "=" },
        { "uninitialized:", "string:", "=" },
        { "uninitialized:", "string:0", "<" },
        { "uninitialized:", "input:10", "<" },
        { "number:10", "string:abc", "<" },
        { "input:abc", "number:0", ">" },
        { "input:", "number:0", "<" },
        { "input:  10  ", "number:10", "=" },
        { "input:+5", "number:5", "=" },
        { "input:.5", "number:0.5", "=" },
        { "input:1e3", "number:1000", "=" },
        { "input:0x1A", "number:26", "=" },
        { "input:0x1A", "string:0x1A", "=" },
        { "string:0", "number:0", "=" },
        { "input:9", "input:10", "<" },
        { "number:1", "number:1", "=" },
    };

    [Theory]
    [MemberData(nameof(ComparisonMatrix))]
    public void ComparisonFollowsThePosixTable(string left, string right, string expected)
    {
        int order = AwkComparison.Compare(Value(left), Value(right), DefaultConvertFormat);

        Assert.Equal(expected, order < 0 ? "<" : order == 0 ? "=" : ">");
    }

    // The headline case: the same two strings order one way as literals and the other way as fields.
    [Fact]
    public void TenBeatsNineAsInputAndLosesToItAsAStringLiteral()
    {
        Assert.True(AwkComparison.Compare(AwkValue.Of("10"), AwkValue.Of("9"), DefaultConvertFormat) < 0);
        Assert.True(AwkComparison.Compare(AwkValue.FromInput("10"), AwkValue.FromInput("9"), DefaultConvertFormat) > 0);
    }

    [Theory]
    [InlineData("10", "StrNum")]
    [InlineData("  10  ", "StrNum")]
    [InlineData("+5", "StrNum")]
    [InlineData("-5", "StrNum")]
    [InlineData(".5", "StrNum")]
    [InlineData("1e3", "StrNum")]
    [InlineData("1E-3", "StrNum")]
    [InlineData("0x1A", "StrNum")]
    [InlineData("", "String")]
    [InlineData("   ", "String")]
    [InlineData("abc", "String")]
    [InlineData("12abc", "String")]
    [InlineData("1e", "String")]
    [InlineData("1.2.3", "String")]
    [InlineData("inf", "String")]
    [InlineData("nan", "String")]
    public void NumericStringRecognitionNeedsTheWholeString(string text, string expected)
    {
        Assert.Equal(expected, AwkValue.FromInput(text).Kind.ToString());
    }

    [Theory]
    [InlineData("12abc", 12)]
    [InlineData(" 3.5e2xyz", 350)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    [InlineData("0x1A", 26)]
    [InlineData("-0x10", -16)]
    [InlineData("inf", 0)]
    [InlineData("nan", 0)]
    [InlineData("1e3", 1000)]
    public void ConversionTakesAsMuchOfTheFrontAsLooksNumeric(string text, double expected)
    {
        Assert.Equal(expected, AwkValue.Of(text).ToNumber());
    }

    [Theory]
    [InlineData("number:0", false)]
    [InlineData("number:1", true)]
    [InlineData("string:", false)]
    [InlineData("string:0", true)]
    [InlineData("string:abc", true)]
    [InlineData("input:0", false)]
    [InlineData("input:0.0", false)]
    [InlineData("input:abc", true)]
    [InlineData("input:", false)]
    [InlineData("uninitialized:", false)]
    public void TruthDependsOnWhereTheValueCameFrom(string value, bool expected)
    {
        Assert.Equal(expected, Value(value).IsTrue());
    }

    [Theory]
    [InlineData(1d / 3, "%.6g", "0.333333")]
    [InlineData(1d / 3, "%.2g", "0.33")]
    [InlineData(0.0000001, "%.6g", "1e-07")]
    [InlineData(9007199254740992d, "%.6g", "9007199254740992")]
    [InlineData(4611686018427387904d, "%.6g", "4611686018427387904")]
    [InlineData(1e18, "%.6g", "1000000000000000000")]
    [InlineData(100000d, "%.6g", "100000")]
    [InlineData(0d, "%.6g", "0")]
    [InlineData(2d, "%.2g", "2")]
    [InlineData(1.5, "%.6g", "1.5")]
    [InlineData(-3.25, "%.6g", "-3.25")]
    public void AnIntegralValuePrintsAsAnIntegerWhateverTheFormatSays(double value, string format, string expected)
    {
        Assert.Equal(expected, AwkValue.Of(value).ToStringWith(format));
    }

    // .NET renders negative zero as "-0"; awk prints it as 0, and `print -0` in both oracles agrees.
    [Fact]
    public void NegativeZeroPrintsAsZero()
    {
        Assert.Equal("0", AwkValue.Of(-0d).ToStringWith(DefaultConvertFormat));
    }

    // 1e30 is integral and far outside a 64-bit integer, and the two oracles spell it differently —
    // gawk gives the exact expansion, one-true-awk gives %.30g. The exact expansion is ours.
    [Fact]
    public void AVeryLargeIntegralValueKeepsItsExactExpansion()
    {
        Assert.Equal("1000000000000000019884624838656", AwkValue.Of(1e30).ToStringWith(DefaultConvertFormat));
    }

    [Fact]
    public void AStringKeepsItsTextWhateverTheFormatSays()
    {
        Assert.Equal("10", AwkValue.Of("10").ToStringWith("%.2g"));
        Assert.Equal("0.333333333", AwkValue.FromInput("0.333333333").ToStringWith("%.2g"));
    }

    [Fact]
    public void AnUninitializedValueIsBothZeroAndEmpty()
    {
        Assert.Equal(0, AwkValue.Uninitialized.ToNumber());
        Assert.Equal(string.Empty, AwkValue.Uninitialized.ToStringWith(DefaultConvertFormat));
        Assert.True(AwkValue.Uninitialized.ComparesNumerically);
    }

    private static AwkValue Value(string specification)
    {
        int separator = specification.IndexOf(':', StringComparison.Ordinal);
        string kind = specification[..separator];
        string text = specification[(separator + 1)..];

        return kind switch
        {
            "number" => AwkValue.Of(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)),
            "string" => AwkValue.Of(text),
            "input" => AwkValue.FromInput(text),
            _ => AwkValue.Uninitialized,
        };
    }
}
