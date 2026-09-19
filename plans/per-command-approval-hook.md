# Per-Command Approval Hook

Give the shell a seam the embedding host answers before **every** command dispatch — owned applet
or native program, fully expanded, inside loops, branches and `$( )` — so the host never parses
bash to decide what it is consenting to.

**Status: delivered**, released as `v0.2.0`. The work landed in `9a7b8d8` (*feat: ask the host
before every command dispatch*), `0b3df67` (*docs: describe the per-command approval contract*) and
`0ab91eb` (*test: record the oracle answers the new differential needs*). The contract as shipped
lives in `docs/host-integration.md`.

Every phase below is complete, its boxes ticked, each with a Phase Summary recording what was
decided and the verification run behind it — read them for why the code looks the way it does. What
is *not* one-to-one is the commits: there are two rather than the six this plan's Deployment Plan
asked for, because `ShellExecutor.cs` alone is touched by phases 1 through 5 and splitting it after
the fact would have meant hand-authoring intermediate states that never existed and never compiled.
Fake bisectability is worse than none, so the per-phase history lives here instead.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the consent decision from a static pre-pass over the command line to a run-time hook
at the dispatch point, so a denial can name `rm build/a.cs` on iteration 3 of a loop rather than the
unexpandable `rm $f` a parser sees.

**Architecture:** One new seam, `ICommandApprover`, consulted in `ShellExecutor.RunOwnedOrExternal`
— the single point every command passes through with `name` and `arguments` already expanded.
Classification stops answering *"do we own every program in this line?"* and starts answering
*"can this shell run this line at all?"*.

**The invariant this plan establishes:** a native process starts only because `Sharp.Shell` asked
for one. The host stops deciding to go native — it stops having a whole-line `bash -c` path at all —
and becomes a service the shell calls. Everything native flows through `ICommandExecutor`, in one of
two requests the shell makes, and both are gated by `ICommandApprover` first.

**Tech Stack:** xunit.v3 on VSTest (`dotnet test tests/Sharp.Shell.Tests --filter
"FullyQualifiedName~X"`), `ShellHarness` / `BashOracle` test support, differential suites against
real `bash`.

**Motivating consumer:** duetui, which vendors this repo as `vendor/sharp-shell`. Its current
work-around is a host-side lexer pass (`plans/per-command-bash-approval.md` in that repo) that slices
a line into segments before it runs. Everything this plan lands makes that pass unnecessary.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Branch: `feature/per-command-approval`, cut from `main` at `f7116b4`.

## Global constraints

- Never commit without the user's explicit approval. Each phase ends with a proposed commit; ask.
- `dotnet test tests/Sharp.Shell.Tests` green before every commit.
- `Sharp.Shell` keeps its compile constraints: zero package references, no
  `System.Diagnostics.Process`, no threads, no reflection — it must still trim into a NativeAOT
  WASI-P2 guest.
- The `sharp` binary's behaviour does not change: its default approver allows everything, so
  `--root`, `--strict` and `--explain` mean exactly what they mean today.

## Ground truth in the tree

- `src/Sharp.Shell/Execution/ShellExecutor.cs` — `RunOwnedOrExternal(name, arguments, input, state,
  errorSink, cancellationToken)` is the single dispatch point. `Invoke` expands words
  (`TryExpandWords`) and resolves redirections before calling it, so `name` and `arguments` are
  final. `RunExternal` already calls `external.Execute(...)`.
- `ShellExecutor.Substitute` runs `$( )` through `Execute` against a forked state, so a hook at the
  dispatch point sees substituted commands for free.
- `RunFor` / `RunWhile` call `Run` per iteration, so the hook fires per iteration with the loop
  variable already bound.
- `ShellExecutor.Run` is the only public way in and currently short-circuits on
  `ExecutionTier.Native` — returns `ShellRun(classification, null)` with nothing touched.
- `ShellState.ExitRequested` / `ExitStatus` is the existing unwind mechanism, checked in
  `RunSequence`, `RunAndOr`, `RunWhile`, `RunFor`.
- `ICommandExecutor.Execute` returns `CommandExecution(bool IsSupported, int ExitCode,
  IEnumerable<string> Output, string Error)` — lazy in type, materialized by today's callers.
- `Parser.cs:22` — `ProcessOnlyCommands = ["trap", "jobs", "fg", "bg", "wait", "disown", "coproc"]`.
  `Parser.cs:29` — `UnsupportedKeywords = ["select", "function"]`. `&` is rejected at `:67` and `:445`.
- `tests/Sharp.Shell.Tests/EscalationRateTests.cs` — the existing measurement of how often real agent
  commands escalate. It is the metric Phase 5 moves.

## Non-Goals

- **File-operand gating moving to a hook.** `Classification.Reads` / `Writes` / `KnowsEveryRead` stay
  as they are. The same argument applies to them — a per-open hook would beat static resolution — but
  it is a second seam with its own design, and folding it in here doubles the blast radius.
- **Emulating the process model.** `&`, job control, `trap`, `coproc` and process substitution stay
  refused by name. They need concurrency the WASI-P2 guest has no threads for. See Phase 6 for what
  happens to those lines instead.
- **Prompting policy.** How the host caches, batches or renders a decision is the host's business.
  This plan defines the question, not the dialog.

## Phase 1: The `ICommandApprover` seam
Status: Complete

- [x] Add `src/Sharp.Shell/ICommandApprover.cs`, shaped like `ICommandExecutor` beside it:
      `CommandApproval Approve(string program, IReadOnlyList<string> arguments, string
      workingDirectory, bool isOwned, CancellationToken cancellationToken)`, returning
      `CommandApproval(bool IsAllowed, string? Reason)`.
- [x] Ship two implementations: `AllowAllCommandApprover` (the default, so `sharp` is unchanged) and
      `DenyingCommandApprover` (the fail-closed posture, mirroring `NotSupportedCommandExecutor`).
