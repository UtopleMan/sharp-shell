namespace Sharp.Shell.Expansion;

// Shell glob matching: *, ?, [...] with ranges and a leading ! or ^ negation. Shared by parameter
// trimming (${x#pat}), by the globber, and later by case arms, so the three cannot drift apart.
public static class PatternMatcher
{
    public static bool Matches(string text, string pattern) => Matches(text, 0, pattern, 0);

    // The longest or shortest prefix of text matching pattern, or -1 when none does. Used by
    // ${x#pat} and ${x%pat}, which need the boundary rather than a yes/no.
    public static int MatchingPrefixLength(string text, string pattern, bool longest)
    {
        int found = -1;
        for (int length = 0; length <= text.Length; length++)
        {
            if (!Matches(text[..length], pattern))
            {
                continue;
            }

            if (!longest)
            {
                return length;
            }

            found = length;
        }

        return found;
    }

    public static int MatchingSuffixStart(string text, string pattern, bool longest)
    {
        int found = -1;
        for (int start = text.Length; start >= 0; start--)
        {
            if (!Matches(text[start..], pattern))
            {
                continue;
            }

            if (!longest)
            {
                return start;
            }

            found = start;
        }

        return found;
    }

    private static bool Matches(string text, int textIndex, string pattern, int patternIndex)
    {
        while (patternIndex < pattern.Length)
        {
            char current = pattern[patternIndex];

            if (current == '*')
            {
                return MatchesStar(text, textIndex, pattern, patternIndex);
            }

            if (textIndex >= text.Length)
            {
                return false;
            }

            if (current == '?')
            {
                textIndex++;
                patternIndex++;
                continue;
            }

            if (current == '[')
            {
                int closing = FindClassEnd(pattern, patternIndex);
                if (closing < 0)
                {
                    return MatchesLiteral(text, ref textIndex, pattern, ref patternIndex);
                }

                if (!MatchesClass(text[textIndex], pattern[(patternIndex + 1)..closing]))
                {
                    return false;
                }

                textIndex++;
                patternIndex = closing + 1;
                continue;
            }

            if (current == '\\' && patternIndex + 1 < pattern.Length)
            {
                patternIndex++;
                current = pattern[patternIndex];
            }

            if (text[textIndex] != current)
            {
                return false;
            }

            textIndex++;
            patternIndex++;
        }

        return textIndex == text.Length;
    }

    private static bool MatchesLiteral(string text, ref int textIndex, string pattern, ref int patternIndex)
    {
        if (text[textIndex] != pattern[patternIndex])
        {
            return false;
        }

        textIndex++;
        patternIndex++;
        return true;
    }

    private static bool MatchesStar(string text, int textIndex, string pattern, int patternIndex)
    {
        for (int consumed = textIndex; consumed <= text.Length; consumed++)
        {
            if (Matches(text, consumed, pattern, patternIndex + 1))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindClassEnd(string pattern, int openingBracket)
    {
        int cursor = openingBracket + 1;
        if (cursor < pattern.Length && pattern[cursor] is '!' or '^')
        {
            cursor++;
        }

        if (cursor < pattern.Length && pattern[cursor] == ']')
        {
            cursor++;
        }

        while (cursor < pattern.Length && pattern[cursor] != ']')
        {
            cursor++;
        }

        return cursor < pattern.Length ? cursor : -1;
    }

    private static bool MatchesClass(char candidate, string body)
    {
        bool negated = body.Length > 0 && body[0] is '!' or '^';
        string members = negated ? body[1..] : body;
        bool matched = false;

        for (int index = 0; index < members.Length; index++)
        {
            if (index + 2 < members.Length && members[index + 1] == '-')
            {
                matched |= candidate >= members[index] && candidate <= members[index + 2];
                index += 2;
                continue;
            }

            matched |= candidate == members[index];
        }

        return matched != negated;
    }
}
