using Sharp.Shell.Execution;
using Xunit;

namespace Sharp.Shell.Tests;

public class ShellStateTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("duetui-shell-state").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void StartsAtTheRoot()
    {
        ShellState state = new(root);

        Assert.Equal(Path.GetFullPath(root), state.WorkingDirectory);
    }

    [Fact]
    public void ChangesIntoASubdirectory()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        ShellState state = new(root);

        Assert.True(state.TryChangeDirectory("sub", out string error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "sub"), state.WorkingDirectory);
    }

    [Fact]
    public void RefusesToLeaveTheRoot()
    {
        ShellState state = new(root);

        Assert.False(state.TryChangeDirectory("..", out string error));
        Assert.Contains("outside the workspace", error, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(root), state.WorkingDirectory);
    }

    [Fact]
    public void RefusesAnAbsolutePathOutsideTheRoot()
    {
        ShellState state = new(root);

        Assert.False(state.TryChangeDirectory("/", out _));
    }

    [Fact]
    public void RefusesAMissingDirectory()
    {
        ShellState state = new(root);

        Assert.False(state.TryChangeDirectory("nope", out string error));
        Assert.Contains("nope", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesRelativeToTheWorkingDirectory()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        ShellState state = new(root);
        state.TryChangeDirectory("sub", out _);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "sub", "f.txt"), state.Resolve("f.txt"));
    }

    // A root of "/" means unconfined, which is what a shell run outside the sandbox wants. The
    // first implementation appended a separator to the trimmed root, producing "//", so with a
    // filesystem root nothing was ever inside it and every path was refused.
    [Fact]
    public void TheFilesystemRootContainsEverything()
    {
        ShellState state = new(Path.GetPathRoot(Path.GetTempPath())!);

        Assert.True(state.IsInsideRoot(root));
        Assert.True(state.TryChangeDirectory(root, out _));
        Assert.Equal(Path.GetFullPath(root), state.WorkingDirectory);
    }

    [Fact]
    public void KnowsWhenAPathEscapesTheRoot()
    {
        ShellState state = new(root);

        Assert.True(state.IsInsideRoot(state.Resolve("a/b")));
        Assert.False(state.IsInsideRoot(state.Resolve("../outside")));
    }
}
