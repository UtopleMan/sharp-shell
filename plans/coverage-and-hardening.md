# Coverage lift, agent-corpus mining, and complexity refactors

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the measured risk hotspots in `Sharp.Shell`, replace the machine-local mined
corpus with a committed public one mined from SWE-bench and Terminal-Bench trajectories, and
flatten the three pure-complexity methods the CRAP analysis flagged.

**Architecture:** Tests first (phases 1–3 attack the CRAP hotspots at the command-line seam via
`ShellHarness`, matching the house test idiom), then the public corpus (phase 4, fixed data so
rates can be ratcheted), then refactors under the protection of 100% existing coverage (phase 5),
then in-process coverage for the `sharp` binary's session logic (phase 6).

**Tech Stack:** xunit.v3 on VSTest (`--filter "FullyQualifiedName~X"`), coverlet.collector
(Cobertura), `ShellHarness` / `BashOracle` test support, python3 for the one-off miner.

**Spec:** The coverage analysis that produced these numbers is
`tests/Sharp.Shell.Tests/TestResults/coverage-analysis/coverage-analysis.md` (gitignored — the
load-bearing numbers are repeated inline below, so this plan stands alone). Baseline: 93.1% line,
88.4% branch, 1051 methods, 31 with CRAP > 30, 1368 tests green in 2m11s.

## Global constraints

- Never commit without the user's explicit approval. Each phase ends with a proposed commit;
  ask before running `git commit`.
- Branch first: `feature/coverage-hardening` off the current work once the pending oracle fix
  lands (phase 0).
- Run `dotnet test tests/Sharp.Shell.Tests` before every commit.
- House style: file-scoped namespaces, primary constructors, collection expressions, no
  underscore prefixes, comments only as warnings of consequences, explicit types in tests as in
  the neighbouring files.
- Test idiom: drive real command lines through `ShellHarness` where the behaviour is
  user-visible; drive internals directly (`InternalsVisibleTo` already grants access) where the
  method is an internal seam like `SedOptions.Parse`.
- The vendored oils corpus stays pristine; our expectations live in
  `tests/Sharp.Shell.Tests/corpus/expected-failures.txt`.

## For future agents

Mark checkboxes `- [x]` as you go; set each phase's **Status** to `Complete` and write its
**Phase Summary** with real verification output before moving on.

Reference points:

- `tests/Sharp.Shell.Tests/Support/ShellHarness.cs` — runs a command line, returns
  `ShellResult` (`Stdout`, `Stderr`, `ExitCode`); disposable, one temp workspace per instance.
- `tests/Sharp.Shell.Tests/SedAppletTests.cs` — the Theory/InlineData-over-command-lines idiom.
- `tests/Sharp.Shell.Tests/Differential/BashOracle.cs` — compares a command against system
  bash; ten-second cap, kills the tree on timeout.
- `tests/Sharp.Shell.Tests/Differential/MinedDifferentialTests.cs` — the
  classify-then-compare pattern phase 4's new test copies: only `Owned` and non-mutating lines
  are executed.
- `src/Sharp.Shell/Commands/Sed/SedOptions.cs:131` — `ReadLongOption`, the CRAP 1018 method.
- `tests/Sharp.Shell.Tests/corpus/awk-agent-oneliners.txt` — the committed-corpus format
  phase 4 follows: one command per line, `#` comments allowed.

---

## Phase 0: Land the pending fixes

Status: Not started

The oracle deadlock fix in the working tree is a prerequisite: without it any non-terminating
corpus line hangs the suite forever, and phases 2 and 4 add exactly that kind of line.

- [ ] **Step 1:** Verify the working tree holds only the intended changes:
      `git status --short` — expected: `BashOracle.cs`, `ExternalOracle.cs`,
      `ProcessCommandExecutor.cs`, `Sharp.Shell.Tests.csproj` (coverlet.collector),
      `README.md` (security architecture section), this plan file.
- [ ] **Step 2:** `dotnet test tests/Sharp.Shell.Tests` — expected: 1368 passed.
- [ ] **Step 3:** With approval, commit in two pieces:
      `fix: bound the oracle helpers so a non-terminating command cannot hang the suite`
      (three test-support files + csproj), and
      `docs: add the security architecture section to the readme` (README.md).
