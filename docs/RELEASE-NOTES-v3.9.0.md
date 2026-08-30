# EncodingChecker v3.9.0

## Safer source interpretation

- **C1:** An explicit legacy source remains useful for resolving ambiguous text, but
  EC now refuses it when it contradicts a reliable full-file Unicode reading or a
  BOM-confirmed UTF-16/UTF-32 encoding. Refusals use the stable
  `ExplicitSourceConflictsWithDetection` reason code.
- Explicit legacy choices that merely differ from a legacy detector result remain
  allowed through the strict conversion pipeline. Journal schema 3 records both
  canonical code pages and whether the explicit choice differed from detection.
- **M1:** `-Validate` now strictly validates the complete file rather than only the
  detector sample, including malformed or truncated trailing sequences.
- **H1:** Unknown or unidentified files are represented as skipped work in saved plans
  instead of causing plan creation to fail.
- UTF-7 supplied to conversion through `-From` or `-Target` is now reported as
  unsupported with usage exit code `1`, rather than escaping CLI validation as an
  unhandled .NET `NotSupportedException`. Label-only `-Validate utf-7` remains valid.
- The main encoding selectors and legacy-source review now share one runtime-resolved,
  canonical codec list. EC no longer offers aliases or charset names that the current
  .NET runtime cannot construct and would subsequently refuse.

## Clearer command-line outcomes and reports

- Exit code `5` now means conversion was safely refused. It is distinct from invalid
  command-line usage (`1`) and processing failure (`3`), so scripts can respond
  appropriately.
- CSV conversion reports are explicitly written as UTF-8 with a BOM for predictable
  opening in Excel and other spreadsheet applications.
- JSON conversion journals distinguish what EC detected from the source codec actually
  selected and record stable reason codes and terminal outcomes.

## Recovery and filesystem hardening

- With backups enabled, EC now refuses installation when either the source or backup
  SHA-256 cannot be obtained, or when the two hashes differ. Recovery-record failures
  use the stable `RecoveryRecordError` code and leave the original unchanged.
- Directory scans skip hidden files, system files, and reparse points. EC also excludes
  its own plans, journals, reports, backups, sidecars, and temporary outputs.
- Strict-codec construction and sample-validation behavior are kept in parity across
  EncodingChecker, LineEndingNormalizer, and CorpusTesters.
