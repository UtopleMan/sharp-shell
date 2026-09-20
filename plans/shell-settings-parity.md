# Shell settings: environment, identity, options and rc files

Give the shell the settings surface a real shell has — a variable model that distinguishes exported
from local, an environment that reaches child processes, the parameters zsh honours, an options
table, and rc files — and put each piece on the correct side of the line between `Sharp.Shell`, the
core that compiles into duetui's wasm container, and `shsh`, the binary that has to behave like a
shell.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Where this came from.** An investigation comparing `shsh` against the zsh on this machine found
that the gap is not options — it is that **`shsh` has no environment at all**:

```
$ shsh -c 'echo "HOME=[$HOME] PATH=[$PATH] USER=[$USER] PWD=[$PWD]"'
HOME=[] PATH=[] USER=[] PWD=[]

$ shsh -c '/usr/bin/env | grep -c .'
90                                  # children see 90 vars; the shell itself sees none

$ shsh -c 'export FOO=bar; echo "expand=[$FOO]"; printenv FOO || echo "child: FOO unset"'
expand=[bar]
child: FOO unset                    # export does not export
```

`ExportApplet`'s comment calls this honest — *"the guest starts with nothing and a variable is a
variable"*. That is true of the wasm guest as it is wired today and **false of `shsh`**, which
starts real processes through `NativeTier`.

**Verified against duetui's WASI setup rather than assumed.** The guest can have a real environment,
and the plumbing already exists end to end:

| Evidence | Where |
|---|---|
| Full WASI P2 is linked, so `wasi:cli/environment` is available to the guest | `ComponentLinker.AddWasi` → `wasmtime_component_linker_add_wasip2` |
| The host can set an explicit guest environment | `WasmStore.ApplyEnvironment` → `wasi_config_set_env`, fed by `WasiOptions.Environment` |
| The builtin toolset **already passes one** | `BuiltinToolsetComposition.RuntimeFor`: `Environment = new Dictionary<string, string> { ["PWD"] = workspace.RootPath }` |

So duetui already tells the guest where it is — and **`Sharp.Shell` ignores it**, because
`ShellState.Variables` starts empty and nothing reads the environment it was given. That is a live
bug, not a hypothetical: the host supplies `PWD` and `$PWD` expands to nothing.

Note also that `wasi_config_inherit_env` is declared in duetui's interop and **deliberately never
called on any path** — the guest receives an explicit environment chosen by the host, never the
host's own. That stance is respected here: nothing in this plan makes the guest inherit anything.

## The split, and the rule that decides it

The user asked for this boundary to be explicit. One rule settles almost every case:

> **`Sharp.Shell` owns anything that changes what a command line *means*.**
> **`shsh` owns anything that needs a terminal, an operating-system environment, or a home directory.**

The core must keep compiling into a trimmed NativeAOT WASI-P2 guest: zero package references, no
`System.Diagnostics.Process`, no threads, no reflection. It may not read `~`, may not enumerate the
OS environment, and may not assume a terminal exists.

| Item | Home | Why |
|---|---|---|
| Variable model, exported flag | **core** | Changes what `$FOO` and `export` mean |
| `$HOME` `$PATH` `$USER` … as values | **core** | Storage is storage; the guest can have them too |
| *Seeding* those values | **each host** | `shsh` from the OS environment; duetui's guest from what the host passed it through `WasiOptions.Environment` |
| Exported subset reaching a child | **core** interface, **shsh** implementation | The shell decides *what* to hand over; the host decides what a child actually gets |
| `~` → `$HOME` | **core** | Word expansion |
| `$PWD` `$OLDPWD` `cd -` `CDPATH` | **core** | `cd` and expansion |
| `IFS` | **core** | Field splitting |
| Shell identity (`$0`, message prefix) | **core** holds it, **each host** supplies it | The guest is not called `shsh` and never was |
| Options table, `set` / `shopt` / `setopt` | **core** | Options change execution semantics |
| `alias` `unalias` `source` `unset` | **core** | Builtins that change dispatch and parsing |
| `pushd` `popd` `dirs`, `autocd` | **core** | Directory state and dispatch |
| Finding and loading `~/.shshenv`, `~/.shshrc` | **shsh** | A home directory and a start-up sequence |
| `PS1` / `PS2` rendering | **shsh** | There is no prompt without a terminal |
| `HISTFILE` `HISTSIZE` `SAVEHIST` | **core** stores, **shsh** obeys | Handed to the line-editor plan |

The two edges worth stating, because they look like exceptions and are not:

- **rc *loading* is `shsh`; the `source` builtin that executes a file is core.** Finding `~/.shshrc`
  needs a home directory; running a file of shell commands does not.
- **The options *table* is core; which options the interactive editor reads is `shsh`.** A flag is
  a value either way; only the terminal behaviour it drives is binary-side.

## Decisions already taken

Settled with the user before the plan was written. Do not relitigate; if one proves wrong, say so
and record the change here.

| | |
|---|---|
| Variable model | One dictionary, each variable carrying an **exported** flag. What bash and zsh do. |
| Shell identity | **Host-supplied**. `shsh` reports `shsh`; duetui's guest reports what it chooses. |
| Options | **Full machinery**, not named behaviours — an rc file needs something to switch. |
| Option spelling | **Both.** `set -o` and `shopt` because the language is bash; `setopt`/`unsetopt` as the zsh spelling of the same table. |
| rc files | **Two.** `~/.shshenv` read always, `~/.shshrc` interactive only. |
| Seeding | `shsh` inherits the **real process environment**. `--root` confines the filesystem, not the environment. |
| `ICommandExecutor` | **Clean break**, released as **v0.3.0**. No defaulted overload. |