- [x] Add it as a third `ShellExecutor` constructor parameter with a defaulted overload, so existing
      construction sites keep compiling.
- [x] Consult it at the top of `RunOwnedOrExternal`, **before** the `applets.TryGet` branch, so owned
      applets are gated too — this is what finally lets a host refuse `rm` by name rather than only
      by path.
- [x] `isOwned` is passed as information, not policy: the host may want a different answer for a
      sandboxed applet than for a real process, and only the shell knows which it is about to run.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Approver"` — new suite green,
  covering: the hook fires once per simple command; `for f in a b c; do rm $f; done` fires three
  times with `rm a`, `rm b`, `rm c`; `echo $(git rev-parse HEAD)` fires for both `echo` and `git`;
  `if X; then A; else B; fi` fires for the taken branch only; the hook fires for owned applets and
  for native names alike; an allowed decision leaves behaviour byte-identical.
- `dotnet test tests/Sharp.Shell.Tests` — whole suite green with the default approver, proving the
  seam is inert until someone plugs into it.

### Phase Summary

The seam is in and inert. `src/Sharp.Shell/ICommandApprover.cs` carries `CommandApproval(bool
IsAllowed, string? Reason)` with `CommandApproval.Allowed` and `CommandApproval.Deny(reason)`, the
`ICommandApprover` interface, and the two implementations — `AllowAllCommandApprover` and
`DenyingCommandApprover`. `ShellExecutor` takes it as a third primary-constructor parameter with a
two-argument overload that supplies `AllowAllCommandApprover`, so `Session` and every other
construction site compiled untouched.

Decisions worth knowing before continuing:

- **`isOwned` means "the applet is the thing about to run"**, not "an applet of that name exists.
  `RunOwnedOrExternal` now calls a new `ResolveOwned(name, arguments)` that returns an
  `OwnedCommand(IApplet Applet, IReadOnlyList<string> Arguments)` record or null; it returns null
  when the applet rejects a flag, because that invocation goes to the native tier. Short-flag
  bundles are expanded once, inside `ResolveOwned`, and the expanded list rides on the record.
- **The approver sees the unexpanded `arguments`**, the same list the native tier would receive —
  not the bundle-expanded applet list. `ls -la` is asked about as `ls -la`, never as `ls -l -a`.
- **Interim refusal behaviour, pending Phase 2.** A denial returns `AppletRun.Failed(126)` and
  writes `duetui-shell: <reason>` to the command's error sink. That is the exit code and the message
  Phase 2 formalizes; what Phase 2 still has to add is the *unwind* — today a denial is an ordinary
  non-zero result, so `denied || fallback` still runs `fallback`. Nothing in this phase's tests
  asserts otherwise.
- `DenyingCommandApprover`'s reason is `"<program>: not approved"`.

New test support: `tests/Sharp.Shell.Tests/Support/RecordingCommandApprover.cs` (records an
`ApprovalRequest(Program, Arguments, WorkingDirectory, IsOwned)` per call, with a `Texts` helper and
an optional `Func<ApprovalRequest, CommandApproval>` decision) and
`Support/CountingCommandExecutor.cs` (counts native dispatches, so a test can prove a refused
command never reached `ICommandExecutor`). `ShellHarness` gained an optional second constructor
parameter, `ICommandApprover? approver = null`.

**Verification run.** `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Approver"` —
11 passed, 0 failed. `dotnet test tests/Sharp.Shell.Tests` — 1640 passed, 0 failed, 0 skipped
(2 m 6 s), which is the whole suite unchanged by the seam.

## Phase 2: Refusal semantics
Status: Complete

A denial must stop the run, not return non-zero and continue — otherwise `denied || fallback` routes
around a refusal into a branch nobody approved.

- [x] Add `ShellState.RefusalRequested` / `RefusalReason` alongside the existing `ExitRequested` /
      `ExitStatus`, and unwind through the same checks in `RunSequence`, `RunAndOr`, `RunWhile`,
      `RunFor`, `RunCase`, and out of `Substitute`.
- [x] A refused command exits `126` ("found but not executable"), distinct from `127`
      ("command not found") which `RunExternal` already uses.
- [x] Write the reason to stderr as `duetui-shell: <reason>`, matching every other refusal message
      in `ShellExecutor`.
- [x] Surface it on `ShellResult` so the host can tell a refusal from an ordinary failure without
      parsing stderr.
- [x] A refusal inside `$( )` refuses the outer line too — the substitution's fork must not swallow it.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Refusal"` — green, covering:
  `allowed && denied && allowed` runs the first and stops, third never dispatches;
  `denied || fallback` does **not** run `fallback`; a refusal on loop iteration 2 stops the loop;
  a refusal inside `$(…)` stops the outer command; exit code is `126`; `ShellResult` reports the
  refusal distinctly.
- Explicitly recorded in the Phase Summary: commands *before* the refused one have already run.
  That is the contract change this plan makes — a line stops being atomic — and the summary must say
  so in those words.

### Phase Summary

**The contract change, in those words: commands *before* the refused one have already run. A line
stops being atomic.** That is not a defect of the implementation; it is what moving the decision to
run time costs. The shell can only ask about `rm $f` once `$f` has a value, and by the time it can
ask, the commands ahead of it are done. `RefusalTests.WorkDoneBeforeTheRefusalStands` asserts it
rather than describing it: `echo written > made.txt; rm keep.txt; echo never > after.txt` leaves
`made.txt` written, `keep.txt` intact, and `after.txt` never created.

What a refusal now does: `ShellState.RequestRefusal(reason)` sets `RefusalRequested` and
`RefusalReason` beside the existing `ExitRequested` / `ExitStatus`, and a new `ShellState.IsUnwinding`
(`ExitRequested || RefusalRequested`) is what every construct checks. `ShellExecutor.UnwindStatus`
answers `126` for a refusal and `ExitStatus` for an exit. `RefusedExitCode = 126` is named once, at
the top of `ShellExecutor`.

