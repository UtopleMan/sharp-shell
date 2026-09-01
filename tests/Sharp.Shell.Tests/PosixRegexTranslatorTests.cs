using System.Text.RegularExpressions;
using Sharp.Shell.Text;
using Xunit;

namespace Sharp.Shell.Tests;

// The dialect gap this class exists to close: `a\+` repeats in POSIX BRE and is a literal plus in
// .NET, `foo(bar)` groups in .NET and is literal parens in BRE. Every test asserts on match
// behaviour rather than on the emitted pattern, because the emitted pattern is an implementation
// detail and the behaviour is the contract.
public class PosixRegexTranslatorTests
{
    private const RegexDialect Basic = RegexDialect.BasicPosix;

    private const RegexDialect Extended = RegexDialect.ExtendedPosix;

    private static Regex Compile(
        string pattern,
        RegexDialect dialect = Basic,
        bool allowsGnuExtensions = true,
        bool isMultiline = false)
    {
        RegexTranslation translation = PosixRegexTranslator.Translate(pattern, dialect, allowsGnuExtensions, isMultiline);
        Assert.Null(translation.RefusalReason);
        Assert.NotNull(translation.Pattern);

        return new Regex(translation.Pattern!, isMultiline ? RegexOptions.Multiline : RegexOptions.None);
    }

    private static bool Matches(
        string pattern,
        string input,
        RegexDialect dialect = Basic,
        bool allowsGnuExtensions = true,
        bool isMultiline = false) =>
        Compile(pattern, dialect, allowsGnuExtensions, isMultiline).IsMatch(input);

    private static string MatchOf(string pattern, string input, RegexDialect dialect = Basic) =>
        Compile(pattern, dialect).Match(input).Value;

    private static string Refusal(string pattern, RegexDialect dialect = Basic, bool allowsGnuExtensions = true)
    {
        RegexTranslation translation = PosixRegexTranslator.Translate(pattern, dialect, allowsGnuExtensions);
        Assert.Null(translation.Pattern);
        Assert.NotNull(translation.RefusalReason);

        return translation.RefusalReason!;
    }

    [Fact]
    public void BasicBackslashPlusRepeats()
    {
        Assert.Equal("aaa", MatchOf(@"a\+", "aaa"));
    }

    [Fact]
    public void BasicBarePlusIsALiteral()
    {
        Assert.Equal("a+", MatchOf("a+", "xa+y"));
    }

    [Fact]
    public void BasicBackslashQuestionIsOptional()
    {
        Assert.Equal("ab", MatchOf(@"ab\?", "ab"));
        Assert.Equal("a", MatchOf(@"ab\?", "ac"));
    }

    [Fact]
    public void BasicParenthesesAreLiterals()
    {
        Assert.True(Matches("foo(bar)", "foo(bar)"));
        Assert.False(Matches("foo(bar)", "foobar"));
    }

    [Fact]
    public void ExtendedParenthesesGroup()
    {
        Assert.True(Matches("foo(bar)", "foobar", Extended));
    }

    [Fact]
    public void BasicEscapedParenthesesGroup()
    {
        Assert.Equal("ab", MatchOf(@"\(ab\)", "ab"));
    }

    [Fact]
    public void ExtendedEscapedParenthesesAreLiterals()
    {
        Assert.True(Matches(@"\(a\)", "(a)", Extended));
    }

    [Fact]
    public void BasicIntervalNeedsBackslashBraces()
    {
        Assert.Equal("aaa", MatchOf(@"a\{2,3\}", "aaaa"));
        Assert.True(Matches("a{2}", "a{2}"));
    }

    [Fact]
    public void ExtendedIntervalUsesBareBraces()
    {
        Assert.Equal("123", MatchOf("[[:digit:]]{2,}", "123", Extended));
    }

    [Fact]
    public void BasicStarIsALiteralAtTheStart()
    {
        Assert.True(Matches("*a", "*a"));
    }

