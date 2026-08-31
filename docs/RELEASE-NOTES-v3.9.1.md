# EncodingChecker v3.9.1

This patch release hardens saved conversion plans, recovery reporting, and malformed-input handling introduced in v3.9.0.

## Fixes

- Saved plans now validate every listed file, path, hash, and codec before any conversion begins. A malformed or stale row invalidates the whole plan, including rows marked to skip or refuse.
- Unreadable files remain visible as explicit refusals instead of preventing plan creation.
- Unsupported runtime codecs are reported cleanly throughout planning and journaling instead of causing an unexpected exception.
- Files with repeated leading byte-order marks are refused before EC creates a backup or attempts conversion, with a clear diagnostic.
- Conversion journals now record a backup whenever this run created one, even when a later conversion step failed. Recovery-metadata paths are recorded separately.
- Journal construction is defensive against malformed paths and unsupported recorded codec names.

## Interface and maintenance

- Clarified conversion review and export wording.
- Simplified conversion-policy flow and improved test documentation.
- Added focused regression coverage for saved-plan validation, repeated BOMs, unreadable files, unsupported codecs, and backup/journal reconciliation.

The strict streaming converter, atomic source-file installation, and the v3.9 legacy-source safety policy are unchanged.