Unwind points, all of them: `RunSequence`, `RunAndOr`, `RunWhile` (both the loop guard and straight
after the condition, so a refused condition is not mistaken for a loop that ended), `RunFor`,
`RunCase` (after the subject expands and after each non-matching arm, because both can substitute),
`RunIf` — **not in the plan's list but the same bug class as `denied || fallback`**: without it a
refused condition reads as false and the `else` branch runs — `RunPipeline` (after every stage), and
out of both forks, `Substitute` and the new `RunSubshell`.

Decisions worth knowing before continuing:

- **`RunSubshell` is new.** The `SubshellNode` arm used to be an inline `Run(..., state.Fork(), ...)`.
  A fork must not swallow a refusal, so the arm became a method that copies `RefusalRequested` back
  to the parent. `Fork()` itself does **not** copy refusal state — forking an already-refused state
  does not happen, and carrying it in would be a second, silent path.
- **The refusal message is written in `Invoke`, not at the denial site.** It goes to `writeError`,
  the shell's own stderr, rather than through the command's `errorSink` — a `2>` belongs to a
  command that never got to run. `Invoke` returns before `ApplyOutputRedirection` when this dispatch
  was refused, so a denied `cat x > out` does not truncate `out` on its way out
  (`ARefusedCommandDoesNotTouchItsRedirectionTarget`). `Invoke` also returns early *before*
  dispatching when an expansion already refused, which is what keeps the message from printing twice
  for `echo $(rm f)`.
- **`ShellResult` gained a fourth member**, `string? RefusalReason = null`, plus `WasRefused`.
  Defaulted, so every existing construction site compiled untouched. This is the reliable channel —
  stderr is where every other failure writes too.
- The Phase 1 interim behaviour is now the real behaviour: exit `126`, message
  `duetui-shell: <reason>`. What changed is that it unwinds.

**Verification run.** `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~Refusal"` —
37 passed, 0 failed; 12 of those are the new `RefusalTests`, the other 25 are pre-existing
`AwkParserTests` that share the substring. `dotnet test tests/Sharp.Shell.Tests` — 1652 passed,
0 failed, 0 skipped (2 m 6 s).

## Phase 3: Canonical command text
Status: Complete

The host matches rules against text. `program` + `string[] arguments` must have exactly one spelling,
or `dotnet build "my proj"` and `dotnet build my\ proj` become different rule targets.

- [x] Add `src/Sharp.Shell/CommandText.cs`: `public static string Of(string program,
      IReadOnlyList<string> arguments)` — POSIX single-quoting for any argument containing
      whitespace, a quote, or a shell metacharacter; bare otherwise.
- [x] Put it on `CommandApproval`'s input so every host spells the target identically rather than
      each one joining arguments its own way.
- [x] Document the consequence on `ICommandApprover`: the text is **post-expansion**, so `dotnet
      $VERB` reaches the host as `dotnet build`. That is what actually runs, and it is not what the
      user typed.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~CommandText"` — green, covering:
  arguments with spaces, single quotes, double quotes, `$`, backticks, newlines, empty string, and
  non-ASCII; round-trip through `Lexer.Tokenize` yields the original argv.
- Differential: for a corpus of argv arrays, `CommandText.Of` fed back through real `bash -c` with a
  printing helper reproduces the same argv.

### Phase Summary

`src/Sharp.Shell/CommandText.cs` has `Of(program, arguments)` and `Quote(word)`. Quoting is
allow-list rather than deny-list: a word is bare only when every character is a letter or digit
(`char.IsLetterOrDigit`, so non-ASCII letters stay readable) or one of `_-./:@+,%=`. Everything else
— whitespace, both quotes, `$`, backticks, `*?[]{}`, `~`, `!`, `#`, `&|;<>()`, backslash — is
single-quoted, and an embedded `'` becomes `'\''`. The empty string quotes to `''`.

**The seam's signature changed.** `ICommandApprover.Approve` now takes `string commandText` between
`arguments` and `workingDirectory`, and `ShellExecutor` fills it with `CommandText.Of(name,
arguments)`. That is the literal reading of "put it on `CommandApproval`'s input": the host receives
the canonical spelling rather than joining the arguments its own way. It is six parameters now,
which is past this repo's usual limit, but it keeps the shape of `ICommandExecutor.Execute` next
door and maps straight onto a flat WIT import — the `host-approve-command` the Risks section
anticipates. If a later phase needs a seventh, that is the moment to fold them into a request
record, not now.

The post-expansion caveat is documented on the interface itself, not only in the plan: `dotnet
$VERB` reaches the host as `dotnet build`.

`RecordingCommandApprover.ApprovalRequest` gained `CommandText` and lost its home-made `Text`
helper, so the approver suites now assert against the real spelling rather than a test-local
imitation of it.

**Verification run.** `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~CommandText"`
— 26 passed, 0 failed: 25 unit cases (`CommandTextTests`, covering spaces, both quote kinds, `$`,
backticks, globs, `~`, newlines, tabs, backslashes, the empty string and non-ASCII, plus the
round-trip property that `Lexer.Tokenize` gives back the original argv as fully-literal words) and
the differential (`Differential/CommandTextDifferentialTests`, 14 argv arrays spelled by
`CommandText.Of`, run through real `bash -c` as `printf '%s\0' …`, split on NUL and compared).

The differential is gated on `BashOracle.IsLive`, not `IsAvailable`: it asserts against *real* bash,
and gating on availability would make it throw `MissingRecordingException` on a replay machine until
someone regenerated the committed cassette. Regenerating means a full-suite run under
`SHARP_ORACLE_RECORD=1` — `OracleRecorder.Write` emits only what that run captured, so a filtered
record run would truncate the cassette. Left alone deliberately; the entries will be picked up the
next time the cassette is regenerated for other reasons.

