using System.Text.RegularExpressions;
using Sharp.Shell.Commands.Sed;
using Xunit;

namespace Sharp.Shell.Tests;

// The parser answers the question classification asks before anything runs: do we own this script?
// Every test here asserts which of the three answers came back — parsed, invalid sed, or valid sed
// we do not own — because the three lead to completely different behaviour.
public class SedScriptParserTests
{
    private static SedProgram Parse(string script, bool isExtended = false, bool allowsGnu = true)
    {
        SedParseResult result = SedScriptParser.Parse(script, new SedParseOptions(isExtended, allowsGnu));
        Assert.Null(result.Error);
        Assert.Null(result.UnsupportedConstruct);
        Assert.NotNull(result.Program);

        return result.Program!;
    }

    private static string Error(string script)
    {
        SedParseResult result = SedScriptParser.Parse(script);
        Assert.Null(result.UnsupportedConstruct);
        Assert.NotNull(result.Error);

        return result.Error!;
    }

    private static bool PatternMatches(Regex? pattern, string input) => pattern!.IsMatch(input);

    private static string Unsupported(string script)
    {
        SedParseResult result = SedScriptParser.Parse(script);
        Assert.Null(result.Error);
        Assert.NotNull(result.UnsupportedConstruct);

        return result.UnsupportedConstruct!;
    }

    [Fact]
    public void LineRangePrintIsTheShapeEveryMinedCommandUses()
    {
        SedProgram program = Parse("1,120p");
        SedPrint print = Assert.IsType<SedPrint>(Assert.Single(program.Commands));

        Assert.Equal(SedAddressKind.Line, print.Range!.Start.Kind);
        Assert.Equal(1, print.Range.Start.Line);
        Assert.Equal(SedRangeEndKind.Address, print.Range.EndKind);
        Assert.Equal(120, print.Range.End!.Line);
    }

    [Fact]
    public void SeveralCommandsSeparatedBySemicolons()
    {
        Assert.Equal(2, Parse("30,45p;115,125p").Commands.Count);
    }

    [Fact]
    public void LastLineAddress()
    {
        SedPrint print = Assert.IsType<SedPrint>(Assert.Single(Parse("$p").Commands));
        Assert.Equal(SedAddressKind.Last, print.Range!.Start.Kind);
    }

    [Fact]
    public void RegexAddressWithTheDefaultDelimiter()
    {
        SedDelete delete = Assert.IsType<SedDelete>(Assert.Single(Parse("/foo/d").Commands));
        Assert.Equal(SedAddressKind.Regex, delete.Range!.Start.Kind);
        Assert.NotNull(delete.Range.Start.Pattern);
    }

    [Fact]
    public void RegexAddressWithACustomDelimiter()
    {
        SedDelete delete = Assert.IsType<SedDelete>(Assert.Single(Parse(@"\%a/b%d").Commands));
        Assert.True(PatternMatches(delete.Range!.Start.Pattern, "a/b"));
    }

    [Fact]
    public void EmptyRegexAddressMeansTheLastRegex()
    {
        SedDelete delete = Assert.IsType<SedDelete>(Assert.Single(Parse("//d").Commands));
        Assert.Equal(SedAddressKind.Regex, delete.Range!.Start.Kind);
        Assert.Null(delete.Range.Start.Pattern);
    }

    [Fact]
    public void CaseInsensitiveAddressFlag()
    {
        SedDelete delete = Assert.IsType<SedDelete>(Assert.Single(Parse("/foo/Id").Commands));
        Assert.True(PatternMatches(delete.Range!.Start.Pattern, "FOO"));
    }

    [Fact]
    public void RelativeAndSteppedRangeEnds()
    {
        Assert.Equal(SedRangeEndKind.RelativeLines, Assert.IsType<SedPrint>(Parse("2,+3p").Commands[0]).Range!.EndKind);
        Assert.Equal(SedRangeEndKind.NextMultiple, Assert.IsType<SedPrint>(Parse("2,~4p").Commands[0]).Range!.EndKind);
        Assert.Equal(SedAddressKind.Step, Assert.IsType<SedPrint>(Parse("0~3p").Commands[0]).Range!.Start.Kind);
        Assert.Equal(SedAddressKind.Zero, Assert.IsType<SedPrint>(Parse("0,/foo/p").Commands[0]).Range!.Start.Kind);
    }

