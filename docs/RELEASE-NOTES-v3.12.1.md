# EncodingChecker v3.12.1

Two defects from an independent review of the released v3.12.0, one refactor taken
because the inconsistency it describes was the finding, and two rows of the backlog that
did not survive being re-derived from the source.

## An unwritable output destination is refused before the run, not after

The journal and the report are written once scanning has returned. A destination that
could never be written was therefore discovered *after* conversion had rewritten the
files — so a user who asked for a journal ended with changed files and no record of the
change, which is the one outcome a journal exists to prevent.

Measured on all four shapes of the mistake: `-Journal` or `-Report`, under a missing
directory or onto an existing one. All four converted first and failed afterwards.

The check now runs in `RunConsoleMode`, after option validation and before either mode
dispatches, so one place covers `-Apply` and every scan mode.

**The exit code is deliberately unchanged.** `docs/CLI.md` assigns 3 to a report failure,
and the test that pins it states the reasoning: a report that cannot be written is a
processing failure, not a usage error. Putting the check among the usage validators made
it exit 1 and broke that contract for no benefit. The defect is *when* the failure is
found, not what it is called.

It does not probe by creating a file. That would leave one behind on every path that then
fails, and it still could not promise the later write — the disk can fill and permissions
can change in between. It removes the two mistakes a caller can make before the run
starts, which is what the four cases were.

## A plan action no build ever wrote is refused, not reported as converted

`System.Text.Json` accepts a number for any enum, so a damaged or hand-edited plan could
carry `"Action": 99` and load without complaint. It then reached the mapping from planned
action to report result, whose fallback arm was `Converted`.

Measured before the fix: `-Apply` exited **0**, printed "1 selected, 1 converted, 0
failed", and wrote a journal recording `Status=Converted` and `PlannedAction=99` — while
the source file's hash was unchanged. The journal asserted work that never happened, which
is worse than a missing journal, because this project treats the journal as its audit
trail.

Two changes, because either alone leaves the other half reachable. Loading a plan now
rejects an undefined action or source interpretation before any source is touched. The
mapping names every action and throws for anything else: a value that cannot arise from a
decision this build made has no report result, and guessing `Converted` is the guess that
makes the report lie.

## EC writes its own artifacts the way it writes everyone else's

EC installs a converted file by writing a temporary file beside it and replacing the
original, and it writes its recovery sidecar the same way, with a read-back check. Its
plan, journal, report and settings did not: each truncated its destination and then wrote
into it, so an interruption left a half-written artifact where a readable one had been.

The inconsistency is the finding. EC had already decided this matters and had already
built the mechanism; four of its own artifacts did not use it.

The plan is the worst case — a truncated plan destroys the reviewed plan a user was about
to apply, and re-running `-Plan` produces one nobody has reviewed. The settings file is
the case already known to have failed this way, recorded as EC-16 and closed by this
change rather than on its own account.

All four now write through one shared writer, into a temporary file beside the destination
under the suffix scans already exclude, and then through the existing replacement. The
recovery sidecar keeps its own writer: it also reads back and verifies what it wrote,
which is more than the shared one does and should not be reduced to it.

The bytes are unchanged. A report produced by this build is byte-identical to one produced
by v3.12.0, BOM included.

## Two backlog rows corrected

`CX-07` was recorded as fixed, with a note describing a fix that was never made and should
not be: `docs/CLI.md` states plainly that EC does not exclude plans, journals or reports
left by earlier runs, and that is deliberate. What is excluded is `.bak`, `.ecmeta.json`,
temporary conversion files, and the output paths of the running command. This is the same
failure as the drive-root defect — a record trusting a summary instead of the source — so
the row now says the record was wrong rather than quietly restating the behaviour.

CSV formula injection is **withdrawn**: it does not reproduce. Every file is resolved
through `Path.GetFullPath`, so the `File` column always begins with a drive letter or a
UNC prefix and can never begin with `=`, `+`, `-` or `@`.

The backlog's summary line also stopped reconciling with the table it summarises, so it
now states a figure counted from the rows and says that it was counted.

## The backlog is a ledger you can check

`docs/DEFECT-BACKLOG.md` exists because a status summary drifted from the code and a
defect recorded as closed shipped broken in two releases. It had since drifted twice more,
in smaller ways, and both were found by hand.

Every entry has now been re-derived from the source rather than carried forward: **61
unique findings — 44 fixed, 13 open, 1 not reproduced, 1 withdrawn, 1 not a defect, and 1
design decision.** It is organised by status rather than by discovery date, so the open
work is in one place instead of spread across five chronological tables, and every finding
has a stable id — including the thirty-three that previously had none and could not be
cited at all.

The recheck found one more error in the old summary, this one introduced while correcting
the previous one: the count of fixed findings included CX-07, which the same review had
just reclassified as *not a defect*.