`dotnet test tests/Sharp.Shell.Tests` — 1678 passed, 0 failed, 0 skipped (2 m 5 s).

## Phase 4: The shell is the only thing that asks for a process
Status: Complete

Stop diverting whole lines. The shell runs the line end to end and reaches `ICommandExecutor` per
unowned command. The double-execution hazard that motivated all-or-nothing disappears with the
fall-through that caused it: there is no longer a path that runs the owned half and then hands the
same line to a real shell.

- [x] `ShellExecutor.Run` no longer short-circuits on unowned programs. Classification still runs,
      but the only verdict that returns without executing is *unrunnable* — a parse failure or a
      process-only construct (Phase 6).
- [x] Split `Classification` accordingly: keep `Tier` for compatibility but drive the short-circuit
      off a new `IsRunnable` / `UnrunnableReason`, so "we don't own `git`" stops being a reason to
      hand the line back.
- [x] Add the second request to the seam: `ICommandExecutor.ExecuteLine(string commandLine, string
      workingDirectory, CancellationToken)`. It is how the shell asks for a line it cannot run
      itself — and it is the shell asking, so the invariant holds. The host never reaches for a
      process on its own initiative.
- [x] Gate `ExecuteLine` through `ICommandApprover` too, as one target: the whole line. Coarser than
      per-command, and correct, because for these lines nobody can say what the commands are.
- [x] `sharp --strict` keeps its meaning by supplying `DenyingCommandApprover` for unowned names
      rather than by refusing to execute the line. `--explain` prints the per-command decisions.
- [x] Make `CommandExecution.Output` genuinely lazy end to end so `cat huge.log | native` and
      `native | head -2` stream. `StageOutput` already hands a lazy `IEnumerable<string>` downstream;
      the executor implementation must not materialize. See Risks — the guest's WIT carries a
      materialized `proc-output` today, so streaming needs the handle-based imports instead.
- [x] Native stdin: `RunExternal` passes `input` through; confirm a native consumer receives an owned
      producer's output incrementally, and that `PipelineStopTests`' early-stop property holds across
      the boundary.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~ExternalExecutor|FullyQualifiedName~PipelineStop|FullyQualifiedName~NativeFallThrough"`
  — green; `NativeFallThroughTests` will need rewriting for the new contract, and the rewrite is the
  proof that the contract changed deliberately.
- New test: nothing calls `ICommandExecutor` unless `ShellExecutor` called it. Assert by giving the
  executor a recording double and checking that every invocation is preceded by an approver call for
  the same target.
- New test: `ls | native-cat | head -2` stops the producer early across both boundaries.
- New test: an owned command mutates, then a native command is refused — the mutation stands and the
  line stops. Asserted explicitly, because it is the behaviour Phase 2 traded for.
- `dotnet test tests/Sharp.Shell.Tests` — whole suite green, differential suites included.

### Phase Summary

The fall-through is gone. `ShellExecutor.Run` executes every line it can run, and `ShellRun.Result`
is no longer nullable — a line the shell cannot run is not handed back, it is asked for through
`ICommandExecutor.ExecuteLine` and its answer comes back in the same shape as any other.

**What "unrunnable" means.** `Classification` gained `string? UnrunnableReason` (defaulted, so every
existing construction site compiled) and `IsRunnable => UnrunnableReason is null`, plus a
`Classification.Unrunnable(...)` factory beside `Native(...)`. `Tier` and `UnownedPrograms` are
untouched and still describe the line, which is why every existing `ClassificationTests` assertion
still holds. Two things set it:

- a parse failure — `&`, `trap`, `coproc`, `select`, `function`, and anything malformed;
- an expansion form the shell does not implement (`CheckParameterForm`). **This is wider than the
  plan's "a parse failure or a process-only construct."** It is deliberate: an unowned program has
  another tier to ask, and an unimplemented `${…}` form has none. Leaving it runnable would mean the
  line half-runs and then dies at exit 2 with no fall-through — strictly worse than today for no
  gain.

Not owning `git`, an explicit path like `./build.sh`, a command name that needs expanding, and an
applet refusing a flag are all **runnable** now. That last one is the quiet win: `sed --debug 's/x/y/'`
reaches `ICommandExecutor` as one command instead of sending its whole line away.

**Both seams gained their line-level request.** `ICommandExecutor.ExecuteLine(commandLine,
workingDirectory, cancellationToken)` is defaulted to `CommandExecution.NotSupported`, so the guest
and every test double keep the fail-closed answer without writing it out. `ICommandApprover`
gained a matching `ApproveLine(commandLine, workingDirectory, cancellationToken)` with **no**
default — the plan's invariant is that both requests are gated, and a security seam should not let
an implementor gate one by accident and miss the other. A refused line exits `126` with its reason
on `ShellResult.RefusalReason`; an allowed line no executor can take exits `127` and reports the
classification's own `UnrunnableReason`, which is what `--strict` used to print.

**The binary.** `NativeTier` stopped being a static `bash -c` helper and became the binary's
`ICommandExecutor`. `Execute` runs one program with its streams captured; `ExecuteLine` runs
`bash -c` **attached to the terminal**, exactly as before, because that path is the escape hatch for
constructs this shell has no model for and capturing it would break the interactive programs people
reach for it with. `Session` now holds one `ShellExecutor` built from the settings — `NativeTier` or
`NotSupportedCommandExecutor`, `AllowAllCommandApprover` or the new `OwnedOnlyCommandApprover` — and
its `Run` has no fall-through branch left.

**`--strict` changed observably, by decision.** It used to refuse to execute an unowned line: nothing
ran, exit `127`, `sharp: <reason>`. It now refuses the unowned command at the dispatch point: the
owned commands ahead of it run, exit `126`, `duetui-shell: 'git' is not one of the sandboxed
commands`. This contradicts the Global constraint "the `sharp` binary's behaviour does not change",
which was written before this phase existed; the user was asked and chose the refusal semantics over
preserving the old exit code. `BlackBox/SharpContractTests.Strict_mode_refuses_to_leave_the_owned_command_set`
and `SharpSessionTests` were updated, and Phase 6's README work has to say so.

