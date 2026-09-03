# EncodingChecker v3.11.0

This release makes saved plans binding, improves failure reporting, and closes
several cases where the command line or journal could describe a safer outcome
than actually occurred.

## Important compatibility changes

- The plan schema changes from 4 to 5 and conversion semantics from 5 to 6.
  Plans created by v3.10.1 or earlier are refused; create them again with
  v3.11.0. Older plans do not contain every decision needed by this build.
- `-Validate` now reports BOM-less UTF-16 with an unprovable byte order as
  `Invalid`. With `-FailOnChanges`, this returns exit code 2.
- `-Plan` now returns exit code 3 if any selected file could not be read. The
  plan is still written so the failed file remains visible.
- Blank values for `-Plan`, `-Apply`, `-From`, `-Journal`, `-Report`,
  `-Include`, `-Exclude`, and `-Validate` are rejected with exit code 1.
- `-Plan`, `-Journal`, and `-Report` cannot use EC's reserved `.bak`,
  `.ecmeta.json`, or `.unicodechecker.tmp` suffixes. This prevents command
  output from replacing a backup or recovery artifact, including through a
  Windows path alias with trailing spaces or periods.
- `-DetectOnly` with `-Target`, `-WhatIf`, or `-Backup`; `-Validate` with
  `-WhatIf` or `-Backup`; and `-Quiet` with `-Verbose` are now rejected instead
  of silently ignoring an option.
- Journal readers must accept `ConvertedWithWarning`, `InstallationUnknown`,
  and `NotAttempted`.

## Plan safety

The main fix prevents an applied plan from broadening a reviewed decision. A
file recorded as `Refuse`, `Skip`, or `Unchanged` cannot later become
`Convert` merely because the policy is evaluated again. Revalidation may only
make a saved decision stricter.

This closes a real failure in which `-Apply` converted a file that its plan had
recorded as `Refuse`, exited successfully, and produced mojibake that ordinary
output round-trip verification could not recognize.

Plan application also now:

- preserves strict full-file Unicode validation needed by source-conflict checks;
- rejects a root or source path redirected through a reparse point;
- reports deleted files as missing rather than as possible links;
- binds each write to the approved source hash immediately before installation;
- keeps unreadable, non-writing entries visible without requiring a false hash;
- rejects plan, report, and journal path collisions, including a journal path
  that names a planned source file.

## Honest results and journals

- Errors are written to stderr even with `-Quiet`.
- Scan failures remain processing errors from planning through `-Apply`.
- Backup failures are recorded as `Failed`, not `InstallationUnknown`.
- Post-install failures are recorded as `ConvertedWithWarning`; genuinely
  uncertain installation is recorded as `InstallationUnknown`.
- Interrupted GUI runs keep a journal. Completion tracking is thread-safe, and
  files the write pass never reached are recorded as `NotAttempted`.
- Retrying an interrupted or failed row clears the earlier attempt's status,
  backup path, recovery-record path, and output hash before recording the retry.
- Every parallel conversion error updates the original result entry, keeping
  GUI rows, CSV reports, plans, and journals consistent.
- EC's selected backup, recovery-record, and temporary files are counted as
  excluded coverage rather than disappearing from the result.

## Command-line and GUI improvements

- Invalid option combinations and empty filters fail early with actionable text.
- The GUI counts skipped files separately from unchanged files.
- Completed and interrupted GUI summaries use the journal's terminal outcomes,
  so every selected file is counted exactly once.
- Saved window bounds are restored only when part of the title bar is reachable
  on a currently attached monitor.
- A well-formed but damaged settings file with null values falls back to usable
  defaults instead of preventing the GUI from opening.
- The BOM-less Unicode advisory uses the same plain explanation in read-only,
  review, and refusal surfaces.

## Limits

- Installation remains per file, not transactional across a whole batch.
- A final hash check narrows but cannot eliminate every concurrent-write race.
- A command-line Ctrl+C returns exit code 4, but a complete interruption journal
  is currently guaranteed only by the GUI orchestration path.
- Plans, journals, and reports from earlier commands are ordinary files. Keep
  them outside the directory being scanned if they must not be processed.

## Verification before release

The release gate is a warning-free Release build, the complete test suite with
no skipped tests, shared detector parity, the manual GUI smoke test, and the
four-corpus regression audit where the changed policy is relevant.