    [Fact]
    public void BasicCaretIsALiteralInTheMiddle()
    {
        Assert.True(Matches("a^b", "a^b"));
    }

    [Fact]
    public void BasicDollarIsALiteralInTheMiddle()
    {
        Assert.True(Matches("a$b", "a$b"));
    }

    [Fact]
    public void AnchorsBindToTheWholePatternSpace()
    {
        Assert.True(Matches("^a$", "a"));

        // .NET's bare $ also matches before a trailing newline. sed's does not.
        Assert.False(Matches("^a$", "a\n"));
    }

    [Fact]
    public void MultilineAnchorsBindToEachLine()
    {
        Assert.True(Matches("^b$", "a\nb\nc", isMultiline: true));
    }

    [Fact]
    public void DotMatchesNewlineUnlessMultiline()
    {
        Assert.True(Matches("a.b", "a\nb"));
        Assert.False(Matches("a.b", "a\nb", isMultiline: true));
    }

    [Fact]
    public void NegatedBracketMatchesNewlineUnlessMultiline()
    {
        Assert.True(Matches("[^x]", "\n"));
        Assert.False(Matches("[^x]", "\n", isMultiline: true));
    }

    [Fact]
    public void BracketTreatsALeadingCloseBracketAsALiteral()
    {
        Assert.True(Matches("[]a-]", "]"));
        Assert.True(Matches("[]a-]", "a"));
        Assert.True(Matches("[]a-]", "-"));
        Assert.False(Matches("[]a-]", "b"));
    }

    [Fact]
    public void NegatedBracketTreatsALeadingCloseBracketAsALiteral()
    {
        Assert.True(Matches("[^]]", "a"));
        Assert.False(Matches("[^]]", "]"));
    }

    [Theory]
    [InlineData("[[:alpha:]]", "q", "1")]
    [InlineData("[[:digit:]]", "7", "q")]
    [InlineData("[[:upper:]]", "Q", "q")]
    [InlineData("[[:space:]]", "\t", "q")]
    [InlineData("[[:punct:]]", ";", "q")]
    [InlineData("[[:xdigit:]]", "f", "g")]
    public void PosixCharacterClassesTranslate(string pattern, string matching, string notMatching)
    {
        Assert.True(Matches(pattern, matching));
        Assert.False(Matches(pattern, notMatching));
    }

    [Fact]
    public void BackreferencesWork()
    {
        Assert.True(Matches(@"\(a\)\1", "aa"));
        Assert.False(Matches(@"\(a\)\1", "ab"));
    }

    [Fact]
    public void GnuWordOperatorsTranslate()
    {
        Assert.True(Matches(@"\<word\>", "a word here"));
        Assert.False(Matches(@"\<word\>", "wordy"));
        Assert.True(Matches(@"\bword\b", "a word"));
        Assert.True(Matches(@"\w\+", "abc"));
        Assert.False(Matches(@"^\w\+$", "a b"));
        Assert.True(Matches(@"a\sb", "a b"));
    }

    [Fact]
    public void GnuNumericEscapesTranslate()
    {
        Assert.True(Matches(@"\x41", "A"));
        Assert.True(Matches(@"\o101", "A"));
        Assert.True(Matches(@"\d065", "A"));
        Assert.True(Matches(@"a\tb", "a\tb"));
    }

    [Theory]
    [InlineData(@"a\Wb", "a-b", "axb")]
    [InlineData(@"a\Sb", "axb", "a b")]
    [InlineData(@"\Bcat", "concat", "cat dog")]
    public void GnuNegatedClassesAndBoundariesTranslate(string pattern, string matching, string notMatching)
    {
        Assert.True(Matches(pattern, matching));
        Assert.False(Matches(pattern, notMatching));
    }

    [Fact]
    public void GnuBufferAnchorsBindToTheWholeSubject()
    {
        Assert.True(Matches(@"\`a", "abc"));
        Assert.False(Matches(@"\`b", "abc"));
        Assert.True(Matches(@"c\'", "abc"));
        Assert.False(Matches(@"b\'", "abc"));
    }