**`--explain`** keeps its `[owned]` / `[native git] — …` first line and now prints one indented line
per decision beneath it — `  owned echo hi`, `  native git status — refused: …`. The decisions are
buffered by `ExplainingCommandApprover` rather than printed as they happen, because the approver
fires during execution and an unbuffered line would print before the classification line it belongs
under.

**Streaming: shell side done, executor side deferred.** The shell does not materialize anywhere —
`StageOutput` hands `execution.Output` straight on, `RunExternal` returns it as `AppletRun.Output` —
and `Support/StreamingCommandExecutor` proves it: `counting | native-cat | head -3` stops both the
owned producer and the native stage at ≤ 4 chunks, which is an owned producer feeding a native
consumer feeding an owned consumer that stops early, in one assertion.

What is **not** done is a non-materializing process executor. `NativeTier` and
`ProcessCommandExecutor` still read to the end, and they have to: `CommandExecution` reports
`ExitCode` and `Error` eagerly, and a process's exit code is not knowable until its output is
drained. Fixing that means reshaping `CommandExecution` the way `AppletRun` is shaped — a mutable
object whose exit code is final only after the enumeration — and moving `RunExternal`'s error write
to the end of the stream. That is the same work the Risks section defers to the handle-based
`host-proc-*` imports, so it belongs with them rather than here. Flagged rather than silently
skipped.

**Test support.** `CountingCommandExecutor` was replaced by `RecordingCommandExecutor`, which
records both request kinds (`Commands`, `Lines`), and `RecordingCommandApprover` gained a `Lines`
list so a suite counting per-command hooks cannot silently count a whole-line one.
`NativeFallThroughTests` was rewritten rather than deleted: it keeps its name and now asserts the
absence of the thing it is named for, including the invariant test the plan asked for — every
`ICommandExecutor` call is preceded by an approver call for the same target.

One incidental finding: `rm -rf dir && echo done | sed --debug 's/x/y/'`, a case in the old suite,
does not parse at all — `done` is a loop keyword, so the line was never Native-because-of-`sed`. It
is genuinely unrunnable, and the rewritten case uses `echo x` instead.

**Verification run.**
`--filter "FullyQualifiedName~ExternalExecutor|FullyQualifiedName~PipelineStop|FullyQualifiedName~NativeFallThrough"`
— 24 passed, 0 failed.
`--filter "FullyQualifiedName~SharpSession|FullyQualifiedName~SharpContract|FullyQualifiedName~Classification"`
— 97 passed, 0 failed.
`dotnet test tests/Sharp.Shell.Tests` — 1689 passed, 0 failed, 0 skipped (2 m 5 s), differential and
black-box suites included and none skipped.

## Phase 5: Shrink the residual
Status: Complete

Every construct still refused by name is a line that falls back to whole-line gating. Close the ones
that need no process model.

- [x] `function` and the `name() { … }` form — a function table on `ShellState`, called through the
      same dispatch point so bodies gate per command.
- [x] `select` — a loop over a prompt; no processes involved.
- [x] `$$` and `$!` — stable synthetic values, documented as synthetic. `$0` already answers
      `duetui-shell` in `ParameterExpander.TryExpandSpecial`; these join that table.
- [x] `[[ … ]]` — unimplemented today; neither the parser nor `TestApplet` knows it. Array
      parameters too, if the corpus shows them.
- [x] Re-run `EscalationRateTests` and record the before/after residual rate in the Phase Summary.
      The number is information, not a gate — that is already this suite's stated contract.

### Verification Plan
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~ControlFlow|FullyQualifiedName~ShellBuiltin|FullyQualifiedName~Parser"`
  — green with the new constructs.
- Differential against real `bash` for every construct added, via the existing oracle harness.
- `dotnet test tests/Sharp.Shell.Tests --filter "FullyQualifiedName~EscalationRate"` — report printed;
  residual rate recorded in the Phase Summary.

### Phase Summary

**The residual, before and after.** `EscalationRateTests` now reports four numbers instead of two,
because this plan split what "escalates" means. Over 334 mined agent commands:

| | before this plan | now |
|---|---|---|
| owned — every program ours | 162 (48.5%) | 162 (48.5%) |
| native — some program not | 172 (51.5%) | 172 (51.5%) |
| **unrunnable — handed over whole** | **172 (51.5%)** | **1 (0.3%)** |

The honest reading: **Phase 4 moved that number, not Phase 5.** Under the old model every native
line was gated whole, so the residual and the native rate were the same 51.5%. Now a native line
runs and is gated command by command, and only a line the shell cannot run at all goes out whole.
The one remaining case on this corpus is an unterminated double quote — a line real bash rejects
too. Phase 5's four constructs contributed nothing *to this corpus*, because it is mined from
`dotnet`/`git`/`find` sessions that never use them; what Phase 5 bought shows up in the conformance
corpus instead, where it is worth 3 cases directly and 63 in total with Phase 4.

**Shell functions.** `FunctionDefinition` in the AST; `name() { … }`, `function name { … }` and
`function name() { … }` all parse. Bodies live in `ShellState.Functions`, copied by `Fork()` like
variables. A call is dispatched through `RunOwnedOrExternal`, so it is approved like any command
and reports `isOwned: true`; functions take precedence over applets, as in bash. The body runs
against the *caller's* state — a `cd` inside one sticks — with only the positional parameters
swapped. Output is buffered rather than streamed, the same compromise `StageOutput` already makes
for a compound pipeline stage. Recursion is capped at `MAX_FUNCTION_DEPTH = 64` with a clear error,
because a stack overflow cannot be caught and a sandbox must not be crashable by `loop() { loop; }`.

