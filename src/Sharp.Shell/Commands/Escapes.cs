using System.Text;

namespace Sharp.Shell.Commands;

// The backslash escapes echo -e and printf share.
internal static class Escapes
{
    public static string Interpret(string text)
    {
        StringBuilder output = new(text.Length);

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\\' || index + 1 >= text.Length)
            {
                output.Append(text[index]);
                continue;
            }

            index++;
            output.Append(text[index] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '0' => '\0',
                'a' => '\a',
                'b' => '\b',
                '\\' => '\\',
                _ => text[index],
            });
        }

        return output.ToString();
    }
}
