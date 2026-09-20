namespace Sharp;

// What a session needs to know before its first line: where it is confined, where it opens, the two
// switches that change what happens to a line the shell does not own, whether it reads its rc files,
// and where its home directory is.
//
// Home is here rather than read from the environment at the point of use so that a test can point it
// at a temporary directory. Production leaves it null and the session falls back to $HOME.
internal sealed record SessionSettings(
    string Root,
    string StartDirectory,
    bool Explains,
    bool Strict,
    bool ReadsRcFiles = true,
    string? Home = null);
