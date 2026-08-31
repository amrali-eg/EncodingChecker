# EncodingChecker v3.9.2

This patch closes cases where a run or recovery record could describe more than EC had
actually established. It also improves scan-coverage reporting without changing which
files EC is willing to convert.

## Safety fixes

- Recovery sidecars now distinguish `Prepared` from `Completed` installation and record
  the verified output file's SHA-256. If installation fails or EC stops after preparation,
  the sidecar remains truthful: its original and expected-output hashes identify which
  state the current file is in. Sidecar updates use a verified temporary file so a failed
  update does not overwrite the last valid record.
- A `-Include` value that contains no usable pattern is rejected instead of silently
  meaning *every file*. `-Include ""`, `-Include ",,,"` and similar values previously
  widened a scan to the whole folder. `-Exclude` is validated the same way. Omitting either
  option keeps its previous meaning.
- The repeated byte-order-mark probe now requires its complete prefix, rather than treating
  a short stream read as proof that no repeated mark exists.
- When an explicit source differs from a BOM-less UTF-16/32 estimate, EC still follows the
  user's source choice but now records and displays a clear warning. A BOM-confirmed UTF
  conflict remains a refusal.
- Saved plans now preserve automatic-detection provenance, including BOM state. This changes
  the plan schema to version 4; regenerate plans created by an earlier release before applying
  them with v3.9.2.

## Scan coverage

- Matching files skipped for hidden, system, or reparse-point attributes are counted after
  include, exclude, output, backup, sidecar, and temporary-file exclusions are applied.
- Hidden, system, and reparse-point folders are counted separately and are not entered.
  EC does not claim to know how many matching files are inside an unexamined folder.
- The GUI includes these counts in its completion status. The CLI writes them to standard
  error so they remain visible with `-Quiet` without entering CSV output.
- Coverage notices are informational and do not change the exit code. Scripts that require
  complete coverage should inspect standard error; no new CLI policy switch is introduced
  in this patch.

## Usability

- Closing the main window a second time can end a run that has not responded to cooperative
  cancellation, after confirmation. The warning now states accurately that completed files
  remain converted and that a file being installed may need inspection afterward.
- The command-line help and README explain which excluded items are counted and where those
  counts appear.

## Unchanged

Detection, strict streaming conversion, exact text verification, backup verification,
the v3.9 legacy-source rule, and normal exit-code meanings are unchanged.