    [Theory]
    [InlineData(@"a\ab", "a\ab")]
    [InlineData(@"a\fb", "a\fb")]
    [InlineData(@"a\vb", "a\vb")]
    [InlineData(@"a\rb", "a\rb")]
    public void GnuControlLiteralsTranslate(string pattern, string subject)
    {
        Assert.True(Matches(pattern, subject));
    }

    [Theory]
    [InlineData(@"\cA", "\u0001")]
    [InlineData(@"\cI", "\t")]
    [InlineData(@"\ci", "\t")]
    public void ControlEscapesTranslate(string pattern, string subject)
    {
        Assert.True(Matches(pattern, subject));
    }

    [Fact]
    public void ShortNumericEscapesStopAtTheFirstNonDigit()
    {
        Assert.True(Matches(@"\d65z", "Az"));
        Assert.True(Matches(@"\x41z", "Az"));
        Assert.True(Matches(@"\o101z", "Az"));
    }

    [Theory]
    [InlineData(@"\c", "control character")]
    [InlineData(@"\xz", "digits")]
    [InlineData(@"\o", "digits")]
    [InlineData(@"\d", "digits")]
    public void MalformedNumericEscapesAreRefused(string pattern, string expected)
    {
        Assert.Contains(expected, Refusal(pattern), StringComparison.Ordinal);
    }

    [Fact]
    public void EscapedMetacharactersAreLiterals()
    {
        Assert.True(Matches(@"a\.b", "a.b"));
        Assert.False(Matches(@"a\.b", "axb"));
        Assert.True(Matches(@"a\\b", @"a\b"));
    }

    // Every character the .NET emitter has to escape or spell differently, reached as a literal so
    // the emitted pattern stays a pattern rather than becoming a syntax error.
    [Theory]
    [InlineData("a#b", "a#b")]
    [InlineData("a b", "a b")]
    [InlineData("a}b", "a}b")]
    [InlineData("a]b", "a]b")]
    [InlineData(@"a\cJb", "a\nb")]
    [InlineData(@"a\cMb", "a\rb")]
    [InlineData(@"a\o014b", "a\fb")]
    [InlineData(@"a\o013b", "a\vb")]
    [InlineData(@"a\o001b", "a\u0001b")]
    [InlineData(@"a\o177b", "a\u007fb")]
    public void LiteralsThatNeedEmitterEscapingStillMatch(string pattern, string subject)
    {
        Assert.True(Matches(pattern, subject));
    }