- [ ] **Step 4:** `git checkout -b feature/coverage-hardening`

**Phase Summary:** _pending_

---

## Phase 1: Sed subsystem tests

Status: Not started

Targets: `SedOptionReader.ReadLongOption` (complexity 64, 38.5% covered, CRAP 1018.6),
`SedFileSystem.ReadLine` (0%), `SedMachine.RenderCharacter` (41.7%),
`SedScriptParser.UnescapeCharacter` (50%).

### Task 1.1: SedOptions long-option table

**Files:**
- Create: `tests/Sharp.Shell.Tests/SedOptionsTests.cs`

**Interfaces:**
- Consumes: `SedOptions.Parse(IReadOnlyList<string>)` (internal, visible to tests) returning
  `SedOptions` with `SuppressesAutoPrint`, `ScriptSources`, `IsExtendedRegex`, `IsSeparate`,
  `IsInPlace`, `InPlaceSuffix`, `IsNullSeparated`, `IsPosix`, `LineWidth`, `Error`,
  `UnsupportedFlag`.

- [ ] **Step 1: Write the failing/green matrix** (most cases pass already; the point is pinning
      every long option — the uncovered 16 lines are the long-option arms agents never write):

```csharp
using Sharp.Shell.Commands.Sed;
using Xunit;

namespace Sharp.Shell.Tests;

public class SedOptionsTests
{
    [Theory]
    [InlineData("--quiet")]
    [InlineData("--silent")]
    public void QuietSuppressesAutoPrint(string flag)
    {
        Assert.True(SedOptions.Parse([flag, "p"]).SuppressesAutoPrint);
    }

    [Fact]
    public void ExpressionWithEqualsCarriesTheScript()
    {
        SedOptions options = SedOptions.Parse(["--expression=s/a/b/"]);
        Assert.Equal("s/a/b/", options.InlineScript);
    }

    [Fact]
    public void ExpressionTakesTheNextArgumentAsValue()
    {
        SedOptions options = SedOptions.Parse(["--expression", "s/a/b/"]);
        Assert.Equal("s/a/b/", options.InlineScript);
    }

    [Fact]
    public void FileRecordsAScriptFileSource()
    {
        SedOptions options = SedOptions.Parse(["--file=x.sed"]);
        Assert.True(options.HasScriptFile);
    }

    [Theory]
    [InlineData("--regexp-extended")]
    public void ExtendedRegexIsRecognised(string flag)
    {
        Assert.True(SedOptions.Parse([flag, "s/a/b/"]).IsExtendedRegex);
    }

    [Theory]
    [InlineData("--null-data")]
    [InlineData("--zero-terminated")]
    public void NullSeparationIsRecognised(string flag)
    {
        Assert.True(SedOptions.Parse([flag, "p"]).IsNullSeparated);
    }

    [Fact]
    public void InPlaceWithoutSuffixIsBare()
    {
        SedOptions options = SedOptions.Parse(["--in-place", "s/a/b/"]);
        Assert.True(options.IsInPlace);
        Assert.Equal(string.Empty, options.InPlaceSuffix);
    }

    [Fact]
    public void InPlaceWithSuffixKeepsIt()
    {
        Assert.Equal(".bak", SedOptions.Parse(["--in-place=.bak", "s/a/b/"]).InPlaceSuffix);
    }

    [Fact]
    public void LineLengthIsRead()
    {
        Assert.Equal(5, SedOptions.Parse(["--line-length=5", "l"]).LineWidth);
    }

    [Fact]
    public void UnknownLongOptionIsUnsupportedNotFatal()
    {
        SedOptions options = SedOptions.Parse(["--follow-symlinks", "p"]);
        Assert.Contains("--follow-symlinks", options.UnsupportedFlag);
    }

    [Fact]
    public void DoubleDashEndsOptions()
    {
        SedOptions options = SedOptions.Parse(["--", "-n"]);
        Assert.Equal("-n", options.InlineScript);
    }
}
```

Also cover `--separate`, `--posix`, `--unbuffered` (accepted, no effect), and the
missing-value error: `SedOptions.Parse(["--expression"])` must set `Error`, not throw. Read
`TakeValue` in `SedOptions.cs` first to assert the exact error text.