**Positional parameters** had to come with functions, or `$1` would silently expand to nothing:
`ShellState.PositionalArguments`, with `$1`…`$N` and `$#` in `ParameterExpander`. Arithmetic needed
the same fix — `ArithmeticEvaluator` now takes the `ShellState` rather than its variable dictionary,
so `add() { echo $(($1 + $2)); }` answers 5 rather than 0. **The differential found that one**, not
a reviewer.

`$@` / `$*` join with a space, which agrees with bash for every argument without whitespace in it.
Where they would disagree — `"$@"` is N fields in bash and one string here — the expansion is
**refused by name** rather than answered wrongly. That is the conservative-applet rule applied to an
expansion.

**`$$` and `$!`.** Removed from the parser's process-only list. `$$` answers a fixed `"1"`,
documented on the constant as synthetic: a value that moved between runs would make every
differential comparison unreproducible, and a host must not build temp-file names from it. `$!` is
the empty string, which is not synthetic at all — it is exactly what bash answers when no background
job has ever been started, and `&` is still refused. `$PPID` stays refused.

**`[[ … ]]`** is a `ConditionNode`, not a `SimpleCommand`, because its operands are expanded with
`ExpandValue` — no field splitting, no globbing. That is the whole reason it exists in bash and the
one thing a `SimpleCommand` could not have given: `[[ -n $v ]]` holds for a value with a space in
it, and `[[ $f == *.cs ]]` matches a pattern instead of the files in the directory. Evaluation is
`TestApplet("[[")` — the same condition language, one parser, with three additions gated on the
`[[` spelling: `==`/`!=` match a glob pattern, `=~` runs a POSIX ERE through `PosixRegexTranslator`,
and `( … )` groups. `&&`/`||` are accepted as spellings of `-a`/`-o`.

`[[` is deliberately **not** in `AppletRegistry`. It is a keyword, and registering it would make it
reachable by expansion — `x='[['; $x -f f ]]` is an error in bash, and the conformance corpus tests
exactly that. `ShellExecutor` holds the instance instead and approves it through a new `TryApprove`
helper shared with `RunOwnedOrExternal`.

**`select`** is a `SelectNode`: menu to stderr, reply from stdin, `REPLY` and the loop variable set,
end of input ends it. Two behaviours were copied from bash because they are observable and the
differential caught both: it writes a newline to **stdout** at end of input, and it returns **1**,
whatever the body last returned.

**Conformance ratchet rebaselined** — `DUETUI_CORPUS_BASELINE=1`, which the file's own header
documents for exactly this case. 2294 → 2231 failing, **63 more cases pass**. Most of the diff is
detail text on cases that were already failing: Phase 4 means a line that used to produce nothing
now produces its owned half, so the recorded "actual" text changed everywhere. The header keeps the
provenance trail, which the regenerator would otherwise have dropped. One case
(`word-split.test.sh:44`, the arithmetic one) was deleted by hand afterwards when the positional fix
made it pass — note that `grep` treats this file as binary and silently finds nothing in it, so edit
it with something that reads it as text.

Four real defects were found by the differential rather than by review: positional parameters in
arithmetic, `select`'s trailing newline, `select`'s exit status, and — via the conformance ratchet —
`[[` being reachable by expansion, unbalanced parentheses inside `[[ ]]`, and a quoted `']]'` being
taken as the terminator. All six are fixed.

**Verification run.**
`--filter "FullyQualifiedName~ControlFlow|FullyQualifiedName~ShellBuiltin|FullyQualifiedName~Parser"`
— 77 + parser cases passed, 0 failed.
`--filter "FullyQualifiedName~DifferentialTests"` — 283 passed, 0 failed; **112/112 language-corpus
commands match real bash 3.2.57**, including 37 new lines covering every construct this phase added
(`$$` excluded on purpose — no real bash agrees with a synthetic value).
`--filter "FullyQualifiedName~EscalationRate"` — report printed, numbers above.
`dotnet test tests/Sharp.Shell.Tests` — 1716 passed, 0 failed, 0 skipped (2 m 5 s).

## Phase 6: The residual contract, documented
Status: Complete

`&`, job control, `trap`, `coproc` and process substitution stay refused: they need concurrency, and
the guest has no threads. Those lines, plus anything that will not parse, are the only ones that
reach `ExecuteLine` — still the shell asking, never the host deciding. That is the honest boundary
and the README must state it.

- [x] `README.md` — Confinement section: Rule 2 is reframed. It was "one unowned program sends the
      whole line native"; it becomes "the shell runs the line and asks about each command before
      dispatching it; a line it cannot run at all is one it asks the host to run whole, as a single
      decision". State plainly that a denial mid-line leaves earlier commands already run.
- [x] `README.md` — state the invariant directly: nothing starts a process except at this shell's
      request. A host that keeps its own `bash -c` path has not adopted this design.
- [x] `README.md` — document `ICommandApprover` beside `ICommandExecutor` as the second host seam,
      with the fail-closed posture spelled out.
- [x] `src/Sharp/README.md` — `--strict` and `--explain` under the new model.
- [x] `AGENTS.md` — check the confinement summary still holds.
- [x] Write `docs/host-integration.md`: what a host must implement, the two seams, the fail-closed
      requirement, the post-expansion target caveat from Phase 3, and prompt-caching guidance — a
      10k-iteration loop fires 10k hooks, so a host whose "once" answer means *once per dispatch*
      will make itself unusable on the first big loop.

### Verification Plan
- `grep -rn "all or nothing\|whole line native" README.md AGENTS.md src/Sharp/README.md` — no stale
  claims left.
- `dotnet test tests/Sharp.Shell.Tests` — green.

### Phase Summary

