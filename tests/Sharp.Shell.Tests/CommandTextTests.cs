using Sharp.Shell.Lexing;
using Xunit;

namespace Sharp.Shell.Tests;

// The host matches rules against text, so argv must have exactly one spelling. The round-trip is
// the property that matters: whatever quoting this produces, tokenizing it must give back the argv
// it started from — otherwise the text names a different command than the one about to run.
public class CommandTextTests
{
    [Theory]
    [InlineData("echo", new[] { "hi" }, "echo hi")]
    [InlineData("dotnet", new[] { "build", "--no-restore" }, "dotnet build --no-restore")]
    [InlineData("grep", new[] { "-rn", "TODO", "src/Sharp.Shell" }, "grep -rn TODO src/Sharp.Shell")]
    [InlineData("dotnet", new[] { "build", "my proj" }, "dotnet build 'my proj'")]
    [InlineData("echo", new[] { "" }, "echo ''")]
    [InlineData("echo", new[] { "it's" }, @"echo 'it'\''s'")]
    [InlineData("echo", new[] { "\"quoted\"" }, "echo '\"quoted\"'")]
    [InlineData("echo", new[] { "$HOME" }, "echo '$HOME'")]
    [InlineData("echo", new[] { "`id`" }, "echo '`id`'")]
    [InlineData("echo", new[] { "*.cs" }, "echo '*.cs'")]
    [InlineData("echo", new[] { "~/notes" }, "echo '~/notes'")]
    [InlineData("echo", new[] { "a\nb" }, "echo 'a\nb'")]
    [InlineData("sed", new[] { "-i", "s/a/b/", "file.txt" }, "sed -i s/a/b/ file.txt")]
    [InlineData("ls", new[] { "café" }, "ls café")]
    public void SpellsTheCommand(string program, string[] arguments, string expected) =>
        Assert.Equal(expected, CommandText.Of(program, arguments));

    [Fact]
    public void AProgramWithNoArgumentsIsJustItsName() =>
        Assert.Equal("pwd", CommandText.Of("pwd", []));

    [Theory]
    [InlineData("echo", "hi")]
    [InlineData("dotnet", "build", "my proj")]
    [InlineData("echo", "")]
    [InlineData("echo", "it's", "a \"quote\"")]
    [InlineData("echo", "$HOME", "`id`", "$(id)")]
    [InlineData("echo", "a\nb", "tab\there")]
    [InlineData("echo", "*.cs", "?", "[abc]", "{a,b}")]
    [InlineData("echo", "~", "!", "#", "&", "|", ";", "<", ">")]
    [InlineData("echo", "café", "日本語", "naïve")]
    [InlineData("echo", @"back\slash", "'")]
    public void TokenizingTheTextGivesBackTheArgv(params string[] argv)
    {
        string text = CommandText.Of(argv[0], argv[1..]);

        Assert.Equal(argv, Argv(text));
    }

    private static IReadOnlyList<string> Argv(string commandLine)
    {
        LexResult lexed = Lexer.Tokenize(commandLine);

        Assert.Null(lexed.Error);

        List<string> words = [];
        foreach (Token token in lexed.Tokens.Where(token => token.Kind == TokenKind.Word))
        {
            Assert.True(token.Word!.IsFullyLiteral, $"'{token.Text}' did not tokenize as literal text");
            words.Add(token.Word.LiteralText);
        }

        return words;
    }
}
