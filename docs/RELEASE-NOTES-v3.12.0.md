# EncodingChecker v3.12.0

Twelve findings from an independent review, run against the source rather than
against the backlog: a target's spelling could rewrite an entire tree, two
claims were made without reading the file, and three failures were reported to
nobody.

## A file already in the target encoding is left alone

`ConversionPolicy` compared the detected charset's label against whatever the
caller typed. "utf-16", "unicode", "ucs-2" and "utf-16le" all name code page
1200, so every accepted alias but one failed the test: `-Target unicode` on a
tree already in UTF-16LE decoded, re-encoded, verified and reinstalled every
file to produce **identical bytes**.

Nothing was corrupted. What was lost was every modification time — and with
`-Backup`, a `.bak` and an `.ecmeta.json` appeared beside each unchanged file.
Under `-FailOnChanges` the same clean tree exits 0 for `-Target utf-16` and 2
for `-Target unicode`; on BOM-less UTF-16 the alias reaches the ambiguity guard
and exits 5, so the spelling alone turned a clean run into a refusal.

Identity is now the resolved code page. A zero code page means the label did not
resolve, so it proves nothing and never matches.

## "Already in the target encoding" is a whole-file claim again

Detection reads at most 64 KiB. When the source codec matched the target,
nothing read any further — so a file clean for 64 KiB and invalid afterwards was
reported `Unchanged`, and whether EC noticed depended only on which target was
named. The same corrupt file was `Error` under `-Target utf-16` and `Unchanged`
under `-Target utf-8`. `-Validate` always read the whole file; Convert never
reached that check once it had decided it had nothing to do.

Reading the rest costs almost nothing, and not for the reason expected: Convert
already reads every byte, because the source snapshot hashes the whole stream
before anything is decided. Measured at 0.04-0.10 ms per MiB.

## A preview no longer promises a conversion that would fail

`-Plan` sets `WhatIf`, and conversion returned at the `whatIf` branch before the
converter ran, so nothing decoded the file. A plan therefore recorded
`Action=Convert`, with no reason, for a source that cannot be read; the run
exited 0 and showed the reviewer nothing. The failure surfaced at `-Apply`,
after approval and part-way through the batch.

A staleness check cannot substitute. It proves the bytes have not changed since
review and cannot prove they are readable, because that needs a decode nobody
performed. The entry is now marked `Refuse` rather than merely given an error,
because a plan records the *action*.

Decode only. A target that cannot represent the text still fails at conversion
time, which reading the source cannot predict, and a test named for that case
pins the limit.

## One file's failure is one row, whatever it threw

The per-item catch named four exception types. Anything else — a
`SecurityException` from an ACL the enumerator did not surface, a regex timeout,
a defect in EC itself — escaped `Parallel.ForEach` as an `AggregateException`
and took every file the run had not yet reached with it. The CLI's outer catch
names the same four, so it would have surfaced as a crash rather than exit 3.

It now catches everything except cancellation and `OutOfMemoryException`, since
carrying on after the latter would be pretending to process rather than
processing.

## A folder EC could not read leaves a trace

An unreadable *file* becomes a row with `ScanFailed` and drives exit 3. An
unreadable *directory* produced a warning on stderr and nothing else: no row, no
counter, exit 0 — and nothing whatever in the window, which passes no warning
callback.

Measured against a deny ACE: `-Validate -FailOnChanges` over a tree with one
denied folder reported "1 file(s) processed" and exited 0, and a scan whose
entire base directory was unreadable printed a header-only CSV and exited 0. A
run that examined none of the tree could report success.

Unreadable directories are now counted, separately from the two exclusion
counters, which record folders EC *chose* not to enter. The exit code is
deliberately unchanged, and the documentation now says what that means for a
script.

## Folders skipped by name are counted

Twelve directory names are skipped deliberately and that is documented, but
unlike attribute-excluded folders they incremented no counter. A scan of a tree
whose only content sat under `build/` reported one file, zero exclusions and no
warning, while `docs/CLI.md` promised that EC reports how many files each
exclusion skipped.

They are counted apart from the attribute exclusions, whose message says
"(hidden, system, or reparse point)" and would become untrue if the two were
merged. What is scanned is unchanged.

## Every `-Validate` rejection carries a reason

There were four ways to return `Invalid` and two of them were explained. A
charset outside the allowed list and a file EC could not identify both arrived
as a bare `Invalid` with an empty reason, though they are not the same
situation: one means widen the list or convert the file, the other means EC
could not tell what the file is.

`CharsetNotAllowed` is new. `UnknownEncoding` is reused deliberately, because
two names for one condition depending on which mode ran would be its own defect.
This was the only outcome left in the product where the reader had to re-derive
a reason the producer already knew.

