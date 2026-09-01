using Sharp.Shell.Lexing;
using Xunit;

namespace Sharp.Shell.Tests;

public class LexerTests
{
    private static IReadOnlyList<Token> Tokens(string source)
    {
        LexResult result = Lexer.Tokenize(source);
        Assert.Null(result.Error);
        return result.Tokens;
    }

    private static Word SingleWord(string source)
    {
        Token token = Assert.Single(Tokens(source), candidate => candidate.Kind == TokenKind.Word);
        Assert.NotNull(token.Word);
        return token.Word!;
    }

    [Fact]
    public void SplitsAWordPerWhitespaceRun()
    {
        IReadOnlyList<Token> tokens = Tokens("echo   hi");

        Assert.Equal(
            ["echo", "hi"],
            tokens.Where(token => token.Kind == TokenKind.Word).Select(token => token.Word!.LiteralText));
    }

    [Fact]
    public void SingleQuotedTextIsOneWordWithOneSingleQuotedPart()
    {
        Word word = SingleWord("'a b'");

        WordPart part = Assert.Single(word.Parts);
        Assert.Equal(WordPartKind.SingleQuoted, part.Kind);
        Assert.Equal("a b", part.Text);
        Assert.True(word.IsFullyLiteral);
        Assert.Equal("a b", word.LiteralText);
    }

    [Fact]
    public void SingleQuotesDoNotExpand()
    {
        Word word = SingleWord("'$x'");

        WordPart part = Assert.Single(word.Parts);
        Assert.Equal(WordPartKind.SingleQuoted, part.Kind);
        Assert.Equal("$x", part.Text);
    }

    [Fact]
    public void DoubleQuotedTextKeepsItsNestedParts()
    {
        Word word = SingleWord("\"a $x\"");

        WordPart part = Assert.Single(word.Parts);
        Assert.Equal(WordPartKind.DoubleQuoted, part.Kind);
        Assert.NotNull(part.Nested);
        Assert.Equal([WordPartKind.Literal, WordPartKind.Parameter], part.Nested!.Select(nested => nested.Kind));
        Assert.Equal("x", part.Nested![1].Text);
        Assert.False(word.IsFullyLiteral);
    }

    [Fact]
    public void BracedParameterKeepsItsWholeExpression()
    {
        Word word = SingleWord("${name:-fallback}");

        WordPart part = Assert.Single(word.Parts);
        Assert.Equal(WordPartKind.Parameter, part.Kind);
        Assert.Equal("name:-fallback", part.Text);
    }

    [Fact]
    public void CommandSubstitutionCapturesItsInnerSource()
    {
        Word word = SingleWord("$(echo $(date))");

        WordPart part = Assert.Single(word.Parts);
        Assert.Equal(WordPartKind.CommandSubstitution, part.Kind);
        Assert.Equal("echo $(date)", part.Text);
    }

    [Fact]
    public void BackticksLexAsCommandSubstitution()
    {
        Word word = SingleWord("`date`");

        WordPart part = Assert.Single(word.Parts);
        Assert.Equal(WordPartKind.CommandSubstitution, part.Kind);
        Assert.Equal("date", part.Text);
    }

    [Fact]
    public void ArithmeticExpansionIsItsOwnKind()
    {
        Word word = SingleWord("$((1 + 2))");

        WordPart part = Assert.Single(word.Parts);
        Assert.Equal(WordPartKind.Arithmetic, part.Kind);
        Assert.Equal("1 + 2", part.Text);
    }

    [Fact]
    public void LeadingTildeIsItsOwnKind()
    {
        Word word = SingleWord("~/src");

        Assert.Equal(WordPartKind.Tilde, word.Parts[0].Kind);
        Assert.Equal(WordPartKind.Literal, word.Parts[1].Kind);
        Assert.Equal("/src", word.Parts[1].Text);
    }

    [Fact]
    public void BackslashEscapesTheNextCharacter()
    {
        Word word = SingleWord("a\\ b");

        Assert.True(word.IsFullyLiteral);
        Assert.Equal("a b", word.LiteralText);
    }

    [Theory]
    [InlineData("a | b", "|")]
    [InlineData("a || b", "||")]
    [InlineData("a && b", "&&")]
    [InlineData("a & b", "&")]
    [InlineData("a ; b", ";")]
    [InlineData("a > b", ">")]
    [InlineData("a >> b", ">>")]
    [InlineData("a < b", "<")]
    [InlineData("a << b", "<<")]
    [InlineData("a <<- b", "<<-")]
    [InlineData("a &> b", "&>")]
    public void RecognisesOperators(string source, string expected)
    {
        Assert.Contains(Tokens(source), token => token.Kind == TokenKind.Operator && token.Text == expected);
    }

    [Fact]
    public void FileDescriptorPrefixBindsToTheRedirectionOperator()
    {
        Assert.Contains(Tokens("cmd 2> log"), token => token.Kind == TokenKind.Operator && token.Text == "2>");
        Assert.Contains(Tokens("cmd 2>&1"), token => token.Kind == TokenKind.Operator && token.Text == "2>&");
    }

    [Fact]
    public void DigitsFollowedByWhitespaceStayAWord()
    {
        Assert.Contains(
            Tokens("head -n 2 > out"),
            token => token.Kind == TokenKind.Word && token.Word!.LiteralText == "2");
    }

    [Fact]
    public void ProcessSubstitutionLexesAsItsOwnOperator()
    {
        Assert.Contains(Tokens("diff <(a) <(b)"), token => token.Kind == TokenKind.Operator && token.Text == "<(");
    }

    [Fact]
    public void NewlinesAreSignificant()
    {
        Assert.Contains(Tokens("a\nb"), token => token.Kind == TokenKind.Newline);
    }

    [Fact]
    public void LineContinuationJoinsTwoLines()
    {
        Assert.DoesNotContain(Tokens("a \\\nb"), token => token.Kind == TokenKind.Newline);
    }

    [Fact]
    public void CommentsRunToEndOfLine()
    {
        IReadOnlyList<Token> tokens = Tokens("echo hi # trailing\necho there");

        Assert.Equal(
            ["echo", "hi", "echo", "there"],
            tokens.Where(token => token.Kind == TokenKind.Word).Select(token => token.Word!.LiteralText));
    }

    [Fact]
    public void HashInsideAWordIsNotAComment()
    {
        Assert.Equal("a#b", SingleWord("a#b").LiteralText);
    }

    [Fact]
    public void EndsWithAnEndOfInputToken()
    {
        Assert.Equal(TokenKind.EndOfInput, Tokens("echo hi")[^1].Kind);
    }

    [Theory]
    [InlineData("echo 'unterminated")]
    [InlineData("echo \"unterminated")]
    [InlineData("echo $(unterminated")]
    [InlineData("echo ${unterminated")]
    public void UnterminatedQuotingIsAnErrorNotAnException(string source)
    {
        LexResult result = Lexer.Tokenize(source);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Tokens);
    }
}
