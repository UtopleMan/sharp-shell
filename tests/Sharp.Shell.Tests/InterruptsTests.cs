using Sharp;
using Xunit;

namespace Sharp.Shell.Tests;

// Ctrl+C. The shell survives it, and — the part that is easy to get wrong — so does every command
// after it.
public class InterruptsTests
{
    [Fact]
    public void NothingIsCancelledUntilItIsRaised()
    {
        using Interrupts interrupts = new();

        Assert.False(interrupts.WasRaised);
        Assert.False(interrupts.Token.IsCancellationRequested);
    }

    [Fact]
    public void RaisingCancelsTheRunningCommand()
    {
        using Interrupts interrupts = new();

        interrupts.Raise();

        Assert.True(interrupts.WasRaised);
        Assert.True(interrupts.Token.IsCancellationRequested);
    }

    // The bug this exists to prevent: one source reused for the session stays cancelled after the
    // first Ctrl+C, and every later command runs against a dead token — a shell that looks alive
    // and does nothing.
    [Fact]
    public void TheNextCommandGetsALiveToken()
    {
        using Interrupts interrupts = new();
        interrupts.Raise();

        interrupts.Reset();

        Assert.False(interrupts.WasRaised);
        Assert.False(interrupts.Token.IsCancellationRequested);
    }

    [Fact]
    public void ResettingAnUncancelledSourceKeepsIt()
    {
        using Interrupts interrupts = new();
        CancellationToken before = interrupts.Token;

        interrupts.Reset();

        Assert.Equal(before, interrupts.Token);
    }

    [Fact]
    public void EachRaiseIsAnsweredByItsOwnReset()
    {
        using Interrupts interrupts = new();

        for (int round = 0; round < 3; round++)
        {
            interrupts.Raise();
            Assert.True(interrupts.Token.IsCancellationRequested);

            interrupts.Reset();
            Assert.False(interrupts.Token.IsCancellationRequested);
        }
    }
}