    [Fact]
    public void NegatedAddress()
    {
        Assert.True(Assert.IsType<SedDelete>(Assert.Single(Parse("1!d").Commands)).Range!.IsNegated);
    }

    [Fact]
    public void NestedBlocksKnowWhereTheyEnd()
    {
        SedProgram program = Parse("/a/{/b/{p;};d;}");
        SedBlockStart outer = Assert.IsType<SedBlockStart>(program.Commands[0]);
        SedBlockStart inner = Assert.IsType<SedBlockStart>(program.Commands[1]);

        Assert.Equal(program.Commands.Count, outer.EndIndex);
        Assert.Equal(3, inner.EndIndex);
    }

    [Fact]
    public void SubstitutionWithFlagsAndOccurrence()
    {
        SedSubstitute substitute = Assert.IsType<SedSubstitute>(Assert.Single(Parse("s/a/b/3g").Commands));
        Assert.True(substitute.IsGlobal);
        Assert.Equal(3, substitute.Occurrence);
    }

    [Fact]
    public void SubstitutionWithAnAlternativeDelimiter()
    {
        SedSubstitute substitute = Assert.IsType<SedSubstitute>(Assert.Single(Parse("s|/usr|/opt|").Commands));
        Assert.True(PatternMatches(substitute.Pattern, "/usr/bin"));
    }

    [Fact]
    public void SubstitutionWithAnEscapedDelimiter()
    {
        SedSubstitute substitute = Assert.IsType<SedSubstitute>(Assert.Single(Parse(@"s/a\/b/x/").Commands));
        Assert.True(PatternMatches(substitute.Pattern, "a/b"));
    }

    [Fact]
    public void SubstitutionWhoseBracketContainsTheDelimiter()
    {
        SedSubstitute substitute = Assert.IsType<SedSubstitute>(Assert.Single(Parse("s/[/]/x/").Commands));
        Assert.True(PatternMatches(substitute.Pattern, "/"));
    }

    [Fact]
    public void SubstitutionWritingToAFile()
    {
        SedSubstitute substitute = Assert.IsType<SedSubstitute>(Assert.Single(Parse("s/a/b/w out.txt").Commands));
        Assert.Equal("out.txt", substitute.WriteFile);
    }

    [Fact]
    public void ReplacementPartsSeparateGroupsAndCaseConversion()
    {
        SedSubstitute substitute = Assert.IsType<SedSubstitute>(Assert.Single(Parse(@"s/\(a\)/[\U\1&\E]/").Commands));

        Assert.Collection(
            substitute.Replacement,
            part => Assert.Equal(SedReplacementKind.Literal, part.Kind),
            part => Assert.Equal(SedReplacementKind.UpperCaseRest, part.Kind),
            part => Assert.Equal(1, part.Group),
            part => Assert.Equal(SedReplacementKind.WholeMatch, part.Kind),
            part => Assert.Equal(SedReplacementKind.EndCaseConversion, part.Kind),
            part => Assert.Equal(SedReplacementKind.Literal, part.Kind));
    }

    [Fact]
    public void TransliterationUnescapes()
    {
        SedTransliterate transliterate = Assert.IsType<SedTransliterate>(Assert.Single(Parse(@"y/a\n/b\t/").Commands));
        Assert.Equal("a\n", transliterate.From);
        Assert.Equal("b\t", transliterate.To);
    }

    [Fact]
    public void TextCommandsAcceptBothSpellings()
    {
        Assert.Equal("hello", Assert.IsType<SedText>(Assert.Single(Parse("a hello").Commands)).Text);
        Assert.Equal("hello", Assert.IsType<SedText>(Assert.Single(Parse("a\\\nhello").Commands)).Text);
        Assert.Equal(SedTextPlacement.Insert, Assert.IsType<SedText>(Assert.Single(Parse("i text").Commands)).Placement);
        Assert.Equal(SedTextPlacement.Change, Assert.IsType<SedText>(Assert.Single(Parse("c text").Commands)).Placement);
    }