## Assumptions

- `$0` defaults to the identity the host supplies; there is no fallback to `duetui-shell`. A host
  that supplies nothing gets a documented default, and the default is not a third product's name.
- Exported-ness survives `ShellState.Fork()`, like the variables themselves.
- `export` with no operands lists exported variables, as `export -p` does. `unset` removes a
  variable entirely rather than un-exporting it; `export -n` un-exports.
- Options are per-`ShellState` and survive a fork the way variables do. **Wrong for one option, and
  corrected in Phase 5: errexit must *not* survive a fork.** bash does not pass it into a `$( )` —
  that is what its `inherit_errexit` turns back on, and it is off by default — and four oils cases pin
  it.
- `set -o errexit` / `nounset` / `pipefail` change execution and therefore live in the executor's
  existing unwind machinery, beside `ExitRequested` and `RefusalRequested`. **Held, and the
  restructuring found a live bug: the unwind was never reset, so a session that refused one command
  was silently dead afterwards.** See Phase 5.
- `~user` (another user's home) stays unsupported. `~` and `~/path` are the cases that matter and
  the only ones the core can answer without an OS user database.
- Neither rc file is read for `-c` or for a script — `~/.shshenv` is read for those too, which is
  exactly the zsh split and the reason there are two files.

## Non-goals

- **`eval`.** Not needed by anything here. It is a sibling of `source` in appearance only, and it
  can arrive with the first test that needs it.
- **`~user`**, job-control options (`longlistjobs`), and completion options (`alwaystoend`,
  `completeinword`) — the first needs a user database, the others need features this shell does not
  have.
- **Persisting history.** `HISTFILE`/`HISTSIZE`/`SAVEHIST` become readable values here; obeying them
  belongs to the line-editor plan, which is not yet written.
- **An environment read-hook.** A host callback per `$VAR` would put a round trip inside word
  expansion — the hottest path in the shell — and a loop expanding `$i` ten thousand times would
  fire ten thousand callbacks. The file-operand seam was deferred for the same reason.
- **oh-my-zsh compatibility.** The rc files run *this* shell's language. A `.zshrc` is not a
  `.shshrc` and this plan does not pretend otherwise.

## Risks

**This breaks `ICommandExecutor` for every consumer.** `Execute` and `ExecuteLine` both gain the
environment, by decision without a compatibility overload, so duetui cannot take any part of this
release without also implementing it. Phase 3 is the release boundary; everything after it is
additive.

**`errexit` interacts with the refusal unwind.** `set -e` and a denied command both stop a run, and
the executor now has two unwind reasons (`ExitRequested`, `RefusalRequested`). A third must not turn
that into a tangle of booleans — see Phase 5.

**Seeding the real environment changes what the corpus sees.** The conformance and differential
suites run through `ShellHarness` and `SharpBinary`; if `shsh` suddenly has 90 variables, cases that
expand `$HOME` or `$PATH` change behaviour and the ratchet will move. Phase 3 must check the ratchet
deliberately rather than regenerating it reflexively.

## Phase 1: A variable knows whether it is exported
Status: Complete

Core. No behaviour reaches a child process yet — that is Phase 3. This phase is the model.

- [x] Replace `ShellState.Variables`'s `Dictionary<string, string>` with a type that carries an
      exported flag per name, keeping an indexer so the hundreds of existing `Variables[name]`
      call sites compile unchanged.
- [x] Expose `ExportedVariables` — the marked subset, which is what a child process will receive.
- [x] `ShellState.Fork()` copies flags as well as values.
- [x] `ExportApplet`: `export FOO=bar` assigns and marks; `export FOO` marks an existing variable;
      `export` with no operands lists the marked ones as `export NAME=value`; `export -n FOO`
      un-marks without deleting.
- [x] New `UnsetApplet` (`unset`), removing a variable entirely. Registered in `AppletRegistry`.
- [x] Assigning to an already-exported name keeps it exported — the case a two-dictionary model
      gets wrong, asserted explicitly.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Variable|FullyQualifiedName~Export|FullyQualifiedName~Unset"`
  — green, covering: a plain assignment is not exported; `export` marks; re-assigning an exported
  name keeps the mark; `export -n` un-marks but keeps the value; `unset` removes both; a fork sees
  the same marks; `export` with no operands lists only marked names.
- `dotnet test tests/Sharp.Shell.Tests` — whole suite green. Nothing observable has changed yet, so
  a moved ratchet here means the model leaked.

### Phase Summary

`ShellVariables` (`src/Sharp.Shell/Execution/ShellVariables.cs`) replaces the plain dictionary. It
implements `IReadOnlyDictionary<string, string>`, so every value-only reader — parameter expansion,
the arithmetic evaluator, awk's `ENVIRON`, `read` — compiled unchanged; there were 13 such call
sites, not the hundreds this plan guessed. The flag rides on the entry, so the indexer's setter
carries the existing mark forward and `export PATH=x; PATH=y` stays exported. `Fork` delegates to
`ShellVariables.CopyTo`, which copies entries whole, so a mark cannot be forgotten at one call site.

`export FOO` on a name the shell has never seen creates it empty and marked rather than dropping the
mark. bash has a third state for this — exported but unset, absent from a child's environment until
assigned — and this model has two. Creating it empty is the divergence that keeps
`export FOO; FOO=bar` reaching the child, which is the case that matters.

**The plan's prediction about the ratchet was wrong, and the ratchet moved for a good reason.** This
phase *is* observable: `unset` did not exist and `export -n` was rejected as an unknown flag. Nine
oils cases went from failing to passing — seven in `builtin-vars.test.sh` (unset, unset's exit
status, unset of an unknown name, dynamic scope, `export -n` on an undefined name, `export -n
foo=bar`) and two in `var-op-test.test.sh` (`${v-foo}` and `${s+foo}` under `set -u`, both of which
use `unset` to set up). Each diff was read before the line was deleted; the failures file is now
2221 of 2787, down from 2230, and its header records the step.

Whole suite: 1809 passed, 0 failed.

## Phase 2: The shell knows its own name
Status: Complete

Core holds it, each host supplies it. Today `$0` answers `duetui-shell` and every message reads
`duetui-shell: …`, which matches neither this repository, nor the binary, nor the host.

- [x] A shell identity value on `ShellState` (or the options record it is constructed from),
      supplied by the embedder.
- [x] `ParameterExpander.TryExpandSpecial` answers `$0` from it.
- [x] Every `duetui-shell: ` message prefix in `ShellExecutor` and the applets comes from it.
- [x] `shsh` supplies `shsh`; `ShellHarness` supplies a fixed name so test expectations are stable.
- [x] `docs/host-integration.md` documents the identity as a host responsibility, and its effect on
      `$0` and on every message a user sees. **This is a documented contract change** — the current
      text pins the `duetui-shell:` prefix.
- [x] Sweep the test suite for hardcoded `duetui-shell` expectations.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests` — green.
- `./src/Sharp/bin/Debug/net10.0/shsh -c 'echo $0'` — prints `shsh`.
- `./src/Sharp/bin/Debug/net10.0/shsh --strict -c 'git status'` — the refusal reads `shsh: …`.
- `grep -rn "duetui-shell" src tests docs README.md` — only in prose describing history, never as a
  literal a test or a message depends on.

### Phase Summary

`ShellState` gained `ShellName`, a third constructor argument, defaulting to `sharp-shell` — this
library's own name, so a host that supplies nothing is not told it is duetui. `Fork` carries it.
`$0` reads it. Every message the shell writes about itself goes through one new place,
`ShellState.Message(text)`, which is the only thing that knows the prefix shape.

There were no prefixes in the applets: all ten sat in `ShellExecutor`. Three of them were in `static`
helpers with no `ShellState` in scope — `Unsupported`, `Report`, `MatchesAnyPattern` — so those
helpers now take the state. `TryExpandWords` takes it to reach `Report`.

`ShellHarness` supplies `test-shell` rather than letting the default answer, deliberately: a test
asserting `sharp-shell:` would still pass if the identity plumbing broke and fell back. `Session`
supplies `shsh` from a single `Session.SHELL_NAME` constant that `Program` now shares, so the
binary's name has one home instead of four literals.

`docs/host-integration.md` gained a section on the name as a host responsibility, carrying the
v0.3.0 contract-change note; `README.md` no longer pins the old prefix. The 35 `duetui-shell:`
strings in `expected-failures.txt` were in the informational detail column, which the ratchet does
not compare — rewritten to `sharp-shell:` so the file does not read as stale. Three test temp-dir
prefixes were renamed for the same reason.

Verified through the binary: `shsh -c 'echo $0'` prints `shsh`, and
`shsh --strict -c 'git status'` reports `shsh: 'git' is not one of the sandboxed commands` with exit
126. Whole suite: 1815 passed, 0 failed; the conformance ratchet did not move.

## Phase 3: The environment reaches child processes — v0.3.0
Status: Complete

The release boundary. Core defines what is handed over; each host decides what a child receives.

- [x] `ICommandExecutor.Execute` and `ExecuteLine` both take the exported variables. **A clean
      break — no defaulted overload.**
- [x] `ShellExecutor` passes `state.ExportedVariables` at both call sites.
- [x] `NativeTier` applies them to `ProcessStartInfo.Environment`, so `export FOO=bar; printenv FOO`
      prints `bar`.
- [x] `ShellState` accepts an environment at construction, every entry marked exported. The core
      **reads nothing from the OS itself** — it is given values, which is what keeps it compiling
      into the guest.
- [x] `shsh` seeds it from `Environment.GetEnvironmentVariables()`, every inherited variable marked
      exported. `--root` does not change this: it confines the filesystem.
- [x] Record for the duetui side: no new host plumbing is required. `WasiOptions.Environment`
      already reaches the guest and already carries `PWD`; what is missing is the guest wrapper
      passing its environment into `ShellState`. That is duetui-side code, outside `Sharp.Shell`'s
      compile constraints, so it may call `Environment.GetEnvironmentVariables()` freely.
- [x] Correct `ExportApplet`'s comment. The guest can have an environment — `wasi:cli/environment`
      exists — and what was described as honest was a description of the wiring.
- [x] Update `ProcessCommandExecutor`, `CorpusCommandExecutor`, `StubCommandExecutor`,
      `RecordingCommandExecutor`, `StreamingCommandExecutor` for the new signature.
- [x] `docs/host-integration.md`: the environment is part of the request, and filtering it is the
      host's job — the natural place, since the host already implements `Execute`.
- [x] `README.md`: `Sharp.Shell` compile constraints are unchanged, and the environment is data the
      host supplies rather than something the core reads.

### Verification Plan
- `./src/Sharp/bin/Debug/net10.0/shsh -c 'export FOO=bar; printenv FOO'` — prints `bar`.
- `./src/Sharp/bin/Debug/net10.0/shsh -c 'BAZ=local; printenv BAZ || echo absent'` — prints
  `absent`: an unexported variable must not reach the child.
- `./src/Sharp/bin/Debug/net10.0/shsh -c 'echo "[$HOME]"'` — the real home directory.
- A core test constructing `ShellState` with an explicit environment — no OS involved — proves the
  guest path: `$PWD` expands to the value supplied, which is the case duetui already feeds and the
  shell currently drops.
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~ExternalExecutor|FullyQualifiedName~Environment"`
  — green.
- `dotnet test tests/Sharp.Shell.Tests` — whole suite green. **If the conformance ratchet moves,
  read the diff before regenerating**: a seeded environment changes cases that expand `$HOME` or
  `$PATH`, and that is a deliberate change to record, not a number to reset.

### Phase Summary

Both `ICommandExecutor` methods now take `IReadOnlyDictionary<string, string> environment` after
`workingDirectory`. No overload, no default — an executor written against v0.2 does not compile. Five
test doubles and `NativeTier` were updated; `ExecuteLine` keeps its defaulted body, which is a
default *implementation*, not a compatibility overload.

`NativeTier` clears `ProcessStartInfo.Environment` before applying what it was handed. Without the
clear, a child would see the union of `shsh`'s own start-up environment and the shell's exported set,
so `unset PATH` would be invisible to it.

`ShellState` gained a fourth constructor argument, and `ShellVariables.SeededWith` marks every entry
exported on the way in. The core reads nothing from the operating system; `Session.InheritedEnvironment`
does the `Environment.GetEnvironmentVariables()` walk on the binary side, where `--root` still confines
only the filesystem.

**Two things the plan did not list, both consequences of it rather than additions.** `AwkApplet`
carried the same false comment as `ExportApplet` and fed `ENVIRON` from the whole variable table;
`ENVIRON` is the environment a child would receive, so it is now `state.ExportedVariables`, with a
test for an unexported variable being invisible to it. And `SpecHelpers.printenv.py` — the corpus
helper — read the *test process's* environment through `Environment.GetEnvironmentVariable`; it now
reads the environment it was handed, which is the only way the corpus's export cases can mean
anything.

**The ratchet: 2221 → 2215, seven new passes and one new failure.** The seven are the corpus's export
and unset cases, now that `printenv.py` sees what the shell actually exports — including "Export a
variable before defining it", which is precisely the case Phase 1's create-empty-and-mark choice was
made for, and "Unset exported variable, then define it again. It's NOT still exported."

The one regression is `builtin-vars.test.sh:14`, "can't export associative array (strict_array)". It
used to pass for the wrong reason: `export a` did nothing and `printenv.py a` read the test runner's
environment, printing bash's expected `None`. This shell has no associative arrays, so `typeset -A a`
and `a["foo"]=bar` are both command-not-found; `export a` then marks an unseen name, which Phase 1
decided creates it empty, and the child sees `a=` where bash's exported-but-unset `a` is absent.
Matching bash here needs a third variable state — exported, with no value — which the settled
one-flag model does not have. One array case is not enough evidence to reopen that decision, so it is
listed as an expected failure and recorded here. If `set -u` or `${a-foo}` meets the same seam in
Phase 5, revisit it then.

Verified through the binary: `export FOO=bar; printenv FOO` prints `bar`; `BAZ=local; printenv BAZ ||
echo absent` prints `absent`; `echo "[$HOME]"` prints the real home; `/usr/bin/env | grep -c .` still
reports 90. Whole suite: 1825 passed, 0 failed.

## Phase 4: The parameters a shell is expected to honour
Status: Complete

Core, and entirely internal — no host surface at all.

- [x] `~` expands to `$HOME`, falling back to the root when `$HOME` is unset. Today
      `shsh --root /tmp -c 'echo ~'` prints `/tmp`, which is plausible and wrong.
- [x] `$PWD` tracks `WorkingDirectory`; `$OLDPWD` records the previous one.
- [x] `cd -` returns to `$OLDPWD` and prints the new directory, as bash does. Today it fails with
      `cd: -: No such file or directory`.
- [x] `cd` honours `CDPATH` for a relative target that is not `.` or `..`.
- [x] `IFS` drives field splitting. `WordExpander.FieldSeparators` is a hardcoded `" \t\n"`; it
      becomes the value of `IFS`, with the default preserved when unset. An empty `IFS` disables
      splitting, which is the property `word-split.test.sh` in the corpus exercises.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Expansion|FullyQualifiedName~Cd|FullyQualifiedName~FieldSplit"`
  — green.
- Differential against real bash, added to `LanguageCorpus`: `IFS=: ; x=a:b:c; for i in $x; do echo
  $i; done`; `IFS= ; x="a b"; for i in $x; do echo "[$i]"; done`; `cd /tmp && cd - && pwd`;
  `echo ~`; `echo ~/notes`.
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Conformance"` — the ratchet is
  expected to **improve**; record by how much.

### Phase Summary

`~` reads `$HOME` and falls back to the root only when it is unset or empty, so
`shsh --root /tmp -c 'echo ~'` now prints the real home rather than `/tmp`.

`ShellState.TryChangeDirectory` sets `PWD` and `OLDPWD` on every successful change. It does **not**
invent `PWD` at construction: the indexer keeps whatever export mark a name already had, so a
host-supplied `PWD` stays exported and one the shell sets for itself is a plain variable. Inventing it
would have put `PWD` in every test's `export` listing and in every child's environment on a host that
never asked for it, and "the core invents nothing" is the rule the rest of this plan rests on.

`cd` gained three behaviours, each pinned against the real bash by direct probe before it was written:
`cd -` returns to `$OLDPWD` and prints where it landed, erroring with `cd: OLDPWD not set` when there
is nowhere to return to; `CDPATH` is searched **before** the working directory, not after — the
opposite of what the first draft of the test assumed — and the directory it lands in is announced,
because it is not the one that was typed; `.`, `..`, `./x`, `../x` and absolute paths skip the search.

`IFS` replaces the hardcoded separators, and the two kinds had to be separated to get either right:
IFS whitespace collapses into one boundary and is ignored at both ends, while a non-whitespace
separator is a boundary every time it appears, so `a::b` yields an empty middle field. That lives in
one place, `WordExpander.FieldSplitter`, because the fragment model lets a field span quoted and
unquoted pieces and the run state has to cross them. An empty `IFS` disables splitting.

Ten differential cases were added to `LanguageCorpus` and **all ten matched real bash on the first
run**, including the two the author was unsure of (`IFS=:; x=:;` and a mixed whitespace/non-whitespace
IFS). The cassette was re-recorded and verified under `SHARP_ORACLE_REPLAY=1`.

**Two of the plan's differential cases could not go in, and why.** `cd /tmp && cd - && pwd` and a bare
`echo ~` both print absolute paths, and `DifferentialTests.Compare` gives bash and this shell *two
different temp roots* — so any case printing one can never match, whatever the shell does. They are
covered by unit tests instead, against behaviour probed from the real bash by hand, and the corpus got
`HOME=/home/me; echo ~` and `HOME=/home/me; echo ~/notes`, which are path-free and assert the same
thing.

**The ratchet improved by 20: 2215 → 2195.** All 20 are tilde and word-splitting cases —
`word-split.test.sh` 1, 11–15, 20, 26–28, 41, 45, 53, plus tilde expansion in `[[`, in a `for` list,
after a glob, and in brace expansion. No case regressed. Whole suite: 1841 passed, 0 failed.

## Phase 5: The options table
Status: Complete

Core. One table, three spellings, because the language is bash and the parity target is zsh.

- [x] An options record on `ShellState`, surviving a fork.
- [x] `set -o name` / `set +o name`, and `set -o` with no operand listing current state.
- [x] `shopt -s` / `-u` / `-q`, and bare `shopt` listing.
- [x] `setopt` / `unsetopt` as the zsh spelling of the same table, so one flag has one home and two
      names reach it. Named in the table, so `setopt` and `shopt -s` cannot drift.
- [x] Execution options: `errexit`, `nounset`, `pipefail`. These change what a line does and belong
      beside the existing unwind reasons.
- [x] **`errexit` must not become a third unwind boolean.** `ShellState` already has
      `ExitRequested` and `RefusalRequested` behind `IsUnwinding`; a failing command under `errexit`
      is a third *reason* for the same unwind, and it joins that mechanism rather than sitting
      beside it.
- [x] Dispatch option: `autocd` — a command that names a directory changes to it. Implemented at
      the dispatch point so it passes `ICommandApprover` like anything else.
- [x] Interactive options stored but not acted on here: `histignoredups`, `histignorespace`,
      `noflowcontrol`. They are read by `shsh`, and `noflowcontrol` in particular belongs to the
      line-editor plan. Storing them now means that plan has somewhere to read from.
- [x] An unknown option name is an error, not a silent no-op.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Options|FullyQualifiedName~Errexit"`
  — green, covering: all three spellings set the same flag; `set -o` lists; unknown names error;
  options survive a fork; `set -e` stops a sequence at the first failure and a `&&` left side is
  exempt; `set -u` fails on an unset variable; `pipefail` reports the first non-zero stage;
  `autocd` changes directory and is seen by the approver.
- Differential against real bash for `errexit`, `nounset` and `pipefail`, added to `LanguageCorpus`.
- `dotnet test tests/Sharp.Shell.Tests` — whole suite green.

### Phase Summary

`ShellOptions` is one dictionary of seven names with typed accessors for the four the executor reads.
Three builtins reach it — `SetApplet`, `ShoptApplet`, and `SetoptApplet` registered twice as `setopt`
and `unsetopt` — and they share `OptionListing`, so the wording and the error message have one home.
bash pads a listing's name to fifteen characters and follows it with a tab; that was read off the real
thing with `od -c` rather than guessed.

`set` implements `-o`/`+o` with a name, `-o`/`+o` alone (a table, and the commands that would restore
it), and `-e`/`-u` as the short spellings. Everything else is refused **by name**: `set --`, `set -x`,
`set +C`. Bare `set` reports that listing variables is not supported rather than printing nothing.

**The unwind became one mechanism with three reasons, and that uncovered a live bug.** `ShellState`
now holds a single `UnwindReason` behind the existing predicates. Adding the third reason meant
asking when the unwind is cleared — and the answer was *never*: a session that refused one command
was silently dead afterwards, every later line expanding its words, seeing the refusal still set and
running nothing. `printf 'git status\necho one; echo two\n' | shsh --strict` printed the refusal and
then nothing at all. `BeginRun()` now clears it at the top of each run, with a test.

Four errexit exemptions, each probed from the real bash before it was written: an `if`/`while`/`until`
condition, the left side of `&&` or `||`, and everything inside a `!`-negated pipeline including its
inverted answer. They share one counter, `testedDepth`, incremented by `Tested(...)` — a command whose
failure is being *examined* is not a command errexit is about.

**Three corrections to the plan, all found by running it.**

1. **`pipefail` reports the rightmost failing stage, not the first.** bash:
   `(exit 3) | (exit 4) | true` is 4 and `(exit 3) | true | true` is 3. An upstream stage's status is
   only final once the last stage has drained it, so `RunStages` keeps each stage and reads the
   statuses afterwards.
2. **Options do *not* all survive a fork: errexit must not.** The plan's assumption says they survive
   like variables. bash deliberately does not pass errexit into a `$( )` — that is what its
   `inherit_errexit` option turns back on, and it is off by default. Four oils cases pin it, starting
   with `set -e; echo $(echo one; false; echo two)` printing `one two` and carrying on. `CopyToSubshell`
   copies everything else and clears errexit.
3. **`nounset` exits 127, not 1.** That is what bash reports for an unbound variable, verified rather
   than assumed. It is a *fatal* expansion — `ExpansionResult.IsFatal` — so it stops the run instead of
   failing one command, and the `${x-word}` forms are deliberately exempt because asking whether a
   variable is set is the one thing nounset must not break.

**One thing the plan did not list and the suite demanded.** `SharpCorpusTests.AScriptFileReadsTheSameAsOneCommandString`
failed on six cases: `set -e` stopped a `-c` body but not a script file, because the unwind ends a run
and a script is read a line at a time. bash ends the whole shell — an interactive one too, verified by
piping into `bash -i` — so `Session.WantsExit` now includes `StoppedOnFailure`.

`autocd` sits at the dispatch point and re-enters it as `cd <name>`, so the approver is asked about the
`cd` that runs rather than the word that was typed, and only when the name has no arguments and is not
owned.

Fourteen differential cases were added and **all fourteen matched real bash on the first run**. The
ratchet improved by 9: 2195 → 2186, with 11 cases newly passing (`errexit.test.sh` 2, 3, 8, 18, 21,
22, 23; `failglob` under `set -e`; `shopt -q invalid`; `strict_arith`) and two newly failing, both
recorded with their reason: `errexit-osh.test.sh:28` needs `local`, and `sh-options.test.sh:18` needs
`noclobber`. Both had been passing only because `set -o errexit` used to be a command not found.
Whole suite: 1874 passed, 0 failed; cassette re-recorded and verified under replay.

## Phase 6: alias, source, and the directory stack
Status: Complete

Core builtins. `source` is what makes Phase 7 possible: `shsh` finds the rc file, the core runs it.

- [x] `AliasApplet` (`alias`, `unalias`) storing aliases on `ShellState`, surviving a fork.
- [x] Alias expansion at the command position only, once per name, with the recursion guard bash
      uses — an alias whose body names itself must resolve to the command, not loop.
- [x] A command produced by an alias is dispatched normally, so **`ICommandApprover` sees the
      expanded command**, not the alias. The host must not be asked about `ll` when `ls -la` runs.
- [x] `SourceApplet` (`source` and `.`) reading a file and executing it against the *current* state,
      so a `cd` or an `export` inside it persists — that is the whole point.
- [x] A file `source` cannot read is an error with its reason, not a silent success.
- [x] `pushd`, `popd`, `dirs` over a directory stack on `ShellState`, with `autopushd` making `cd`
      push.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Alias|FullyQualifiedName~Source|FullyQualifiedName~DirectoryStack"`
  — green, covering: an alias expands at the command position and not as an argument; a
  self-referential alias terminates; the approver is asked about the expanded command; a sourced
  file's `cd` and `export` persist; a missing file errors; `pushd`/`popd` round-trip.
- Differential against real bash for the directory stack and for alias expansion.
- `dotnet test tests/Sharp.Shell.Tests` — whole suite green.

### Phase Summary

Aliases and the directory stack live on `ShellState` beside the variables and fork with them.
`AliasApplet` is registered twice, as `alias` and `unalias`; `DirectoryStackApplet` three times, as
`pushd`, `popd` and `dirs`; `SourceApplet` twice, as `source` and `.`.

Alias expansion happens in `Invoke`, **before** the words are expanded, so only a fully literal first
word can name an alias — `x=ll; $x` runs `ll`, as it does in bash — and the alias body is lexed and
spliced in front of the remaining words. Each name expands at most once, which is bash's guard:
`alias ls='ls f.txt'` reaches the command instead of looping. Because the splice happens before
dispatch, the approver is asked about `echo hello` and never about `hi`.

**`source` needed a seam, and the seam is `AppletContext.RunShellText`.** An applet cannot reach the
executor — the registry is built before the executor exists — so the executor supplies the runner when
it builds the context. It deliberately does *not* begin a new run: `Execute` now calls `BeginRun()` and
delegates to a new `RunText`, and `source` gets `RunText`, so an `exit` inside a sourced file ends the
line that sourced it. Four tests that invoke an applet directly went through a new
`Support/AppletContexts.For` helper whose runner throws — an applet other than `source` reaching for the
executor is a bug, and the double says so.

`pushd`/`popd`/`dirs` were probed from the real bash first: the working directory is the stack's first
entry, both `pushd` and `popd` print the stack they leave behind, the home directory prints as `~`, an
empty stack makes `popd` report `directory stack empty`, and — the one the corpus caught — `dirs` takes
options but not operands and fails on one rather than ignoring it.

**The differential corpus needed a shape to compare against at all.** Non-interactive bash does not
expand aliases: it needs `shopt -s expand_aliases` *and* the definition on an earlier line, because it
expands at parse time. The corpus cases are therefore three-liners beginning
`shopt -s expand_aliases || true` — this shell has no such option and always expands, so the `|| true`
carries both sides through the same script. Adding `expand_aliases` to the table as a no-op was
rejected: the plan's own rule is that an unknown option is an error, and a flag that silently does
nothing is exactly what that rule forbids. The directory stack is *counted* rather than printed
(`for d in $(dirs); do echo x; done`) for the same reason `cd -` could not be compared in Phase 4:
the oracle and this shell run in different temp roots.

**The ratchet improved by 23: 2186 → 2163.** Twenty-four cases newly pass — seventeen in
`alias.test.sh` (including recursive expansion of the first word, an alias overriding a builtin, and
aliases inside a subshell, a here-document and a command substitution) and seven in
`builtin-eval-source.test.sh` (a nonexistent file, no arguments, a syntax error, a directory, and `exit`
within `source`). One newly fails and is recorded: `alias.test.sh:39`, a here-document inside an alias
body, where the behaviour being compared against is annotated in the corpus as a bash bug.
Whole suite: 1901 passed, 0 failed.

## Phase 7: rc files and the prompt
Status: Complete

`shsh` only. Nothing in this phase may move into the core: it needs a home directory, a start-up
sequence and a terminal.

- [x] `shsh` reads `~/.shshenv` on every start — `-c`, a script, a pipe, interactive — before
      anything else runs.
- [x] `shsh` reads `~/.shshrc` only when interactive, after `~/.shshenv`.
- [x] Both are executed through the core's `source` machinery rather than a second loader.
- [x] A missing rc file is silence, not an error. A **syntax error in one is reported with the file
      name and does not stop the shell starting** — an unusable shell is worse than a broken alias.
- [x] `--norc` skips both, for tests and for debugging a broken config.
- [x] `PS1` and `PS2`, when set, replace the hardcoded `<dir>$ ` and `> `. The minimum expansion set
      worth having: the working directory, the home-relative working directory, and the last exit
      status. Anything beyond that is named as unsupported rather than approximated.
- [x] `src/Sharp/README.md` documents both files, their order, `--norc`, and the prompt values.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~RcFile|FullyQualifiedName~Prompt"`
  — green. The rc tests point `HOME` at a temporary directory rather than reading the real one, so
  the suite never depends on the machine it runs on.
- Black-box, through the binary: a `~/.shshenv` defining a variable is visible to `shsh -c`; a
  `~/.shshrc` alias is *not* visible to `shsh -c` but is interactively; `--norc` skips both; a
  broken rc file reports its name and the shell still starts.
- `dotnet test tests/Sharp.Shell.Tests` — whole suite green.

### Phase Summary

`Session` gained `LoadStartupFiles(interactive)`, called by `Program` after the session exists and
before anything runs. `~/.shshenv` always, `~/.shshrc` only when interactive, and an `exit` in either
is honoured before the command the shell was started for. A missing file is silence. A file that
cannot be parsed is reported with its name — `shsh: .shshenv: unterminated single quote` — and the
shell starts anyway.

**The syntax check happens before the file runs, deliberately.** A typed line that will not parse is
handed to a real shell; an rc file must not be, or a broken `.shshrc` would be executed by
`/bin/bash` behind the user's back. `CommandReader.SyntaxErrorIn` already answers that question, so
the loader asks it first and reports rather than running.

**`Execute` stayed internal.** The plan asked for rc files to run "through the core's `source`
machinery", and the obvious way — calling `executor.Execute` — is blocked on purpose: the comment on
`ShellExecutor.Run` says `Execute` is internal so no other assembly can reach execution without
passing through the approval path. Making it public to load an rc file would have punched a hole in
that invariant to save a line. The loader calls `executor.Run` instead, which is the same machinery
with the approver in front of it, and the binary does the part that is genuinely its own: finding the
file.

`SessionSettings` gained `ReadsRcFiles` (from `--norc`) and `Home`. `Home` is a seam so a test can
point the rc lookup at a temporary directory; production leaves it null and `$HOME` answers. The
prompt deliberately does **not** use it: `\w` shortens against the live `$HOME`, because someone who
changes `HOME` expects the prompt to follow.

`PromptRenderer` implements five escapes — `\w`, `\W`, `\?`, `\$`, `\\` — and leaves every other
backslash escape bash defines exactly as it was typed. `\?` is this shell's own: bash has no escape
for the exit status, which is the one thing the plan asked for and bash cannot spell. The unsupported
set is named in `src/Sharp/README.md` rather than approximated: a hostname nobody looked up would be a
lie, and a visible `\h` is a question.

**Every black-box run now passes `--norc`.** Without it, `SharpContractTests` and `SharpCorpusTests`
would read the `~/.shshenv` of whoever ran the suite — the tests would measure a developer's home
directory. `SharpRcFileTests` opts back in with a `HOME` of its own, which `SharpBinary.Run` can now
set for the child.

**A pty-driven interactive test was written, proved the feature, and then removed.** Driving the
binary through the vendored `test-pty` harness confirmed both halves — `~/.shshrc` read at a real
prompt, and `PS1` from it rendered — and passed in 89 ms on its own and in 54 s alongside the
conformance and corpus suites. **Run as part of the whole 1926-test suite it deadlocks**, and the run
never finishes: `UnixPtyBackend` takes a `NoGCRegion` and calls `forkpty`, which is the classic
fork-in-a-threaded-process hazard, and a test host with that many parallel tests is exactly the
condition it guards against. The interactive path is covered in-process instead
(`RcFileTests.TheInteractiveFileIsReadWhenTheShellIsInteractive`, `PromptTests`), and the binary path
black-box. **This is a finding about the harness, not about the shell, and it deserves its own fix** —
either serialising pty starts against the rest of the suite or a spawn that does not fork.

Whole suite: 1926 passed, 0 failed. `dotnet format --verify-no-changes` clean.

## Final Recap

All seven phases are complete. `dotnet test` is green at **1926 passed, 0 failed**, and the oils
conformance ratchet moved from **2230 failures to 2163** — 67 cases that used to fail now pass, with
four deliberate regressions recorded in `expected-failures.txt`, each with the reason beside it.

| Phase | What landed |
|---|---|
| 1 | `ShellVariables`: one table, an exported flag per name, `export -n`, `unset` |
| 2 | `ShellState.ShellName`, host-supplied; every message and `$0` read it |
| 3 | `ICommandExecutor` carries the environment; `shsh` seeds from the OS, `NativeTier` applies it |
| 4 | `~` → `$HOME`, `$PWD`/`$OLDPWD`, `cd -`, `CDPATH`, `IFS` |
| 5 | One options table, three spellings; errexit, nounset, pipefail, autocd |
| 6 | `alias`/`unalias`, `source`/`.`, `pushd`/`popd`/`dirs`, autopushd |
| 7 | `~/.shshenv`, `~/.shshrc`, `--norc`, `PS1`/`PS2` |

**Thirty differential cases were added to `LanguageCorpus` and every one of them matched the real bash
on its first run.** That is the strongest evidence in this plan: the semantics were probed from
bash before being written, not guessed and then patched.

**Five things the plan got wrong, all found by running it.** They are recorded in the phase summaries
and repeated here because a reader of the plan should not have to find them:

1. **`pipefail` reports the rightmost failing stage**, not the first (Phase 5).
2. **Options do not all survive a fork** — errexit must not, which is what bash's `inherit_errexit`
   is for (Phase 5).
3. **`nounset` exits 127**, not 1 (Phase 5).
4. **`CDPATH` is searched before the working directory**, not after (Phase 4).
5. **Phase 1 *is* observable** — the plan predicted the ratchet would not move, and it moved by nine,
   because `unset` did not exist and `export -n` was a rejected flag (Phase 1).

**Two live bugs were fixed on the way, neither of them in the plan.** The unwind never reset, so a
session that refused one command was silently dead for every line after it. And `set -e` stopped a
`-c` body but not a script file, because the unwind ends a run and a script is read a line at a time;
the suite's own `AScriptFileReadsTheSameAsOneCommandString` caught it.

**One thing the plan asked for that could not be done as written.** Two of Phase 4's differential
cases — `cd /tmp && cd - && pwd` and a bare `echo ~` — print absolute paths, and
`DifferentialTests.Compare` gives bash and this shell *different* temp roots, so no such case can ever
match. They are covered by unit tests against hand-probed bash behaviour, and the corpus got path-free
equivalents. The same constraint shaped the directory-stack cases in Phase 6, which count the stack
rather than print it.

**What is deliberately still missing**, beyond the plan's own non-goals: `local` (an
`errexit-osh.test.sh` case needs it), `noclobber` (a `sh-options.test.sh` case needs it), and bash's
third variable state — exported but unset — which one oils case about associative arrays wants and the
settled one-flag model cannot express.

