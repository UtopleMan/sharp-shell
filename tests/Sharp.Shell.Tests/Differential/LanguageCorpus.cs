namespace Sharp.Shell.Tests.Differential;

// The committed half of Layer 2: shell snippets that exercise the language rather than the
// machine, so the same lines mean the same thing on every checkout and the ratchet below is
// meaningful. Every one of them is read-only and confined to the test's own temp workspace.
//
// The other half — commands mined from real sessions — is machine-specific and therefore
// report-only; see MinedDifferentialTests.
//
// Deliberately NOT here: anything whose expected output is coreutils *formatting* rather than shell
// language. BSD `wc` and `uniq -c` pad their counts into columns and the GNU ones do not, so those
// cases cannot ratchet portably — they would pass on macOS and fail on a Linux CI runner, or the
// reverse. They are asserted directly instead, in AppletOutputFormatTests, where the expectation is
// *our* format rather than the local platform's.
public static class LanguageCorpus
{
    public static IReadOnlyList<string> Commands { get; } =
    [
        "echo hello",
        "echo -n no-newline",
        "echo a b   c",
        "printf '%s-%d\\n' x 7",
        "printf '%s\\n' one two three",
        "true && echo yes",
        "false || echo fallback",
        "! true; echo $?",
        "echo one; echo two",
        "printf 'b\\na\\nc\\n' | sort",
        "printf 'a:b:c\\n' | cut -d : -f 2",
        "printf 'abc\\n' | tr 'a-z' 'A-Z'",
        "printf 'one\\ntwo\\nthree\\n' | head -2",
        "printf 'one\\ntwo\\nthree\\n' | tail -1",
        "printf 'x\\ny\\n' | grep y",
        "printf 'x\\ny\\n' | grep -c x",

        // The GNU regex escapes BSD grep also implements, so the oracle keeps the translator honest
        // on either platform. `\\s` is deliberately absent: BSD grep does not implement it.
        "printf 'a1 b\\n' | grep -c '\\w'",
        "printf 'a-b\\n' | grep -c 'a\\Wb'",
        "printf 'cat cathode\\n' | grep -c '\\<cat\\>'",
        "printf 'cat cathode\\n' | grep -c '\\bcat\\b'",
        "printf 'concatenate\\ncat dog\\n' | grep -c 'cat\\B'",
        "x=1; echo $x",
        "x=1; echo ${x}2",
        "echo ${missing:-default}",
        "y=set; echo ${y:+present}",
        "s=abcdef; echo ${#s}",
        "s=a/b/c; echo ${s#*/}",
        "s=a/b/c; echo ${s##*/}",
        "s=a/b/c; echo ${s%/*}",
        "s=aXbXc; echo ${s//X/-}",
        "echo $((2 + 3 * 4))",
        "echo $(( (2 + 3) * 4 ))",
        "echo $((7 / 2)) $((7 % 2))",
        "echo $((1 < 2)) $((1 && 0))",
        "echo $(echo nested)",
        "echo \"quoted $(echo sub)\"",
        "v=\"a  b\"; echo $v",
        "v=\"a  b\"; echo \"$v\"",
        "echo 'single $notexpanded'",
        "echo \"double ${x:-d}\"",
        "if true; then echo then-branch; fi",
        "if false; then echo t; else echo else-branch; fi",
        "if false; then echo t; elif true; then echo elif-branch; fi",
        "i=0; while [ $i -lt 3 ]; do i=$((i+1)); done; echo $i",
        "i=0; until [ $i -ge 2 ]; do i=$((i+1)); done; echo $i",
        "for i in a b c; do echo $i; done",
        "case x in x) echo matched;; *) echo other;; esac",
        "case zz in x) echo m;; *) echo default;; esac",
        "case b in a|b) echo alternatives;; esac",
        "x=outer; (x=inner); echo $x",
        "x=outer; { x=inner; }; echo $x",
        "[ -z '' ]; echo $?",
        "[ a = a ]; echo $?",
        "[ 2 -lt 1 ]; echo $?",
        "test 1 -ne 2; echo $?",
        "basename /a/b/file.txt",
        "basename /a/b/file.txt .txt",
        "dirname /a/b/file.txt",
        "dirname bare",
        "echo hi > f.txt && cat f.txt",
        "echo a > f.txt; echo b >> f.txt; cat f.txt",
        "cat << EOF\nheredoc body\nEOF",
        "v=value; cat << EOF\n$v\nEOF",
        "cat << 'EOF'\n$v\nEOF",
        "echo a; exit 3; echo b",
        "for i in 1 2 3; do echo $i; done | head -2",
        "echo one two | cut -d ' ' -f 2",
        "sort -n << EOF\n10\n2\nEOF",
    ];
}
