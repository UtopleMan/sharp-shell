namespace Sharp.Shell.Tests.Support;

// A directory outside any workspace, for the tests that check what a shell rooted at the filesystem
// can reach.
internal sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory() => Path = Directory.CreateTempSubdirectory("duetui-shell-outside").FullName;

    public string Path { get; }

    public string Write(string name)
    {
        string absolute = System.IO.Path.Combine(Path, name);
        File.WriteAllText(absolute, string.Empty);
        return absolute;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
