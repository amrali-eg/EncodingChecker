# EncodingChecker v3.9.2

This patch closes two gaps where a run could report a result it had not established, and
hardens two smaller paths found in the same review.

## Fixes

- A `-Include` value that contains no usable pattern is now rejected instead of silently
  meaning *every file*. `-Include ""`, `-Include ",,,"` and similar previously widened a
  scan to the whole folder — the opposite of what the caller asked for, and dangerous when
  the value came from an unset variable in a script. `-Exclude` is validated the same way.
  Omitting either option still means "every file", unchanged.
- Files skipped for being hidden, system, or reparse points are now counted and reported.
  They were dropped inside the operating system's own enumeration, so they produced no row,
  no count and no note: a folder containing one validated clean and exited `0`, and a
  conversion reported `Selected: 1` for two files. What gets skipped is unchanged; the
  count is what makes a clean result distinguishable from files EC never opened. It is
  written to standard error so it survives `-Quiet` and stays out of the CSV report.
- The repeated byte-order-mark probe now requires its full prefix, rather than treating a
  short read as "no repeated mark".
- Closing the main window a second time during a run that has not stopped now closes it,
  after confirmation. Cancellation is cooperative, so a run blocked on unresponsive storage
  previously left the window impossible to close.

## Documentation

- The help text and README now state that hidden files, system files, and reparse points are
  not examined. This was previously recorded only in the v3.9.0 release notes, not in the two
  places a reader looks.

## Unchanged

Detection, the strict streaming converter, output verification, atomic installation, backup
handling, and the v3.9 legacy-source policy are untouched. No conversion behaves differently.
