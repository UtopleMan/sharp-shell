namespace Sharp.Shell.Commands.Sed;

// The seam between the sed machine and the workspace. Every path a script names goes through an
// implementation of this, so confinement is decided in one place rather than at each of the six
// commands that can touch a file.
internal interface ISedFileSystem
{
    // Missing files are not an error for `r`: real sed treats an unreadable file as empty.
    string ReadAll(string path);

    string? ReadLine(string path);

    void Append(string path, string text);

    bool IsStandardOutput(string path);

    bool IsStandardError(string path);
}
