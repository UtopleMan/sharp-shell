namespace Sharp.Shell.Commands;

// basename and dirname are defined on the text of a path, with '/' as the separator, whatever the
// host filesystem uses. Path.GetFileName would follow the platform separator instead.
internal static class PathText
{
    public static string LastSegment(string path)
    {
        string trimmed = path.TrimEnd('/');

        if (trimmed.Length == 0)
        {
            return "/";
        }

        int separator = trimmed.LastIndexOf('/');
        return separator < 0 ? trimmed : trimmed[(separator + 1)..];
    }

    public static string Parent(string path)
    {
        string trimmed = path.TrimEnd('/');
        int separator = trimmed.LastIndexOf('/');

        if (separator < 0)
        {
            return ".";
        }

        return separator == 0 ? "/" : trimmed[..separator];
    }
}
