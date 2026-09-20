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
        "printf 'abcabc\\n' | grep -o abc",
        "printf 'a\\nbab\\n' | grep -o -n a",
        "printf 'cat cats\\n' | grep -o -w cat",

        // The two builtins read octal differently and keep the backslash on an escape neither
        // defines, so both spellings are pinned against the real thing.
        "printf 'a\\vb\\n'",
        "printf 'a\\fb\\n'",
        "printf 'a\\101b\\n'",
        "printf 'a\\010b\\n'",
        "printf 'a\\x41b\\n'",
        "printf 'a\\qb\\n'",
        "echo -e 'a\\0101b'",
        "echo -e 'a\\101b'",
        "echo -e 'a\\x41b'",
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

        // IFS is a variable, so these pin the two kinds of separator against the real thing: runs of
        // whitespace collapse, a non-whitespace separator delimits every time it appears, and an empty
        // IFS stops splitting altogether. Absolute paths are deliberately absent from every case here
        // — the oracle and this shell run in different temp roots, so nothing that prints one can
        // ratchet.
        "IFS=:; x=a:b:c; for i in $x; do echo $i; done",
        "IFS=:; x='a b'; for i in $x; do echo \"[$i]\"; done",
        "IFS=:; x=a::b; for i in $x; do echo \"[$i]\"; done",
        "IFS=:; x=:a; for i in $x; do echo \"[$i]\"; done",
        "IFS=:; x=a:; for i in $x; do echo \"[$i]\"; done",
        "IFS=:; x=:; for i in $x; do echo \"[$i]\"; done",
        "IFS=; v='a b'; for i in $v; do echo \"[$i]\"; done",
        "IFS=' :'; x='a  :b'; for i in $x; do echo \"[$i]\"; done",
        "HOME=/home/me; echo ~",
        "HOME=/home/me; echo ~/notes",

        // The execution options, which change what a line does rather than what a word means. The
        // exemptions are the interesting half: a command whose failure is being *tested* is not a
        // command errexit is about.
        "set -e; false; echo never",
        "set -e; false && echo x; echo after",
        "set -e; true && false; echo after",
        "set -e; if false; then echo t; fi; echo after",
        "set -e; while false; do echo body; done; echo after",
        "set -e; ! false; echo after",
        "set -e; ! true; echo after",
        "set -u; echo $nope; echo after",
        "set -u; echo ${nope-fallback}",
        "set -u; x=1; echo $x",
        "set -o pipefail; false | true; echo $?",
        "set -o pipefail; true | false; echo $?",
        "false | true; echo $?",
        "set -e; echo $(echo one; false; echo two)",

        // Aliases, in the shape bash needs to expand them at all: non-interactive bash wants
        // `shopt -s expand_aliases` *and* the definition on an earlier line, because it expands at parse
        // time. This shell always expands and has no such option, so the `|| true` carries the oracle and
        // this shell through the same three lines. Anything printing an absolute path is deliberately
        // absent — the two sides run in different temp roots — which is why the directory stack is
        // counted rather than printed.
        "shopt -s expand_aliases || true\nalias greet='echo hello'\ngreet",
        "shopt -s expand_aliases || true\nalias greet='echo hello'\ngreet there",
        "shopt -s expand_aliases || true\nalias greet='echo hello'\necho greet",
        "shopt -s expand_aliases || true\nalias ll='echo listed'\nll; ll",
        "mkdir -p a; pushd a > junk; for d in $(dirs); do echo x; done",
        "mkdir -p a; pushd a > junk; popd > junk; for d in $(dirs); do echo x; done",
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

        // Constructs that were refused by name until the per-command approval hook landed. $$ is
        // deliberately absent: this shell answers a synthetic value, and no real bash agrees.
        "greet() { echo hi; }; greet",
        "function greet { echo from-the-keyword; }; greet",
        "function greet() { echo both-forms; }; greet",
        "add() { echo $(($1 + $2)); }; add 2 3",
        "count() { echo $#; }; count a b c",
        "first() { echo \"$1\"; }; first 'a b'",
        "all() { echo $@; }; all x y z",
        "outer() { inner; }; inner() { echo nested; }; outer",
        "greet() { echo hi; }; greet | tr a-z A-Z",
        "pick() { cd /; }; echo before",
        "echo \"[$!]\"",
        "echo $#",
        "echo \"[${1}]\"",
        "[[ -n x ]]; echo $?",
        "[[ -z '' ]]; echo $?",
        "[[ abc == a* ]]; echo $?",
        "[[ abc != a* ]]; echo $?",
        "[[ abc = abc ]]; echo $?",
        "[[ abc =~ ^a.c$ ]]; echo $?",
        "[[ abc =~ ^b ]]; echo $?",
        "[[ 2 -lt 3 ]]; echo $?",
        "[[ a == a && b == b ]]; echo $?",
        "[[ a == z || b == b ]]; echo $?",
        "[[ ! -f nothing-here ]]; echo $?",
        "[[ ( -n x ) ]]; echo $?",
        "v='a b'; [[ -n $v ]]; echo $?",
        "v='a b'; [[ $v == 'a b' ]]; echo $?",
        "f=notes.cs; [[ $f == *.cs ]]; echo $?",
        "if [[ -n x ]]; then echo taken; fi",
        "printf '1\\n' | select item in alpha beta; do echo $item; done",
        "printf '2\\n' | select item in alpha beta; do echo $item; done",
        "printf '9\\n' | select item in alpha beta; do echo \"[$item]\"; done",
    ];
}
