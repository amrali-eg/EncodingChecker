# EncodingChecker v3.14.5

Three fixes to what EC records and writes around a conversion. What EC detects,
converts and refuses is unchanged; the fixes are in how a file that could not be
processed is planned, how a failed snapshot is recorded, and how the GUI saves reports.

## What changed

- **A file that fails full validation is planned as a refusal.** A file that looked
  like the target codec in the 64 KiB detection sample but held invalid bytes later was
  reported as an error, yet its planned action stayed `Unchanged`. The plan summary and
  the confirmation dialog counted it under "Already in the target encoding" and said "No
  conversion is needed". It is now planned as `Refuse`, so it appears under "Cannot be
  processed safely". Nothing about writing changed: the file was never converted, the CLI
  already exited 3, and applying the plan still re-reads the file, reports the failure
  with its reason and leaves the bytes untouched.
- **The GUI text and CSV exports no longer erase an existing report when the write
  fails.** They opened the chosen file directly, which truncates it first. They now stage
  the report in a temporary file and install it only when complete, as the CLI reports,
  journals, plans and settings already did. A read-only report, or a link, is refused
  with a message rather than replaced.
- **A failed snapshot no longer keeps an earlier run's hash.** GUI rows survive between
  runs. A row whose source could not be read when a plan was prepared was refused as an
  error but kept the hash and size from its previous snapshot, which the plan and the
  journal could then record as if they described this attempt. Both are now cleared.
  A stale hash would have failed the check at apply, so the effect was a wrong recorded
  value, not an unsafe write.
- Documentation and comments only: `SAFETY.md` now says that a failure to mark the
  recovery record complete happens after installation and leaves the verified output in
  place, and names `utf-32le`/`utf-32be` for BOM-less UTF-32; two code comments that
  overstated what an unchanged file and a missing detected codec mean were corrected.

## Compatibility

Conversion semantics stay at **7**, the plan schema at **6**, the journal schema at
**6**. Plans and journals from any 3.14.x release still apply unchanged.

One difference to expect: a plan written by an earlier 3.14.x release for a file of the
first kind records `Unchanged`, so its confirmation dialog still lists that file as
already in the target encoding. Applying it still fails that file safely. Plans written by
this release record `Refuse`, and the journal's planned action for such a file is now
`Refuse` as well.

## Verification

- 933 tests pass, none skipped; release build with no warnings
- The GUI smoke suite passes all ten phases against an ordinary Release build of this
  release commit (report `Outcome` Passed, no evidence errors)
- `docs/Test-DefectBacklog.ps1` passes
- The **Shared Unicode detector parity** check is unaffected: neither shared detector file
  changed
- The four-corpus audit was **not** run. It applies to detection and conversion-policy
  changes; `ConversionPolicy` decides exactly as before, and the only behavioural changes
  are the planned label of a file that failed validation and the export and snapshot
  handling above.
