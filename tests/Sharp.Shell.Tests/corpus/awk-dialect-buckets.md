# Where the two awk oracles disagree with each other

Every entry below was found by sweeping this applet against `/usr/bin/awk` (one-true-awk 20200816)
and `gawk --posix` (GNU Awk 5.4.1) — about 860 program shapes across a dozen input shapes and five
operand shapes, roughly 14 700 comparisons. In each of these the two references answer differently, so
there is no single "awk" to agree with and the case is excluded from `AwkDifferentialTests` by
construction rather than left failing.

This is a decision record, not a to-do list. Re-opening one of these means finding a POSIX argument
that neither implementation already makes.

---

## 1. A relational operator in an unparenthesised `print` list

    awk 'BEGIN { print 1 == 1 }'
    awk '{ print NR == 1 }'
    awk '{ print length($0) > 3 ? "long" : "short" }'

one-true-awk reports a syntax error and exits 2. `gawk --posix` treats the operator as a comparison
and prints 1 or 0.

**Ours follows gawk.** The POSIX grammar gives the output-redirection rule only `>`, `>>` and `|`;
every other relational operator stays an operator. A `>` in that position *is* a redirection here, as
it is in both oracles.

## 2. `FILENAME` while reading standard input

    printf 'x\n' | awk '{ print "[" FILENAME "]" }'

one-true-awk prints `[]`; gawk prints `[-]`.

**Ours follows one-true-awk**, which is the reference dialect. POSIX leaves `FILENAME` unspecified
when there are no file operands.

## 3. `FS = ""` set inside the program

    awk 'BEGIN { FS = "" } { print NF }'

one-true-awk splits the record into single characters; `gawk --posix` leaves it as one field.

**Ours follows gawk.** Note that this is *not* the same question as `-F ''`, where both oracles agree
on one field, nor as `split(s, a, "")`, where both agree on one element per character. All three
behaviours are implemented as the oracles have them.

## 4. `index(s, "")` — an empty needle

    awk 'BEGIN { print index("abc", "") }'

one-true-awk answers 0; gawk answers 1.

**Ours follows gawk.** An empty string occurs at position 1 of every string, including the empty one.

## 5. `\\` in a `sub`/`gsub` replacement

    awk 'BEGIN { s = "aXb"; gsub(/X/, "\\\\", s); print s }'

The replacement string here is the two characters `\\`. one-true-awk emits two backslashes; gawk
emits one.

**Ours follows gawk**, which is what POSIX describes: `\\` in the replacement stands for one literal
backslash.

## 6. An empty alternation branch

    awk 'BEGIN { print match("abc", /x|/) }'

one-true-awk rejects the pattern; gawk matches empty at position 1.

**Ours follows gawk.** POSIX ERE does not define an empty branch, so neither answer is wrong; ours
falls out of the translator without a special case.

## 7. `length` of text outside ASCII

    awk 'BEGIN { print length("héllo") }'

one-true-awk counts bytes (6); gawk counts characters (5).

**Ours counts UTF-16 code units (5)**, which matches gawk for everything inside the Basic
Multilingual Plane. Byte-exactness for arbitrary binary input is not claimed by this applet — the
same limit the other applets state.

## 8. `%d` of a value beyond a signed 64-bit integer

    awk 'BEGIN { printf "%d\n", 1e19 }'

one-true-awk clamps to `9223372036854775807`; gawk prints `10000000000000000000`.

**Ours clamps**, following one-true-awk.

## 9. `%x`, `%o` and `%u` of a negative value

    awk 'BEGIN { printf "%x\n", -1 }'

one-true-awk prints `0`; gawk prints `ffffffffffffffff`.

**Ours follows gawk** — the two's-complement 64-bit pattern, which is what C's `printf` does with an
`unsigned long`.

## 10. `%c` of an empty string

    awk 'BEGIN { printf "[%c]\n", "" }'

one-true-awk prints `[]`; gawk prints `[ ]`.

**Ours follows one-true-awk** and emits nothing.

## 11. `"inf"` and `"nan"` as numeric strings

    echo inf | awk '{ print $1 + 0 }'

Both oracles recognise them and then spell the result differently — `inf` versus `+inf`.

**Ours recognises neither**, so `$1 + 0` is 0. This is the POSIX-strict reading and the tie-breaker
the plan set out in advance. Every comparison involving such a field comes out the same either way;
only the arithmetic conversion differs.

## 12. `for (k in a)` iteration order

    awk '{ t[$1] = $2 } END { for (k in t) print k, t[k] }'

POSIX leaves the order unspecified, and the two oracles differ. This was the single disagreement the
whole sweep turned up that was not a dialect split, and it is not one either.

**No case in the differential suite depends on the order.** `EveryIteratingCaseIsOrderInsensitive`
enumerates the three cases that iterate at all and fails if a fourth appears.

---

## `-F t`

Not a disagreement to bucket but worth recording beside them: `awk -F t` sets the field separator to
a tab under one-true-awk and to the letter `t` under `gawk --posix`. **Ours follows one-true-awk**,
which is historical practice and the reference dialect. `-F '\t'` is a tab under both.