`README.md` — the opening paragraph no longer says an unowned line "is never partly executed"; it
says the shell asks the host for each command before dispatching it. Rule 2 is reframed from "all or
nothing" to "ask before every dispatch", with the mid-line-denial cost in its own emphasised
paragraph, in the words Phase 2's summary fixed. A new **The two host seams** section tables
`ICommandApprover` against `ICommandExecutor`, states the fail-closed posture and why `ApproveLine`
has no default implementation, and gives the 126 / unwind / `RefusalReason` contract. The
process-only list lost `$$` and gained `coproc`, with a sentence naming what stopped being refused.

`src/Sharp/README.md` — `--strict` documents **126**, not 127, and says plainly that the owned
commands ahead of the refusal have already run. `--explain`'s section now shows the per-command
decision lines. Both examples were **run against the built binary and pasted from its output**
rather than written from memory. The `Owned` filter paragraph was rewritten: it used to explain
itself in terms of Rule 2 sending whole lines elsewhere, which is no longer true — the filter now
earns its place by keeping the comparison off whatever `git` is installed on the machine.

`AGENTS.md` — checked, and it carries no confinement summary at all: it is the coding-conventions
file. Nothing to update, which is the answer the checkbox wanted.

`docs/host-integration.md` — new. Covers what a host implements, both seams with their signatures,
the fail-closed requirement (and that `AllowAllCommandApprover` is a compatibility shim nobody
should ship), what a denial does, the mid-line cost in a blockquote, the post-expansion target with
a typed-versus-asked table, prompt-volume guidance with the 10k-loop warning and concrete caching
advice, and the wasm-guest constraints — the UI thread, the epoch deadline, and the handle-based
imports streaming needs — carried over from the plan's Risks section so they live where an
integrator will find them.

**Verification run.** The stale-claim scan over `README.md`, `AGENTS.md` and `src/Sharp/README.md`
finds nothing (run in Python, not `grep` — see Phase 5 on `grep` and binary-looking files).
`dotnet test tests/Sharp.Shell.Tests` — 1716 passed, 0 failed, 0 skipped (2 m 5 s).

## Risks and host-side constraints

These live here rather than in the consuming repo because they constrain the *shape* of the seam:
designing a callback the guest boundary cannot carry would waste the whole plan.

**The builtin toolset starts using host imports.** `BrokeredHostImports` states today's line
plainly: *"The builtin toolset passes no rules at all. It calls none of these imports — its
filesystem is a real WASI preopen and every one of its calls is gated on the host side of the tool
call."* An approval callback crosses that line. It is the deliberate cost of moving the decision to
run time, and the host's import table must special-case the builtin identity rather than refusing it
the way it refuses every extension.

**The native-exec import already exists, materialized.** `host-exec: func(program: string, args:
list<string>) -> result<proc-output, string>` is exactly `ICommandExecutor.Execute`'s shape, and
`proc-output` is `{stdout: string, stderr: string, exit-code: s32}` — a buffer, not a stream. Phase
4's streaming goal cannot go through it. The handle-based imports beside it —
`host-proc-spawn` / `host-proc-write` / `host-proc-read` / `host-proc-status` / `host-proc-close` —
already carry duplex byte pipes and are the path streaming has to take.

**The approval callback blocks the guest.** A new import — `host-approve-command` — is synchronous
from the guest's side and may block for as long as a user takes to answer a modal. Two consequences
to prove before building on it:

- The guest invocation must not be on the UI thread, or the dialog can never render. The host's
  approval broker already assumes this shape — it bridges a blocking background call to the UI
  thread through a completion source — but it has never been exercised from inside a guest call.
- The wasm **epoch deadline** kills a long-running guest call. `RunWhile`'s comment names it as the
  guest-side stop for unbounded loops. A blocked approval must pause or extend that deadline, or
  every prompt the user thinks about for too long becomes a killed shell. Verify this first; it is
  the single most likely reason the design has to change shape.

**Prompt volume.** A 10k-iteration loop fires 10k approvals. The seam must let a host answer
"allowed, and stop asking me about this target for this run" cheaply, or the first big loop makes
the app unusable. This is why `CommandApproval` carries a reason rather than being a bare bool:
there is room to extend it without breaking the interface.

## Final Recap

The consent decision moved from a static pre-pass over the command line to a run-time hook at the
dispatch point, and the whole-line escape hatch it existed to guard disappeared with it.

**What a host sees now.** `ICommandApprover.Approve` fires once per command dispatch — owned applet
or real program, inside loops, branches, pipelines and `$( )` — with `program` and `arguments`
already expanded and a canonical `commandText` beside them. A denial exits `126`, writes
`duetui-shell: <reason>`, and unwinds the run, so `denied || fallback` cannot route around it.
`ShellResult.RefusalReason` reports it without parsing stderr. The one line that still goes out
whole is one this shell cannot run at all, and it goes out through the same two seams —
`ApproveLine`, then `ExecuteLine` — so the invariant holds without exception: **a process starts
because the shell asked for one.**

**What it cost.** A line stopped being atomic. A denial mid-line leaves the commands before it
already run, and that is not recoverable — the shell can only ask about `rm $f` once `$f` has a
value. It is stated in the README, in `docs/host-integration.md`, and asserted in
`RefusalTests.WorkDoneBeforeTheRefusalStands`.

**What it bought.** On 334 mined agent commands the residual — lines gated as one coarse decision
because nobody can say what is in them — went from **172 (51.5%) to 1 (0.3%)**, and that one is an
unterminated quote real bash rejects too. On the vendored oils corpus, 63 more cases pass
(2294 → 2231 failing). The owned/native split is unchanged at 162/172, and that is the point: a
native line is no longer a line that escapes, it is a line whose unowned commands are each asked
about.

**Six phases.** The `ICommandApprover` seam (inert by default); refusal semantics and the unwind;
`CommandText` canonical spelling; the shell as the only thing that asks for a process, which
retired `Classification`'s short-circuit in favour of `IsRunnable` / `UnrunnableReason` and turned
`NativeTier` into an `ICommandExecutor`; the residual shrunk by implementing shell functions,
positional parameters, `select`, `[[ … ]]`, `$$` and `$!`; and the documentation.

