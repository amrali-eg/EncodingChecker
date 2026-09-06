# EncodingChecker v3.11.2

Two silent failures made loud, a measured speedup, and the first backlog this
project has kept.

## A plan rooted at a drive can be applied

`-BasePath D:\` produced a normal-looking plan that `-Apply` then refused
entirely, reporting every file as resolving outside the plan's own directory —
blaming the paths rather than the root. The containment check appended a
separator to a root that already ended in one, so the prefix became `D:\\` and
nothing could ever match it.

This shipped broken in **v3.11.0 and v3.11.1**. It was found before v3.11.0,
recorded, reported as closed, and rediscovered from scratch during an unrelated
review. That is why this release also adds a backlog.

## A source choice that cannot be applied is refused, not dropped

In the review dialog, each refused row carries the path resolved for it. Rows
that resolved to nothing were filtered out of the ticked set in silence, so the
confirm button did nothing and the run reported **"Conversion cancelled. No
files were modified."** — a cancellation nobody asked for, after the user had
ticked files and chosen an encoding.

The drive-root defect was one way in. It is not the only one: the results list
is cleared when a scan *starts* and never when the directory box changes, and
that box accepts typing, a recent entry, or a dragged folder. Scan one folder,
point the box at another, and every row in the next review resolves outside the
plan's root.

The review now says which ticked files it cannot act on and what to do about it,
and stays open. The *Proceed* path already handled this correctly; only the
source-choice path failed silently.

## Conversion is faster

The parallelism cap was `min(ProcessorCount, 4)`. Conversion is bound by
per-file I/O latency rather than CPU, so the cap bit well before core count did.
It is now `min(ProcessorCount, 8)`.

Measured over 2,000 files:

| | Before (4) | After (8) |
|---|---|---|
| Without backups | 4,207 ms | 2,511 ms |
| With backups | 10,516 ms | 7,305 ms |

Past 8 the curve flattens and backup runs stop improving, which is why 8 rather
than something larger. `ProcessorCount` still binds first on small machines, and
`-MaxParallelism` still overrides.

## Recovery files are read with the options they were written with

The plan and sidecar stores passed a configured options object when writing and
none when reading. Nothing observable changes today — both settings affect
writing only — but a setting added later for the writer would have altered every
file EC produces without altering what EC accepts, and each one would have
stopped loading silently.

## A defect backlog

[`docs/DEFECT-BACKLOG.md`](DEFECT-BACKLOG.md) records the status of all
thirty-five findings from the two independent reviews that preceded v3.11.0,
re-derived from the source rather than carried over from a summary, plus
everything found since. Open items are scored on two axes — what a user loses,
and how easily it happens — because one number hides the difference between a
critical impact nobody can trigger and a low impact everyone trips over.

It also records three hashing optimisations that were built, measured and
rejected, so the next person does not have to re-derive them.

## Release engineering

- The GUI smoke suite now **gates the release**. All nine phases run against the
  signed, published executable, after signing and before packaging, and a
  failure stops publication.
- The release workflow can be **rehearsed without publishing**. A manual run does
  everything a release does except create the release. Publishing requires a tag
  push, not merely a tag reference, so a run started by hand cannot publish
  whatever reference it was given.

## Compatibility

No conversion or classification behaviour changes. Conversion semantics stay at
**6**, the plan schema at **5**, the journal schema at **4**, and exit codes are
unchanged.

The drive-root fix does change what happens when such a plan is applied — it
converts where it previously refused — but the decisions a plan records mean
exactly what they meant before. Only the resolution of the paths was wrong, so
the semantics version is deliberately left alone.

## Verification

- 646 tests pass, none skipped; release build with no warnings
- The nine-phase GUI smoke suite passes
- Each fix was mutation-checked — the change reverted, the intended test required
  to fail, the file restored byte-identical — **except** the options-object
  change, which no test can demonstrate without adding a setting to production
  code purely to make one fail. That is stated rather than papered over.
- No four-corpus audit was run. The checklist requires one for a release that
  changes detection or conversion policy; this changes neither.