That is three drifts in a hand-maintained figure, so it is no longer hand-maintained.
`docs/Test-DefectBacklog.ps1` recomputes every number in the header from the rows, and
checks that ids are unique, statuses are known, every open finding carries both impact and
reach, and every detail link resolves.

Three limits are recorded rather than papered over: one open finding needs force-close
timing too precise to trigger and was inspected instead, one needs a filesystem where
`File.Replace` is unsupported, and the historical performance figures were not re-measured
— their current code and safety properties were checked instead.

The checker itself shipped with an encoding defect, which in this project is worth
recording rather than quietly fixing. It was UTF-8 without a BOM and contained em-dashes,
so Windows PowerShell read it in the system ANSI codepage and it would not parse. One of
those em-dashes was a *value* — the marker the ledger writes for an unscored finding — so
repairing only the line the error pointed at would have left that comparison matching
mojibake, in the file whose purpose is being checkable. It is now pure ASCII and runs
under both shells.

The same reasoning was applied to the code comments this release added. Several of them
retold the story of the defect they sat beside - a story already recorded in the commit
message, in these notes, and in the backlog. That is a fourth copy that nothing keeps in
sync, which is the drift problem in a different place. The comments were cut roughly in
half, keeping only what a reader with the code in front of them could not derive: why the
output check returns exit 3 rather than 1, why the writer does not probe by creating a
file, why it flushes to disk, why the recovery sidecar keeps its own writer, and why an
unknown planned action throws instead of being reported as a conversion.

## The source-choice refusal is covered by the smoke suite, not only a unit test

One EC fix has been carried by a unit test rather than a smoke phase since v3.11.2, and
the reason was a defect in the smoke driver rather than in EC.

The sequence it covers: scan a directory, point the window at a different one **without
scanning again**, tick the refused file, choose a source encoding, confirm. That used to
close the review and report *"Conversion cancelled. No files were modified."* — a
cancellation nobody asked for, after the user had ticked files and chosen an encoding. The
behaviour was fixed in v3.11.2. What was missing was a phase that would notice if it came
back.

The phase could not be written, because selecting the source encoding timed out while the
identical call in another phase succeeded. The cause was two failures in sequence. The
item this phase needs sits below the visible part of the dropdown, and UI Automation
reports it offscreen, so the driver's exact-item search rejected it; control then fell
through to a keyboard fallback that brought the *main* window to the front, which is the
wrong target while a modal review is open. The phase that worked chose an encoding that
happened to be visible, so it never reached either failure.

**The suite is now ten phases.** Phase J drives the sequence against the built
application and requires the review to stay open, to name the file that falls outside its
directory, and to leave every source byte unchanged.

No production code changed to make this drivable. The driver was fixed; EC was not
reshaped to be easier to test, which is the trade this suite exists to avoid making.

The unit test stays. It checks the form's decision without needing an interactive desktop,
which is a different question from whether the window behaves, and the unit suite must
keep running without one.

## Compatibility

Conversion semantics stay at **6**, the plan schema at **5**, the journal schema at **4**.
No detection or conversion policy changes: what EC converts, refuses, and reports for a
valid input is exactly what v3.12.0 did.

One exit code changes, for one case:

| Situation | Before | After |
|---|---|---|
| `-Apply` on a plan carrying an undefined action | `Converted`, 0 | refused, 1 |

That 0 was a false success. Every other exit code, reason code, report column and journal
field is unchanged, and a valid plan written by v3.12.0 applies exactly as before.

## Verification

- 742 tests pass, none skipped (was 727); release build with no warnings
- `docs/Test-DefectBacklog.ps1` passes under both Windows PowerShell and pwsh 7,
  and was itself checked by changing a header count and requiring it to fail
- The GUI smoke suite is now **ten phases**, all passing against the built executable.
  The new phase was mutation-checked the same way as the code fixes: the production guard
  it covers was reverted, the phase was required to fail — it did, reporting that the
  review closed after refusing an unusable source choice — and the file was restored
  byte-identical and confirmed by hash
- Each fix was mutation-checked — the change reverted, the intended tests required to
  fail, the file restored byte-identical and confirmed by SHA-256
- Both defects were reproduced end to end before the fix and re-run after: the output
  destination case now exits 3 with the source hash unchanged, and the damaged plan is
  refused with no journal written
- Report output was compared byte-for-byte against v3.12.0's, including the BOM
- Flushing each artifact to disk before installing it was measured over 2,000 files and a
  402 KB report: 152–153 ms with the flush, 153–154 ms without — no measurable cost
- **No four-corpus audit was run.** This release changes no detection or conversion
  policy, so the checklist does not require one. The gap v3.12.0 recorded — a release that
  did change `ConversionPolicy` without a corpus run — is unchanged by this one and still
  stands.