    [Fact]
    public void FileNamesRunToTheEndOfTheLine()
    {
        Assert.Equal("out;txt", Assert.IsType<SedWriteFile>(Assert.Single(Parse("w out;txt").Commands)).Path);
        Assert.Equal("in.txt", Assert.IsType<SedReadFile>(Assert.Single(Parse("r in.txt").Commands)).Path);
    }

    [Fact]
    public void BranchesResolveAgainstLabels()
    {
        SedProgram program = Parse(":a;N;$!ba");
        Assert.True(program.Labels.ContainsKey("a"));
        Assert.Equal("a", Assert.IsType<SedBranch>(program.Commands[^1]).Label);
    }

    [Fact]
    public void ABranchWithNoLabelJumpsToTheEnd()
    {
        Assert.Equal(string.Empty, Assert.IsType<SedBranch>(Assert.Single(Parse("b").Commands)).Label);
    }

    [Fact]
    public void QuitCarriesAnExitCode()
    {
        Assert.Equal(5, Assert.IsType<SedQuit>(Assert.Single(Parse("q5").Commands)).ExitCode);
        Assert.True(Assert.IsType<SedQuit>(Assert.Single(Parse("Q").Commands)).Silent);
    }

    [Fact]
    public void ListTakesAnOptionalWidth()
    {
        Assert.Equal(5, Assert.IsType<SedList>(Assert.Single(Parse("l 5").Commands)).Width);
        Assert.Equal(0, Assert.IsType<SedList>(Assert.Single(Parse("l").Commands)).Width);
    }

    [Fact]
    public void HashNOnTheFirstLineSuppressesAutoPrint()
    {
        Assert.True(Parse("#n\np").SuppressesAutoPrint);
        Assert.False(Parse("p").SuppressesAutoPrint);
    }

    [Fact]
    public void CommentsAreSkipped()
    {
        Assert.Single(Parse("# a comment\np").Commands);
    }

    [Fact]
    public void VersionRequirementIsANoOperationWhenSatisfied()
    {
        Assert.IsType<SedNoOperation>(Assert.Single(Parse("v 4.2").Commands));
    }

    [Theory]
    [InlineData("y/abc/xy/", "different lengths")]
    [InlineData("s/a/b", "unterminated")]
    [InlineData(":a;:a;p", "duplicate label")]
    [InlineData("b nowhere", "can't find label")]
    [InlineData("/a/{p", "unmatched")]
    [InlineData("p}", "unexpected")]
    [InlineData("Z", "unknown command")]
    [InlineData("1,2q", "one address")]
    [InlineData("1,p", "expected an address")]
    [InlineData("w", "missing filename")]
    [InlineData("1,2:label", "addresses")]
    public void InvalidScriptsAreErrors(string script, string expected)
    {
        Assert.Contains(expected, Error(script), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("e ls", "shell command")]
    [InlineData("s/a/b/e", "shell command")]
    [InlineData(@"s/a\|ab/x/", "alternation")]
    [InlineData("v 9.0", "newer sed")]
    public void ValidButUnownedScriptsEscalate(string script, string expected)
    {
        Assert.Contains(expected, Unsupported(script), StringComparison.Ordinal);
    }

    // An address asks only whether the line matches, so it accepts the alternation that `s` refuses.
    [Fact]
    public void AnAmbiguousAlternationIsOwnedInAnAddressAndRefusedInASubstitution()
    {
        Assert.Single(Parse(@"/a\|ab/d").Commands);
        Assert.Contains("alternation", Unsupported(@"s/a\|ab/x/"), StringComparison.Ordinal);
    }

    [Fact]
    public void ExtendedRegexModeChangesTheDialect()
    {
        SedSubstitute substitute = Assert.IsType<SedSubstitute>(Assert.Single(Parse("s/(a)(b)/x/", isExtended: true).Commands));
        Assert.True(PatternMatches(substitute.Pattern, "ab"));
    }

    [Fact]
    public void PosixModeRefusesGnuRegexExtensions()
    {
        SedParseResult result = SedScriptParser.Parse(@"s/a\+/x/", new SedParseOptions(false, AllowsGnuExtensions: false));
        Assert.NotNull(result.UnsupportedConstruct);
    }
}
