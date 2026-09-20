# shsh

`Sharp.Shell` as a runnable shell. Commands the shell owns are C# function calls inside this
process — no `fork`, no `exec`, no `PATH`. Anything it does not own is started as a real program,
one command at a time. Only a line this shell cannot run at all — `&`, `trap`, a syntax error — is
handed to your real shell as the original string.

```
dotnet build src/Sharp/Sharp.csproj
src/Sharp/bin/Debug/net10.0/shsh
```

## Usage

```
shsh                       interactive
shsh -c "echo hi"          one command
shsh script.sh             run a file
echo "echo hi" | shsh      read from a pipe

--root <dir>   confine the shell to <dir>: every path above it is refused, which is the rule the
               sandboxed tool tier runs under. Without it the shell is unconfined, like any shell.
--explain      print each line's classification, then one line per command the shell decided about.
--strict       start no processes at all. An unowned command is refused where it would have been
               dispatched: the line exits 126 with the reason, and the owned commands ahead of the
               refusal have already run. This is the sandboxed guest's configuration, and the one
               to test against — with real programs available, they answer and the result measures
               nothing.
--norc         skip ~/.shshenv and ~/.shshrc, for tests and for debugging a config that breaks the
               shell.
```

## Start-up files

Two files, read from `$HOME` in this order:

| File | When |
|---|---|
| `~/.shshenv` | **Every** start — `-c`, a script, a pipe, interactive — before anything else runs. |
| `~/.shshrc` | Only when the shell is interactive, after `~/.shshenv`. |

That is the zsh split, and the reason there are two: a `-c` line and a script want the environment,
not the aliases and the prompt. Put `export`s and `PATH` in `~/.shshenv`; put `alias`, `setopt`, `PS1`
in `~/.shshrc`.

A **missing file is silence**. A file with a **syntax error is reported with its name and the shell
still starts** — an unusable shell is worse than a broken alias:

```
$ shsh -c 'echo still-here'
shsh: .shshenv: unterminated single quote
still-here
```

`--norc` skips both. An `exit` in either is honoured before the command the shell was started for.

These files run **this shell's language**, which is bash's with the gaps this README describes. A
`.zshrc` or a `.bashrc` is not a `.shshrc`: `shopt -s expand_aliases` is not an option here, because
aliases always expand, and an option this shell does not implement is an error rather than a flag that
silently does nothing.

## The prompt

`PS1` and `PS2` replace the defaults when set — otherwise the prompt is the working directory
relative to the root followed by `$ `, and the continuation prompt is `> `.

| Escape | Is |
|---|---|
| `\w` | the working directory, with `$HOME` written as `~` |
| `\W` | its last component |
| `\?` | the last command's exit status |
| `\$` | a literal `$` |
| `\\` | a literal backslash |

```
PS1='\w \$ '        →  ~/notes $
PS1='[\?] \W% '     →  [0] notes%
```

Every other escape bash defines — `\h`, `\u`, `\t`, `\!`, the colour sequences — is **not
implemented, and is left exactly as it was typed** rather than approximated. A hostname nobody looked
up would be a lie; a visible `\h` is a question. `\?` is this shell's own: bash has no escape for the
exit status.

## Scripts

A command is not always a line, so `shsh script.sh` and `echo … | shsh` read lines until the
command is whole: a function body, a multi-line `if`, a here-document and a trailing `\` or `|` all
continue onto the next line. Interactively, the continuation prompt is `> `.

A **syntax error stops a non-interactive shell where it stands**, as bash does: the offending
command does not run and neither does anything after it, and the exit status is 2. A construct this
shell does not implement — `&`, `trap`, process substitution — is not a syntax error: it is valid
bash, so the line is handed over and the script runs on.

Interactively, **Ctrl+C** abandons whatever is half-typed and brings the prompt back, and **Ctrl+D**
ends the session. Both matter at a `> ` prompt: an unterminated quote leaves one, and `exit` typed
there is swallowed into the pending command rather than run — which is what bash does too. Ctrl+C is
the way out. It needs one Enter to redraw the prompt, because the shell is blocked reading a line
rather than reading keys.

## What `--explain` tells you

The first line classifies the line; the indented ones are the decisions, one per command, in the
order the shell dispatched them.

```
$ shsh --explain -c 'ls src | head -3'
[owned]
  owned ls src
  owned head -3

$ shsh --explain --strict -c 'echo hi; git status'
[native git] — 'git' is not one of the sandboxed commands
  owned echo hi
  native git status — refused: 'git' is not one of the sandboxed commands
```

`[owned]` means every program in the line is one of ours. `[native …]` names the programs that are
not — and the line **still runs**, command by command, rather than being handed over whole.
`mutates` means the line can change files, which is what the agent's tool layer prompts on.

Note what the second example shows: `echo hi` printed before `git status` was refused. A denial
stops the line, but it cannot undo what ran ahead of it.

## Two differences from the sandboxed tool

- **State persists between lines.** A `cd` sticks, because a shell is a conversation. The tool tier
  gets a fresh state per call, so a stray `cd` cannot silently relocate a later one.
- **Unconfined by default.** `--root` opts into the confinement the tool tier always has.

## Tests

`tests/Sharp.Shell.Tests/BlackBox` drives this binary as a black box, through a real process:

- `SharpContractTests` — argv, which stream each thing lands on, exit status, script files,
  piped input, persistence between lines, `--root` confinement, `--strict` refusal.
- `SharpCorpusTests` — the vendored oils spec corpus, which upstream runs against `bash`, `dash`
  and `mksh` the same way. Every case that passes in-process **and** classifies `Owned` must also
  pass through the binary.
- `SharpRcFileTests` — the start-up files, with `HOME` pointed at a temp directory. Every other
  black-box run passes `--norc`, so a `~/.shshenv` on the machine running the suite cannot change
  what the tests measure.

The `Owned` filter keeps the comparison to cases whose behaviour cannot depend on what is installed
on the machine running the suite. A case naming `git` would otherwise measure the local `git`.

Everything a real shell does that has no meaning without processes — `&`, job control, `trap`,
`coproc`, process substitution — is refused by name and sent to your real shell rather than
emulated. Shell functions, `select`, `[[ … ]]`, `$$` and `$!` are not in that set: they need no
process model, and they run here.
