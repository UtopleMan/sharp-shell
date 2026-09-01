namespace Sharp;

// What a session needs to know before its first line: where it is confined, where it opens, and
// the two switches that change what happens to a line the shell does not own.
internal sealed record SessionSettings(string Root, string StartDirectory, bool Explains, bool Strict);