- [ ] **Step 2:** `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~SedOptionsTests"`
      — fix any wrong expectation against the real behaviour (the source is the oracle here;
      GNU sed is the tie-breaker if the behaviour looks wrong).
- [ ] **Step 3:** Full suite green, then propose commit
      `test: pin the sed long-option table`.

### Task 1.2: `R`, `l`, and replacement escapes through the harness

**Files:**
- Modify: `tests/Sharp.Shell.Tests/SedAppletTests.cs` (new theories)
- Modify: `tests/Sharp.Shell.Tests/SedScriptParserTests.cs` (unescape theories)

- [ ] **Step 1:** `R` exercises `SedFileSystem.ReadLine` (currently 0%):

```csharp
[Fact]
public void ReadLineInterleavesTheNamedFile()
{
    using ShellHarness harness = new();
    harness.Run("printf 'x\\ny\\n' > lines.txt");
    ShellResult result = harness.Run("printf 'a\\nb\\nc\\n' | sed 'R lines.txt'");
    Assert.Equal("a\nx\nb\ny\nc\n", result.Stdout);
}

[Fact]
public void ReadLineFromAMissingFileAddsNothing()
{
    using ShellHarness harness = new();
    Assert.Equal("a\n", harness.Run("printf 'a\\n' | sed 'R absent.txt'").Stdout);
}
```

      (If `ShellHarness.Run` cannot chain state between calls, write the fixture file with
      `File.WriteAllText` into the harness workspace instead — check how `GlobberTests` seeds
      files and copy that.)

- [ ] **Step 2:** `l` exercises `RenderCharacter` (41.7%): escapes, octal, fold, `$`:

```csharp
[Theory]
[InlineData("printf 'a\\tb\\n' | sed -n 'l'", "a\\tb$\n")]
[InlineData("printf 'a\\\\b\\n' | sed -n 'l'", "a\\\\b$\n")]
[InlineData("printf 'a\\rb\\n' | sed -n 'l'", "a\\rb$\n")]
[InlineData("printf 'a\\001b\\n' | sed -n 'l'", "a\\001b$\n")]
[InlineData("printf 'abcdefgh\\n' | sed -n 'l 4'", "abc\\\ndef\\\ngh$\n")]
public void ListRendersUnambiguously(string commandLine, string expected)
{
    Assert.Equal(expected, Out(commandLine));
}
```

      Verify each expectation against GNU sed before committing (`bash -c "<the line>"`); the
      fold arithmetic (`width - 1`) is exactly the kind of thing to get wrong by hand.

- [ ] **Step 3:** Replacement unescapes (`UnescapeCharacter`, 50%): theories in
      `SedScriptParserTests` for `s/a/\t/`, `s/a/\n/`, `s/a/\\\\/`, `s/a/\a/`, and an unknown
      escape like `s/a/\q/` (GNU keeps the literal `q`) — assert against GNU behaviour.
- [ ] **Step 4:** Full suite, then propose commit
      `test: cover sed R, l rendering and replacement escapes`.

---

## Phase 2: Regex translator tests

Status: Not started

Targets: `PosixRegexParser.ParseGnuEscape` (65.6%), `ReadBracketCharacter` (60%),
`RegexEmitter.EscapeLiteral` (70%).

### Task 2.1: GNU escape matrix

**Files:**
- Modify: `tests/Sharp.Shell.Tests/PosixRegexTranslatorTests.cs`

- [ ] **Step 1:** Read the existing test file to learn its assertion helper, then add one theory
      row per escape, driven end-to-end through grep so the emitter is covered too:

```csharp
[Theory]
[InlineData(@"printf 'a1 b\n' | grep -c '\w'", "1\n")]
[InlineData(@"printf 'a b\n' | grep -o '\s'", " \n")]
[InlineData(@"printf 'cat cathode\n' | grep -c '\bcat\b'", "1\n")]
[InlineData(@"printf 'cat\n' | grep -c '\<cat\>'", "1\n")]
[InlineData(@"printf 'a\tb\n' | grep -c 'a\tb'", "1\n")]
public void GnuEscapesMatchLikeGnuGrep(string commandLine, string expected)
{
    Assert.Equal(expected, Out(commandLine));
}
```

      Cover the full switch: `\W \S \B \` \' \a \f \v \cX \d65 \o101 \x41`, and the refusal
      path — under `sed --posix`, a `\w` must refuse with `'\w' is a GNU extension` (assert the
      classification/refusal, not a crash).