    // POSIX makes a backslash ordinary inside brackets; GNU reads its escapes there instead.
    [Theory]
    [InlineData(@"[\]]", "]", "a")]
    [InlineData(@"[\\]", @"\", "a")]
    [InlineData(@"[\t]", "\t", "t")]
    [InlineData(@"[\n]", "\n", "n")]
    [InlineData(@"[\r]", "\r", "r")]
    [InlineData(@"[\f]", "\f", "f")]
    [InlineData(@"[\v]", "\v", "v")]
    [InlineData(@"[\a]", "\a", "a")]
    public void BracketReadsGnuEscapes(string pattern, string matching, string notMatching)
    {
        Assert.True(Matches(pattern, matching));
        Assert.False(Matches(pattern, notMatching));
    }

    [Fact]
    public void BracketKeepsTheBackslashForAnEscapeGnuDoesNotDefine()
    {
        Assert.True(Matches(@"[\q]", @"\"));
        Assert.True(Matches(@"[\q]", "q"));
        Assert.False(Matches(@"[\q]", "a"));
    }

    [Theory]
    [InlineData("[a-]", "-", "b")]
    [InlineData("[-a]", "-", "b")]
    [InlineData("[a-c]", "b", "d")]
    [InlineData("[a-c-]", "-", "d")]
    [InlineData(@"[\t-\r]", "\n", "a")]
    public void BracketRangesAndTrailingHyphens(string pattern, string matching, string notMatching)
    {
        Assert.True(Matches(pattern, matching));
        Assert.False(Matches(pattern, notMatching));
    }

    [Fact]
    public void AlternationOfEqualLengthBranchesIsAccepted()
    {
        Assert.True(Matches(@"\(ab\|cd\)", "cd"));
        Assert.True(Matches("ab|cd", "ab", Extended));
    }

    [Fact]
    public void AlternationOfLiteralsWithoutAPrefixRelationIsAccepted()
    {
        Assert.True(Matches("error|warning", "warning", Extended));
    }

    [Fact]
    public void AlternationWhereOneBranchPrefixesAnotherIsRefused()
    {
        Assert.Contains("alternation", Refusal(@"a\|ab"), StringComparison.Ordinal);
        Assert.Contains("alternation", Refusal("foo|foobar", Extended), StringComparison.Ordinal);
    }

    [Fact]
    public void AlternationWithAVariableLengthBranchIsRefused()
    {
        Assert.Contains("alternation", Refusal(@" \+\|\t"), StringComparison.Ordinal);
    }

    [Fact]
    public void AlternationNestedInAGroupIsChecked()
    {
        Assert.Contains("alternation", Refusal(@"x\(a\|ab\)y"), StringComparison.Ordinal);
    }

    [Fact]
    public void CollatingSymbolsAndEquivalenceClassesAreRefused()
    {
        Assert.Contains("collating", Refusal("[[.a.]]"), StringComparison.Ordinal);
        Assert.Contains("equivalence", Refusal("[[=a=]]"), StringComparison.Ordinal);
    }

    [Fact]
    public void UndefinedEscapesAreRefused()
    {
        Assert.Contains("escape", Refusal(@"\e"), StringComparison.Ordinal);
        Assert.Contains("escape", Refusal(@"\p{L}"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[abc")]
    [InlineData(@"\(ab")]
    [InlineData(@"a\{2")]
    [InlineData(@"a\")]
    public void UnterminatedConstructsAreRefused(string pattern)
    {
        Assert.NotEmpty(Refusal(pattern));
    }

    [Fact]
    public void PosixModeRefusesGnuExtensions()
    {
        Assert.Contains("GNU", Refusal(@"a\+", allowsGnuExtensions: false), StringComparison.Ordinal);
        Assert.Contains("GNU", Refusal(@"a\|b", allowsGnuExtensions: false), StringComparison.Ordinal);
        Assert.Contains("GNU", Refusal(@"\w", allowsGnuExtensions: false), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPatternTranslatesToAnEmptyPattern()
    {
        Assert.True(Matches(string.Empty, "anything"));
    }

    [Fact]
    public void RealWorldCorpusPatternsTranslate()
    {
        Assert.Equal("     ", MatchOf(@" \+", "     "));
        Assert.Equal("0xdeadbeef", MatchOf(@"0x[a-f0-9]\+", "0xdeadbeef"));
        Assert.Equal("12", MatchOf("[[:digit:]]{2,}", "12", Extended));
        Assert.True(Matches(@"\^D", "^D"));
    }

    // grep and sed's addresses only ask whether a line matches, and no leftmost-longest difference
    // can change a yes/no answer — so they translate patterns the substitute command refuses.
    [Theory]
    [InlineData(@"a\|ab")]
    [InlineData(@" \+\|\t")]
    [InlineData(@"StatusLineModel\|Baton\b")]
    public void AmbiguousAlternationTranslatesWhenTheMatchExtentIsUnused(string pattern)
    {
        RegexTranslation strict = PosixRegexTranslator.Translate(pattern, Basic);
        RegexTranslation lenient = PosixRegexTranslator.Translate(pattern, Basic, usesMatchExtent: false);

        Assert.NotNull(strict.RefusalReason);
        Assert.Null(lenient.RefusalReason);
    }
}
