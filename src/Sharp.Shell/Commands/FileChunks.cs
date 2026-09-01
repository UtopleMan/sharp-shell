namespace Sharp.Shell.Commands;

// Lazy file reading in fixed chunks, so a consumer that stops early stops the read.
internal static class FileChunks
{
    private const int ChunkCharacters = 8192;

    public static IEnumerable<string> Read(string absolutePath)
    {
        using StreamReader reader = new(absolutePath);
        char[] buffer = new char[ChunkCharacters];

        while (true)
        {
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                yield break;
            }

            yield return new string(buffer, 0, read);
        }
    }
}
