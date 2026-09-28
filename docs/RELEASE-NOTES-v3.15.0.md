# EncodingChecker v3.15.0

Plain ASCII text is now recognised as already being UTF-8, so repeating a conversion no
longer replaces the backup of a file's original. Seven smaller fixes tighten what EC
refuses, what it saves, and what the window tells you. This release changes conversion
policy and advances the conversion semantics version, so plans saved by any 3.14.x
release must be made again.

## ASCII is already UTF-8

ASCII bytes are identical in UTF-8 without a byte-order mark, but EC planned them as a
conversion. A UTF-16 file of English text converts to ASCII bytes, so running
`-Target utf-8 -Backup` a second time rewrote the file to the same bytes and replaced its
`.bak`, which held the UTF-16 original, with a copy of the converted file. The recovery
record describing the original went with it. The text was never at risk; the only copy of
the original bytes was. `-FailOnChanges` also reported a change on every run.

An ASCII file with a UTF-8 target, both without a BOM, is now **Unchanged**, with the
explanation "ASCII is already valid UTF-8 without a BOM." The whole file is still
validated, so a non-ASCII byte past the 64 KiB detection sample is reported as an error,
now before any backup is made. A `utf-8-bom` target still converts ASCII.

`-Validate` follows the same rule: `-Validate "utf-8"` accepts an ASCII file, and a list
allowing only `utf-8-bom` still rejects it.

## Other fixes

- **A source choice that contradicts reliable detection is refused even when it is also
  the target.** `-From windows-1252 -Target windows-1252` on a UTF-8 file used to report
  it as already windows-1252 with exit 0. It is now refused, exit 5, as any other
  contradicting choice is.
- **A source-choice warning stays with a file left unchanged.** Choosing a source for
  BOM-less UTF-16 or UTF-32 that EC cannot confirm is flagged whether or not the file then
  needs rewriting, in reports and in the review window.
- **A file that changes after its conversion was decided keeps its earlier backup.** The
  backup copy now checks the bytes it copies against the approved snapshot. Before, a file
  changed after a plan's stale check but before its own turn was correctly refused, yet
  its `.bak` had already been replaced with the changed bytes and its recovery record
  deleted.
- **Read-only and linked output files are refused, not replaced.** This now covers plans,
  journals, CLI reports, settings and the GUI's journal export, not only the GUI's text
  and CSV exports. The CLI checks `-Plan`, `-Journal` and `-Report` before any file
  changes and exits 3. A read-only or linked `Settings.xml` is no longer replaced, so
  preferences are not saved while it stays that way.
- **Cancelling a review undoes the source choices made in it.** Before, the next review
  showed those files as ready with a source nobody chose in it. Choices made before the
  review opened are kept.
- **The window's Validate status says how many files it checked.** It read "0 files do
  not have the correct encoding" both when every file passed and when no file matched.
  It now reads "No matching files were examined", "Checked N files: all valid", or the
  count that failed, with any files that could not be read.
- **Result rows show this run's state and reason.** A row left unchanged or skipped no
  longer keeps an earlier run's icon, and hovering a row shows why it ended as it did.

## Compatibility

**Conversion semantics move from 7 to 8. Plans written by v3.14.x or earlier are
refused.** Re-run `-Plan` and review the result. The plan schema stays at 6 and the journal
schema at 6, so other readers of these files are unaffected.

Scripts may notice:

- `-Target utf-8` reports ASCII files as `Unchanged` instead of `Converted`, and
  `-FailOnChanges` passes on them.
- `-Validate "utf-8"` passes ASCII files.
- Some explicit source choices that used to end as `Unchanged` (exit 0) or as a
  validation error (exit 3) are now refused (exit 5).
- A read-only or linked `-Plan`, `-Journal` or `-Report` file now stops the run with
  exit 3 before any file changes.
- The window's Validate status line has new wording.

The CSV and JSON formats are unchanged.

## Verification

- 980 tests pass, none skipped; release build with no warnings.
- `docs/Test-DefectBacklog.ps1` passes: 87 findings, 75 fixed, 8 open.
- The **Shared Unicode detector parity** check is unaffected: neither shared detector
  file changed.
- This release changes conversion policy, so the four-corpus audit is required. It runs
  against this exact commit, and its results are recorded in `SAFETY-AUDIT.md` after
  tagging.
- **The executables are not code-signed.** The signing secrets are not configured, so
  the release workflow skips signing. Shipping unsigned is a decision for this release,
  not a passed check.

## Known open issues

Recorded in `DEFECT-BACKLOG.md`: a preview's CSV report still calls files `Converted`
(BL-42), and a file name with an unpaired UTF-16 surrogate cannot be planned or backed up
(BL-43). Neither changes a file's bytes.
