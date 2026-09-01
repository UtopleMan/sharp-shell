# Vendored: oils-for-unix spec corpus

Source: https://github.com/oils-for-unix/oils
Commit: 15de8fd779569e6e3a9f5fcbfc00e7df0ebe0380
Vendored: 2026-08-31
Licence: Apache-2.0 (see LICENSE.txt beside this file). Upstream ships no NOTICE file.

Copied verbatim and **wholesale** — all 222 `spec/*.test.sh` plus `spec/testdata/`, including the
`ysh-*` files this project never runs. Filtering at copy time would create a curation step to
repeat on every update; copying wholesale makes a re-sync a plain directory copy and leaves
selection to the `## compare_shells:` header, which upstream keeps correct.

**Do not edit these files.** Our deviations live in `../expected-failures.txt`, keyed by case id,
so an upstream re-sync is never a merge conflict.

`spec/bin/` is deliberately **not** vendored: its helpers carry `#!/usr/bin/env python2`
shebangs. They are trivial, so the test-only `SpecHelpers` implements them in C# instead and the
suite stays hermetic and python-free.

This corpus is test-only. It is not referenced by anything under `src/` and does not ship with
the binary.

## Re-syncing

    git clone --depth 1 --filter=blob:none --sparse https://github.com/oils-for-unix/oils.git
    cd oils && git sparse-checkout set --skip-checks spec LICENSE.txt
    cp spec/*.test.sh   <repo>/tests/Sharp.Shell.Tests/corpus/oils/spec/
    cp -R spec/testdata <repo>/tests/Sharp.Shell.Tests/corpus/oils/spec/
    cp LICENSE.txt      <repo>/tests/Sharp.Shell.Tests/corpus/oils/

Then update the commit above and re-run the conformance suite; the ratchet reports what moved.
