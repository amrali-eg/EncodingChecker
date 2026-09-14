# EncodingChecker v3.14.2

Five fixes to what EC reports when a run is cancelled or interrupted. None change what
EC detects, converts, or refuses — every fix here is about the record left behind
afterward being accurate.

## An interrupted run no longer claims to have done work it didn't

The GUI's confirmation dialog runs a "decide" pass before you ever click Convert, and
that pass marks eligible files `Converted` — the label it uses for "would convert," so a
preview can show you the plan. If you then cancelled the review, hit a stale plan (a file
changed underneath it), or the plan couldn't be built at all, that label was never
corrected. Exporting the CSV afterward could report those same untouched files as
`Converted`.

The gap was wider than the confirmation dialog: cancelling while the decide pass itself
was still running — a plain Ctrl+C on a large folder, before you're even asked anything —
escaped uncaught and left the same stale label behind.

Every one of these paths now marks the affected rows `NotAttempted` before returning,
reusing the marker already used for a write pass stopped mid-batch. `Run()` wraps the
whole decide/confirm/write sequence in one cancellation handler for exactly this reason.

The same class of bug existed on the command line. An interrupted `-Apply` or direct
scan now marks every row the write pass never reached as `NotAttempted` in both the
console summary and the exported CSV, instead of `-Apply` calling them "unchanged" and a
direct scan's CSV/journal having no way to tell an interrupted run from a complete one.
Journal schema **6** adds an explicit `Interrupted` flag for this. A direct scan's journal
still only covers the files it reached — a direct scan was never a promise to cover the
whole folder, so there's no "remainder" to mark.

A cancelled scan also used to skip writing its CSV report entirely, discarding evidence
for files it had already scanned; it now saves the partial report before returning. A
cancelled preflight (the pass that decides what a `-Plan` would contain) leaves any
existing plan file untouched and says so on stderr, rather than silently producing
nothing.

**BL-31, BL-32, BL-33** in the defect ledger record these.

## The CSV export now names the source that was actually used

You can override automatic detection for a file EC refused. Conversion and the journal
already used your chosen encoding; the CSV kept printing the encoding EC originally
detected. The file converted correctly — the exported record of what it was converted
*from* was wrong. The CSV writer now uses the source label captured when the file was
actually read, for both the GUI export and the CLI `-Report`.

A refused row is the deliberate exception: it still reports the originally detected
source, because nothing was converted and the diagnostic on that row refers to detection,
not to a chosen-but-rejected encoding.

## Cancelling a direct scan and `-Apply` now agree on exit codes

An interrupted direct scan returned exit code 4 unconditionally, even when a file it had
already reached failed to convert before Ctrl+C landed — while an interrupted `-Apply` in
the same situation returned 3. Processing failure now wins over cancellation on both
cancellable paths, matching the documented precedence in `docs/CLI.md`, which was itself
missing the sentence saying so until this release.

## What else is in this release, and why you will not notice it

- **A cancellation timeout no longer drops the last refusal.** If a "would you like to
  cancel?" prompt timed out right after a button press, the real refusal was replaced
  with a literal empty value, misreporting a refused press as no button having been
  found at all. This only affects the GUI smoke suite's own diagnosis of a stuck run;
  `EncodingChecker.exe` is unaffected.
- **The smoke suite's own instrumentation got three narrow fixes**: a transient
  automation failure now leaves a status flag unknown instead of forcing it false,
  matching how its siblings behave; scoring an empty phase list is now `Inconclusive`
  instead of vacuously `Passed`; and the suite's own CI-gate self-test now launches
  `pwsh`, matching what CI actually runs. **EC-32** records the timeout fix.
- **A regression-test hook was added to the converter** (`BeforeVerifyTemporaryOutput`)
  so a test can simulate storage corruption at the one point that matters — after writing
  finishes, before verification and installation — without needing a real disk fault.

## Compatibility

**The journal schema moves from 5 to 6.** The only change is the additive `Interrupted`
flag; EC never re-reads its own journals, so nothing rejects an older one — this is
informational, for anyone else's tooling that parses them. The plan schema stays at
**6**, conversion semantics at **7**. A saved plan from any 3.14.x release still applies
unchanged.

The one behavior change with a visible before/after:

| Situation | Before | After |
|---|---|---|
| Direct scan cancelled, with a reached file failure | exit 4 | exit 3 |
| GUI cancel/stale-plan/mid-decide cancellation, then CSV export | row: `Converted` | row: `NotAttempted` |
| CLI `-Apply`/direct scan interrupted, then report/journal | unreached rows: `Unchanged` or absent | unreached rows: `NotAttempted` |
| CSV source column for an explicit-source conversion | detected encoding | encoding actually used |

Every reason code, report column, and exit code keeps its existing meaning; detection and
conversion behave exactly as they did in v3.14.1.

## Verification

- 826 tests pass, none skipped (was 756); release build with no warnings
- The GUI smoke suite passes all ten phases against an ordinary Release build
- Every regression test added for the cancellation-reporting fixes was mutation-checked:
  the fix was reverted, the new assertion confirmed to fail with the exact defect
  described, then the fix restored and the test confirmed passing again
- `docs/Test-DefectBacklog.ps1` passes at **77 findings — 66 fixed, 7 open**
- **No four-corpus audit was run.** This release changes no detection or conversion
  policy, so the checklist does not require one