## A refusal's reason comes from the decision that made it

The conversion path worked the reason code out again from the four raw facts the
policy had already reduced to a single interpretation. The two copies were
textually identical and their operands never changed between them, so they could
not disagree — but nothing tied them together, and a fourth refusal reason added
to the policy would have fallen through to `LegacySourceRequired` at the call
site: a correct refusal carrying the wrong explanation, with nothing to fail.

It is now `ConversionPolicy.ReasonCodeFor`, verified equivalent across all 256
reachable combinations of the policy's inputs, with a test that fails if any
refusal ever produces no reason.

## A decode failure names the bytes, not a position no file has

`DecoderFallbackException.Index` is relative to the decoder call, not to the
file, and goes negative when the bad sequence began in bytes carried over from
the previous call. A UTF-8 file ending in a truncated three-byte sequence
produced "offset -2 within the failing read chunk" — a message naming a frame it
did not describe.

The offending bytes are reported instead, which mean the same thing wherever the
failure happened.

## Standard output is encoded for whoever is going to read it

After attaching to the parent console, both writers were rebuilt with
`StreamWriter`'s default encoding, which is UTF-8 whatever the console is. On
the machine this was found on, `Console.OutputEncoding` is `ibm437`, and the
per-file CSV rendered "Grüße aus München" as "Gr├╝├ƒe aus M├╝nchen" — the tool
producing in its own output the failure it exists to detect.

A redirected stream stays UTF-8, matching the `-Report` file apart from its BOM.
A console gets its own encoding, so characters it cannot represent become "?",
which is visibly lossy rather than quietly wrong. No global console state is
mutated.

## The documented parallelism default is the one the code uses

v3.11.2 raised the cap from `min(CPU, 4)` to `min(CPU, 8)` and left both
statements of it saying 4: the built-in help and `docs/CLI.md`, which are the
two places someone tuning `-MaxParallelism` against a slow share would look. The
cause was an unnamed literal, with no identity a document could be checked
against; it is now `ScanEngine.MaxParallelismCap`. A test finds the one line in
each document that states the default, extracts every run of digits from it, and
asserts the set equals the cap, so a stale number cannot hide beside a fresh one.

## Documentation

`<file>.bak` is a fixed name holding the version the most recent run replaced,
so converting the same file twice replaces it. That is deliberate and has been
pinned by a test since the suite's first commit, but no document said so, and a
user converting twice lost the original with nothing having warned them.
[`docs/SAFETY.md`](SAFETY.md) now says it.

[`docs/DEFECT-BACKLOG.md`](DEFECT-BACKLOG.md) records every finding of this
review: the twelve fixed here, one withdrawn once its fix was shown to break
four existing tests, and four left open — including a BOM-less UTF-16 file that
can be detected as UTF-32 and converted silently, which output verification
cannot catch because both sides of the comparison use the same wrong codec. It
also records EC-08 — an include pattern that can hang a scan — as reproduced
against inputs built for it, and why the first attempt to reproduce it missed.

## Compatibility

Conversion semantics stay at **6**, the plan schema at **5**, the journal schema
at **4**. A plan written by an earlier build still means what it meant: these
changes make EC refuse more and convert less, which the approved-decision
ceiling already permits.

Exit codes and report contents do change, in both directions:

| Situation | Before | After |
|---|---|---|
| `-FailOnChanges` on a tree already in an alias of the target | 2 | 0 |
| A file valid for 64 KiB and invalid afterwards | `Unchanged`, 0 | `Error`, 3 |
| `-Plan` over a source that cannot be decoded | `Convert`, 0 | `Refuse`, 3 |
| An unexpected exception during a scan | run-ending crash | one row, 3 |
| A `-Validate` row outside the allowed list | empty reason | `CharsetNotAllowed` |

Coverage output gains two lines, for folders skipped by name and folders that
could not be read. Standard output written to a console now uses that console's
encoding rather than UTF-8; redirected output is unchanged.

## Verification

- 727 tests pass, none skipped; release build with no warnings
- The nine-phase GUI smoke suite gates the release, as it has since v3.11.2
- Every fix was mutation-checked: the change reverted, the intended test
  required to fail, the file restored byte-identical and confirmed by hash
- **No four-corpus audit was run, and this release is one that asks for one.**
  The checklist requires a corpus run for a release changing detection or
  conversion policy. Detection is untouched — no detector file changed — but
  `ConversionPolicy` is not, so the exemption v3.11.2 claimed is unavailable
  here. What supports this release is the unit suite, the GUI suite, the
  detector parity check, and a mutation check on each fix.
