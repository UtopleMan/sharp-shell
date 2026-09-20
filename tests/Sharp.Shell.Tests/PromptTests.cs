using Sharp;
using Xunit;

namespace Sharp.Shell.Tests;

// PS1 and PS2, with the escapes this shell implements and no approximations of the ones it does not.
public class PromptTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("sharp-prompt").FullName;

    private readonly StringWriter output = new();

    private readonly StringWriter errors = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(root, recursive: true);
    }

    private Session Confined() => new(new SessionSettings(root, root, false, true, false, root), output, errors);

    [Fact]
    public void WithoutPs1ThePromptIsTheWorkingDirectory()
    {
        Assert.Equal($"{Path.GetFileName(root)}$ ", Confined().Prompt);
    }

    [Fact]
    public void WithoutPs2TheContinuationPromptIsAnAngleBracket()
    {
        Assert.Equal("> ", Confined().ContinuationPrompt);
    }

    [Fact]
    public void Ps1ReplacesThePrompt()
    {
        Session session = Confined();
        session.Run("PS1='shell% '", CancellationToken.None);

        Assert.Equal("shell% ", session.Prompt);
    }

    [Fact]
    public void Ps2ReplacesTheContinuationPrompt()
    {
        Session session = Confined();
        session.Run("PS2='.. '", CancellationToken.None);

        Assert.Equal(".. ", session.ContinuationPrompt);
    }

    [Fact]
    public void TheWorkingDirectoryEscapeIsTheFullPathWhenItIsNotUnderHome()
    {
        Session session = Confined();
        session.Run("HOME=/nowhere; PS1='[\\w] '", CancellationToken.None);

        Assert.Equal($"[{root}] ", session.Prompt);
    }

    [Fact]
    public void TheWorkingDirectoryEscapeIsHomeRelative()
    {
        Session session = Confined();
        session.Run($"HOME={root}; PS1='[\\w] '", CancellationToken.None);

        Assert.Equal("[~] ", session.Prompt);
    }

    [Fact]
    public void TheBasenameEscapeIsTheLastComponent()
    {
        Session session = Confined();
        session.Run("PS1='[\\W] '", CancellationToken.None);

        Assert.Equal($"[{Path.GetFileName(root)}] ", session.Prompt);
    }

    [Fact]
    public void TheStatusEscapeIsTheLastExitCode()
    {
        Session session = Confined();
        session.Run("PS1='[\\?] '", CancellationToken.None);
        session.Run("false", CancellationToken.None);

        Assert.Equal("[1] ", session.Prompt);
    }

    [Fact]
    public void TheDollarEscapeIsADollar()
    {
        Session session = Confined();
        session.Run("PS1='\\$ '", CancellationToken.None);

        Assert.Equal("$ ", session.Prompt);
    }

    // \h, \u, \t and the colour escapes are not implemented. Left as typed rather than approximated:
    // a hostname nobody looked up would be a lie, and a visible \h is a question.
    [Fact]
    public void AnUnsupportedEscapeIsLeftAsItWasTyped()
    {
        Session session = Confined();
        session.Run("PS1='\\h$ '", CancellationToken.None);

        Assert.Equal("\\h$ ", session.Prompt);
    }
}