## Deployment Plan

**Release as v0.3.0.** `ICommandExecutor.Execute` and `ExecuteLine` both gained
`IReadOnlyDictionary<string, string> environment` with no compatibility overload, so every consumer
must change. The same release carries `CommandExecution` becoming a class with settable `Output`,
`ExitCode` and `Error` (main's "read a command's exit code when its output ends", merged into this
branch), which breaks a host that built one with a `with` expression or deconstructed it positionally. `ShellState`'s constructor gained two optional arguments (`shellName`, `environment`),
which is source-compatible. The messages a user sees no longer say `duetui-shell:` — anything matching
on stderr matches the name the host supplied, and `ShellResult.RefusalReason` is the thing to read
instead.

**duetui-side work, in order:**

1. Implement the new `ICommandExecutor` signature in the guest's executor. Nothing forces the guest to
   *use* the environment; filtering it is the host's decision and the interface is where that decision
   belongs.
2. Pass the guest's environment into `ShellState`. No new host plumbing is needed:
   `WasiOptions.Environment` already reaches the guest and already carries `PWD`. The guest wrapper is
   outside `Sharp.Shell`'s compile constraints, so it may call `Environment.GetEnvironmentVariables()`
   freely. Until it does, `$PWD` expands to nothing — the live bug this plan found.
3. Choose the guest's shell identity. It is not `shsh`, and a host that supplies nothing now reports
   `sharp-shell`. Pick the name before the first user sees a refusal message.
4. Re-read `docs/host-integration.md`, which carries all three decisions and the v0.3.0 notes.

**Handoff to the line-editor plan.** `histignoredups`, `histignorespace` and `noflowcontrol` are stored
in `ShellOptions` and read by nobody. `HISTFILE`/`HISTSIZE`/`SAVEHIST` are ordinary variables. That
plan has somewhere to read from and nothing to invent.

**One follow-up that belongs to the vendored harness, not to this shell.** `test-pty` deadlocks when a
pty is started from a test host running the full suite in parallel (`forkpty` under a `NoGCRegion`).
Until that is fixed, interactive behaviour is proved in-process. The test that demonstrated it is
described in Phase 7's summary and can be restored in a commit as soon as the harness can survive it.
