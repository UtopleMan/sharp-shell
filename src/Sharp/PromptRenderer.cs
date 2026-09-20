namespace Sharp;

// PS1 and PS2. The escapes implemented here are the working directory in three forms and the last
// exit status; every other backslash escape bash defines — \h, \u, \t, \!, the colour sequences — is
// left exactly as it was typed and named as unsupported in src/Sharp/README.md. A hostname nobody
// looked up would be a lie, and a visible \h is a question.
internal static class PromptRenderer
{
    public static string Render(string format, string workingDirectory, string? home, int lastExitCode)
    {
        System.Text.StringBuilder rendered = new(format.Length);

        for (int index = 0; index < format.Length; index++)
        {
            if (format[index] != '\\' || index + 1 == format.Length)
            {
                rendered.Append(format[index]);
                continue;
            }

            string? replacement = Replacement(format[index + 1], workingDirectory, home, lastExitCode);

            if (replacement is null)
            {
                rendered.Append(format[index]);
                continue;
            }

            rendered.Append(replacement);
            index++;
        }

        return rendered.ToString();
    }

    private static string? Replacement(char escape, string workingDirectory, string? home, int lastExitCode) =>
        escape switch
        {
            'w' => HomeRelative(workingDirectory, home),
            'W' => Path.GetFileName(Path.TrimEndingDirectorySeparator(workingDirectory)),
            '?' => lastExitCode.ToString(),
            '$' => "$",
            '\\' => "\\",
            _ => null,
        };

    private static string HomeRelative(string workingDirectory, string? home)
    {
        if (home is null || home.Length == 0)
        {
            return workingDirectory;
        }

        if (workingDirectory == home)
        {
            return "~";
        }

        return workingDirectory.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? $"~{workingDirectory[home.Length..]}"
            : workingDirectory;
    }
}