**Deliberate deviations, all recorded in their Phase Summaries.** `sharp --strict` now exits 126
rather than 127 and runs the owned commands ahead of the refusal — the user was asked and chose the
refusal semantics over the old exit code. `$@` is refused when an argument contains whitespace
rather than answered as one field. `$$` is a fixed synthetic `"1"`. A non-materializing *process*
executor is not done — `CommandExecution` reports `ExitCode` and `Error` eagerly, so `NativeTier`
and `ProcessCommandExecutor` must read to the end; the shell side is fully lazy and proven, and the
rest belongs with the handle-based `host-proc-*` imports.

Suite: **1716 passed, 0 failed, 0 skipped.** 112/112 language-corpus commands match real bash.

## Deployment Plan

Nothing here is committed yet — the work sits on `feature/per-command-approval`, cut from `main` at
`f7116b4`. Steps 1 and 2 are this repository; steps 3 onward are duetui.

### 1. Commit and merge this repository

```
dotnet build Sharp.slnx
dotnet test tests/Sharp.Shell.Tests        # expect 1716 passed, 0 failed, 0 skipped
dotnet format --verify-no-changes
```

Commit per phase, in order, so the contract change is bisectable:

| | |
|---|---|
| `feat: ask the host before every command dispatch` | `ICommandApprover`, `ShellExecutor` wiring, `ApproverTests`, test support |
| `feat: stop the run when the host refuses a command` | `RefusalRequested` unwind, `ShellResult.RefusalReason`, `RefusalTests` |
| `feat: give every command one canonical spelling` | `CommandText`, the seam's `commandText`, unit + differential tests |
| `refactor: make the shell the only thing that asks for a process` | `IsRunnable`/`UnrunnableReason`, `ExecuteLine`/`ApproveLine`, `NativeTier` as `ICommandExecutor`, `Session`, rewritten `NativeFallThroughTests` |
| `feat: run the constructs that need no process model` | functions, positional parameters, `select`, `[[ … ]]`, `$$`, `$!`, ratchet rebaseline |
| `docs: describe the per-command approval contract` | `README.md`, `src/Sharp/README.md`, `docs/host-integration.md` |

Merge to `main`. **Tag it** — duetui pins a submodule commit, and the interface change is breaking
for any other consumer.

### 2. Re-record the oracle cassette (optional, but do it before the next contributor does)

`Differential/CommandTextDifferentialTests` is gated on `BashOracle.IsLive`, so it skips where bash
is missing rather than failing on a missing recording. To fold its answers into the committed
cassette, run the **whole** suite with recording on — a filtered run truncates the cassette to what
that run captured:

```
SHARP_ORACLE_RECORD=1 dotnet test tests/Sharp.Shell.Tests
SHARP_ORACLE_REPLAY=1 dotnet test tests/Sharp.Shell.Tests    # prove the recording is complete
```

Record on the platform CI runs on: BSD and GNU coreutils disagree about formatting.

### 3. duetui: bump the submodule

```
cd vendor/sharp-shell && git fetch && git checkout <tag> && cd -
git add vendor/sharp-shell && dotnet build
```

The build **will** break, and that is the point: `ICommandApprover` is a required third constructor
argument in spirit (the two-argument overload supplies `AllowAllCommandApprover`, which must not
ship), and `ShellRun.Result` is no longer nullable, so the host's `if (run.Result is { } result)`
fall-through branch no longer compiles. Both are deliberate — fix them rather than working around
them.

### 4. duetui: implement `ICommandApprover` against `ApprovalGate`

- `Approve` maps the target to the existing rule model. Match on `commandText`, not on the raw line
  — see the post-expansion caveat in `docs/host-integration.md`.
- `ApproveLine` handles the coarse case. It has no default implementation; decide explicitly.
- Fail closed on every path: exception, timeout, a dialog the user dismissed.
- **Cache before shipping.** A 10k-iteration loop fires 10k approvals, and a host whose "always"
  answer means *once per dispatch* is unusable on the first real loop. This is the step most likely
  to look finished and not be.

### 5. duetui: close the host's own process path

This is the step that makes the invariant true end to end, and it is a deletion, not an addition:

- **Delete `nativeShell.RunAsync` and the host's whole-line escalation path.** After this the host
  owns no way to start a process the shell did not ask for.
- **Delete the host-side segmentation pass** — the lexer that slices a line into segments before it
  runs is what this plan exists to remove. Do not keep it "as belt and braces": two consent
  mechanisms that disagree are worse than either alone.
- **Route the degraded path through an in-process `ShellExecutor`** so it stops being an ungated
  hole.
- Retire `plans/per-command-bash-approval.md` Phase 1 there.

### 6. duetui: the guest

- Add the `host-approve-command` import to `wit/extension-api.wit`, and let the **builtin identity**
  through `BrokeredHostImports` rather than refusing it the way every extension is refused. The
  builtin toolset calls no host imports today; this crosses that line deliberately, and it is the
  cost of moving the decision to run time.
- Move native execution onto the `host-proc-spawn` / `host-proc-write` / `host-proc-read` /
  `host-proc-status` / `host-proc-close` handles. The materialized `host-exec` cannot carry
  streaming, and neither can this repo's `NativeTier` until `CommandExecution` stops reporting
  `ExitCode` and `Error` eagerly — that work is shared between the two repositories.

Before enabling the approval callback inside the guest, prove two things, **in this order**:

1. A blocked approval pauses or extends the wasm **epoch deadline**. Verify this first — it is the
   single most likely reason the design has to change shape, and without it a prompt someone thinks
   about for too long kills the shell.
2. The guest invocation is not on the UI thread, or the dialog can never render.