- [ ] **Step 2:** Bracket classes (`ReadBracketCharacter`): `[]]`, `[^]]`, `[a-]`, `[-a]`,
      `[a-c]`, `[[:alpha:]]`, `[\]]` — one theory row each through grep, expectations checked
      against GNU grep first.
- [ ] **Step 3:** Add the escapes that are valid in *both* GNU and BSD (`\w`, `\b`, `\<`, `\>`)
      as lines in the sed/grep differential corpora so the oracle keeps them honest on every
      machine. GNU-only forms stay in the unit theories.
- [ ] **Step 4:** Full suite, then propose commit
      `test: cover the GNU regex escapes and bracket-class edges`.

---

## Phase 3: Applet gaps — find and mv

Status: Not started

Targets: `FindApplet.FindOptions.From` (83.3%, 11 uncovered lines), `MvApplet` file (68%).

### Task 3.1: find option branches

**Files:**
- Modify: `tests/Sharp.Shell.Tests/SearchAppletTests.cs` (or the file that already tests find —
  locate with `grep -rn "find " tests/Sharp.Shell.Tests --include="*Tests.cs" -l`)

- [ ] **Step 1:** Open the Cobertura file's `FindApplet.cs` entries
      (`tests/Sharp.Shell.Tests/TestResults/coverage-analysis/raw/*/coverage.cobertura.xml`,
      `<class filename="Commands/FindApplet.cs">`, lines with `hits="0"`) and list the exact
      uncovered branches. Expect them to be the rarer predicates and the unsupported-flag arm.
- [ ] **Step 2:** One theory row per uncovered branch, plus the escalation case: an unsupported
      predicate such as `find . -newer x` must classify the line native, not half-run.
- [ ] **Step 3:** Full suite, propose commit `test: cover find's remaining option branches`.

### Task 3.2: mv paths

**Files:**
- Modify: `tests/Sharp.Shell.Tests/MutationAppletTests.cs`

- [ ] **Step 1:** Theories through the harness: usage error (`mv` alone → stderr
      `mv: usage: mv source... destination`, exit non-zero), missing source
      (`mv absent.txt out.txt` → `mv: absent.txt: No such file or directory`), rename,
      move-into-directory, several-sources-into-directory, overwrite of an existing target.
      Seed files the same way the existing mutation tests do.
- [ ] **Step 2:** Full suite, propose commit `test: cover mv's error and directory paths`.

---

## Phase 4: Public agent corpus — SWE-bench and Terminal-Bench trajectories

Status: Not started

The local mined corpus (`SessionCommandCorpus`) is honest but uncommittable and
machine-dependent. Public trajectories are fixed data: the owned-rate becomes a number the repo
can ratchet.

### Task 4.1: The miner

**Files:**
- Create: `build/mine-agent-corpus.py`
- Create: `tests/Sharp.Shell.Tests/corpus/agent-commands.txt`

- [ ] **Step 1:** Write the miner. Sources, both permissively licensed (MIT / Apache-2.0 —
      note the attribution in the corpus header):
      - SWE-bench: `github.com/SWE-bench/experiments`, trajectory JSON per submission — pull a
        handful of recent top submissions; commands live in the action/observation records
        (shape varies per harness; extract strings passed to bash-like actions).
      - Terminal-Bench: published run artifacts from `github.com/laude-institute/terminal-bench`
        (and its registry) — agent transcripts contain the executed commands.
      The script: extract → strip obvious secrets/paths (drop lines matching
      `token|secret|Authorization|/Users/|/home/`) → dedupe ordinally → drop non-hermetic lines
      (leading `apt|docker|curl|wget|pip install|npm install|git push`) → sort → write one per
      line under a `#` header naming source repos, commit SHAs, licence, and the mining date.
- [ ] **Step 2:** Run it; eyeball the output for anything secret-shaped before committing.
      Target a few thousand distinct lines; cap the file at ~500 KB.
- [ ] **Step 3:** Propose commit `test: vendor a public agent-command corpus mined from swe-bench and terminal-bench trajectories`
      (script + corpus in one commit so the provenance travels with the data).

