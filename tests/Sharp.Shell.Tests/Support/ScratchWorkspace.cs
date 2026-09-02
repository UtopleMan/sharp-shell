using Sharp.Shell.Execution;

namespace Sharp.Shell.Tests.Support;

// Classification resolves the files a line names against a shell state, so every classifier call
// needs one. The corpus measurements only ask which tier a line lands in, so they classify against
// an empty scratch directory: nothing to glob, nothing to touch, and the same answer every run.
internal static class ScratchWorkspace
{
    public static ShellState State { get; } =
        new(Directory.CreateTempSubdirectory("sharp-classification").FullName);
}