### Task 4.2: Classification ratchet + differential

**Files:**
- Create: `tests/Sharp.Shell.Tests/AgentCorpusTests.cs`

**Interfaces:**
- Consumes: `CommandClassifier.Classify(string)` → `.Tier == ExecutionTier.Owned`, `.Mutates`;
  `BashOracle.Run(command, workingDirectory)`; corpus file copied to output like the existing
  `corpus/**` content items (already covered by the csproj glob).

- [ ] **Step 1:** Write the test, copying `MinedDifferentialTests`' safety filter exactly
      (only `Owned` **and** non-mutating lines execute):

```csharp
public class AgentCorpusTests(ITestOutputHelper output)
{
    private const double OwnedRateFloor = 0.0;

    [Fact]
    public void OwnedRateDoesNotRegress()
    {
        string[] commands = ReadCorpus();
        CommandClassifier classifier = new(AppletRegistry.CreateDefault());
        int owned = commands.Count(command => classifier.Classify(command).Tier == ExecutionTier.Owned);
        double rate = (double)owned / commands.Length;
        output.WriteLine($"owned {owned}/{commands.Length} = {rate:P1}");
        Assert.True(rate >= OwnedRateFloor, $"owned rate {rate:P1} fell below the ratchet {OwnedRateFloor:P1}");
    }

    private static string[] ReadCorpus()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "corpus", "agent-commands.txt");
        return
        [
            .. File.ReadAllLines(path)
                .Where(line => line.Length > 0 && !line.StartsWith('#')),
        ];
    }
}
```

      First run prints the real rate; set `OwnedRateFloor` to that measured value (rounded
      down a point) in the same change — the ratchet starts honest, not aspirational.
- [ ] **Step 2:** Add the differential half: owned + non-mutating lines run through both
      `ShellExecutor` and `BashOracle` (copy `MinedDifferentialTests.RunOurs` and its
      20-second deadline); report the agreement rate; ratchet it the same way. Non-hermetic
      leftovers that disagree for environmental reasons get filtered in the miner, not
      special-cased in the test.
- [ ] **Step 3:** Full suite (the corpus test must stay under ~30 s — sample the corpus with a
      fixed stride if it is slower), then propose commit
      `test: ratchet classification and bash agreement over the public agent corpus`.

---

## Phase 5: Complexity refactors

Status: Not started

All three targets are 100% covered — the tests are the net; behaviour must not change. CRAP
here is pure complexity: `AwkLexer.KeywordKind` (90), `GrepApplet.IsSupportedFlag` (70),
`TokenParser.TryReadRedirection` (46). `SedOptionReader.ReadLongOption` is explicitly *not* a
target: after phase 1 it is a covered, flat, table-like switch — the house style prefers that
over a cleverer shape.

### Task 5.1: KeywordKind and IsSupportedFlag become tables

**Files:**
- Modify: `src/Sharp.Shell/Commands/Awk/AwkLexer.cs:363`
- Modify: `src/Sharp.Shell/Commands/GrepApplet.cs:57`

- [ ] **Step 1:** `KeywordKind` — replace the 20-arm switch with a `FrozenDictionary`:

```csharp
private static readonly FrozenDictionary<string, AwkTokenKind> Keywords =
    new Dictionary<string, AwkTokenKind>(StringComparer.Ordinal)
    {
        ["BEGIN"] = AwkTokenKind.Begin,
        ["END"] = AwkTokenKind.End,
        ["function"] = AwkTokenKind.Function,
        ["if"] = AwkTokenKind.If,
        ["else"] = AwkTokenKind.Else,
        ["while"] = AwkTokenKind.While,
        ["for"] = AwkTokenKind.For,
        ["do"] = AwkTokenKind.Do,
        ["break"] = AwkTokenKind.Break,
        ["continue"] = AwkTokenKind.Continue,
        ["next"] = AwkTokenKind.Next,
        ["nextfile"] = AwkTokenKind.NextFile,
        ["exit"] = AwkTokenKind.Exit,
        ["return"] = AwkTokenKind.Return,
        ["delete"] = AwkTokenKind.Delete,
        ["in"] = AwkTokenKind.In,
        ["print"] = AwkTokenKind.Print,
        ["printf"] = AwkTokenKind.Printf,
        ["getline"] = AwkTokenKind.Getline,
    }.ToFrozenDictionary(StringComparer.Ordinal);

private static AwkTokenKind? KeywordKind(string word) =>
    Keywords.TryGetValue(word, out AwkTokenKind kind) ? kind : null;
```

- [ ] **Step 2:** `IsSupportedFlag` — same move: a `FrozenSet<string>` of the exact flags plus
      the two `StartsWith` prefix checks kept as-is.
- [ ] **Step 3:** `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Awk"` then
      the full suite — zero behavioural diff expected; the awk differential suite is the proof.
- [ ] **Step 4:** Propose commit `refactor: table the awk keywords and grep flag set`.

### Task 5.2: TryReadRedirection split

**Files:**
- Modify: `src/Sharp.Shell/Parsing/Parser.cs` (`TryReadRedirection`, 36 lines, complexity 46)

- [ ] **Step 1:** Read the method. Expected extraction: the fd-prefix parse (`2>`, `&>`,
      `2>&1`) and the operator-kind mapping become two private helpers with the guard clauses
      the house early-return rule wants; `TryReadRedirection` keeps the orchestration only. Do
      not change any token consumption order — the oils corpus and `RedirectionTests` are the
      net, and `ExpectedFailures.Load()` must not change by a single case.
- [ ] **Step 2:** Full suite; diff `expected-failures.txt` expectations — untouched.
- [ ] **Step 3:** Propose commit `refactor: split the redirection parse into guarded helpers`.

---

## Phase 6: In-process coverage for the sharp binary

Status: Not started

`src/Sharp` (`Program.cs`, `Session.cs`, `NativeTier.cs`) is exercised only black-box through a
spawned process, which no collector can see. The black-box contract tests stay — they test the
process seam — but the session logic gains in-process tests so its coverage is real.

### Task 6.1: Reference and test Session in-process

**Files:**
- Modify: `src/Sharp/Sharp.csproj` (add `<InternalsVisibleTo Include="Sharp.Shell.Tests" />`)
- Modify: `tests/Sharp.Shell.Tests/Sharp.Shell.Tests.csproj` (add
  `<ProjectReference Include="../../src/Sharp/Sharp.csproj" />`)
- Create: `tests/Sharp.Shell.Tests/SharpSessionTests.cs`

- [ ] **Step 1:** Read `src/Sharp/Session.cs` and `Program.cs` to find the seam: the loop that
      takes a line, classifies, runs or refuses, and writes to a `TextWriter`. If the seam
      currently writes to `Console` directly, first extract writers into `Session`'s
      constructor (smallest possible change; the binary passes `Console.Out`/`Console.Error`).
- [ ] **Step 2:** Tests, one behaviour each, through the in-process seam: a line that runs
      owned; `--explain` output for `[owned]`, `[native git]`, `[owned mutates]`; `--strict`
      refusal (exit 127, reason on stderr); `--root` refusing a path above the root; state
      persisting across lines (`cd` sticks). Assert against the exact strings the README
      documents — the README is the contract.
- [ ] **Step 3:** Full suite; then re-run the coverage command (phase 0 left
      coverlet.collector in place) and confirm `Sharp` now appears in the Cobertura output
      with the session logic covered.
- [ ] **Step 4:** Propose commit `test: drive the sharp session in-process`.

---

## Verification (whole plan)

- [ ] `dotnet test tests/Sharp.Shell.Tests` — everything green.
- [ ] Re-run coverage:
      `dotnet test tests/Sharp.Shell.Tests --collect:"XPlat Code Coverage" --results-directory tests/Sharp.Shell.Tests/TestResults/coverage-analysis/raw -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura`
- [ ] Success criteria against the baseline (93.1% line / 88.4% branch, 31 methods CRAP > 30):
      `ReadLongOption` ≥ 90% covered, `SedFileSystem.ReadLine` > 0%, `ParseGnuEscape` ≥ 90%,
      flagged-method count ≤ 20, `KeywordKind`/`IsSupportedFlag` complexity collapsed to ~1–3,
      `Sharp` assembly present in the report.
- [ ] `AgentCorpusTests` ratchets committed with measured (not aspirational) floors.
