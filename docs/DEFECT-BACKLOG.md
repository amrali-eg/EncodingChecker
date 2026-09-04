# Defect backlog

Status of the thirty-five findings from the two independent reviews that
preceded v3.11.0, plus what has been found since.

**25 fixed, 9 open, 1 could not be reproduced.**

## Why this file exists

The original review lived in a generated HTML report and in a chat transcript.
Neither survived into the repository, so weeks later the only way to answer
"what is still open?" was to trust a summary — and the summary was wrong.

EC-06 is the demonstration. It was found before v3.11.0, recorded as Medium and
Confirmed, reported as closed, and shipped broken in **both** v3.11.0 and
v3.11.1. It was rediscovered from scratch during an unrelated review, filed as a
new finding, and only then recognised as a known one.

A finding that is written down but not tracked is a finding that gets found
twice and fixed late. Hence this file.

## How each status was reached

`fixed` means the code that caused it is demonstrably gone — a named
replacement, a test that pins the behaviour, or a check re-run against the
current build. `open` means the cited code is still present and was read again
on 2026-09-04. `not reproduced` means an attempt to trigger it failed, which is
weaker than either.

Statuses were re-derived from the source, not carried over from the earlier
report.

## The thirty-five

| ID | Finding | Status | Evidence |
|---|---|---|---|
| EC-01 | Applying a plan converts a file the plan refused | fixed | `PlannedFile.HasReliableUnicodeDetection` carries the flag the plan boundary was dropping. |
| EC-02 | `-Validate` marks an unprovable file valid; `-Target` refuses it | fixed | v3.11.0 reports unprovable BOM-less UTF-16 as `Invalid`. |
| EC-03 | The GUI never shows the advisory v3.10.1 added | fixed | GUI smoke phase H asserts on the rendered advisory text. |
| EC-04 | `-Plan` exits 0 when files failed the scan | fixed | The plan branch returns 3 before considering 2. |
| EC-05 | A plan holding an unreadable file can never be applied | fixed | A hash is required only for `Convert` entries. |
| EC-06 | A drive-root base path makes every plan unusable | fixed | Fixed 2026-09-04. **Shipped broken in v3.11.0 and v3.11.1.** |
| EC-07 | The refusal advises the very encoding it cannot justify | fixed | `DescribeRefusal` offers both byte orders. |
| EC-08 | An include pattern can hang the scan indefinitely | *not reproduced* | A pathological mask against a matching filename completed inside a 10 s budget. No match timeout was added, so this is not *proven fixed*. |
| EC-09 | `.bak` files are excluded, uncounted, and unreported | fixed | `TraversalCounters.FilesExcludedAsEcArtifact`. |
| EC-10 | A scan failure is journaled as a refusal | fixed | A failed snapshot is recorded as `Error`, not `Refused`. |
| EC-11 | `ApplyPlan` leaks its Ctrl+C handler onto a disposed token source | fixed | Both handler sites unsubscribe in a `finally`. |
| EC-12 | The GUI status line counts skipped files as unchanged | fixed | Pinned by a test naming EC-12. |
| EC-13 | The plan's explicit-source field can name one encoding for a run that used several | fixed | `DescribeSourceChoice` reports per-file choices. |
| EC-14 | `OutputTextSha256` is a copy of `SourceTextSha256` | fixed | The record takes the digest verification computed, and throws if absent. |
| EC-15 | The five conversion-semantics booleans are written everywhere and read nowhere | **open** | Only `SemanticsVersion` is enforced on load. |
| EC-16 | Settings.xml is written with truncate-in-place | **open** | `MainForm.Settings.cs:100` still opens `FileMode.Create` and serialises into it. |
| EC-17 | The text-validation comment contradicts its code | **open** | Control characters are penalised, not ignored. Behaviour is right, comment is wrong — and the file must stay byte-identical across three repos, so the fix is a synchronised change. |
| EC-18 | Ambiguity is recomputed on every pass over a BOM-less UTF-16 file | **open** | The or-expression short-circuits only when the flag is already true. |
| EC-19 | The double-BOM guard's reach depends on which object supplied the codec | **open** | `HasMultipleLeadingPreambles` returns false for an empty preamble. |
| EC-20 | `DetectFromFile` opens with looser sharing than every other read path | **open** | Still permits concurrent writes and deletes. |
| EC-21 | Three save-dialog instances are never disposed | fixed | All three use `using var`. |
| EC-22 | Plan serialisation and deserialisation use different options objects | **open** | `ConversionPlan.Load` still deserialises without options. |
| EC-23 | `ApplyPlan` dereferences `ResolvePath` with a null-forgiving operator | **open** | `Program.CliExecution.cs:90`. EC-06 was what happens when that invariant breaks. |
| CX-01 | An empty option value is silently ignored | fixed | Blank values are rejected with exit 1. |
| CX-02 | A failed second conversion destroys the first backup | fixed | `RemoveBeforeBackupReplacement` runs before the backup is replaced. |
| CX-03 | `-Apply` follows a plan root replaced by a junction | fixed | `HasReparsePointInPath` checks the whole path. |
| CX-05 | The journal cannot represent a post-install failure | fixed | `ConvertedWithWarning` and `InstallationUnknown` added. |
| CX-06 | The entropy gate outranks a valid BOM | **open** | `TextEncoding.cs:175` returns null before `UnicodeDetector.DetectFromBuffer` at line 182 reads the BOM. |
| CX-07 | Older plans, journals and reports are ordinary scan candidates | fixed | Reserved suffixes are excluded and rejected as output paths. |
| CX-08 | Documentation and validation disagree about `-DetectOnly` | fixed | Conflicting option combinations are rejected. |
| CX-09 | Cancelling a partly completed GUI run produces no journal | fixed | GUI smoke phase I covers it. |
| CX-10 | Plan summaries say "detection bypassed" when detection still ran | fixed | Now "chosen by you; detection still ran and is recorded". |
| CX-11 | GUI startup can fail if the settings directory cannot be created | fixed | `GetSettingsFileName()` moved inside the `try`. |
| CX-12 | A saved window position is not validated against current monitors | fixed | `WindowPosition.IsReachable` tests the title bar against attached monitors. |
| CX-13 | Detector parity is not a pull-request check or a release gate | fixed | Parity runs on pull requests, and the release workflow declares `needs: parity`. |

## Found after v3.11.1

| Finding | Status | Note |
|---|---|---|
| Ambiguous BOM-less UTF-32 converts silently | **open** | The ambiguity guard covers only code pages 1200 and 1201, so the UTF-32 detector's prefer-little-endian wins with no refusal. Demonstrated end to end; reaching it needs every scalar to be a multiple of 0x100, so real text is unlikely to trigger it. |
| CSV report does not neutralise leading formula characters | **open** | A filename beginning with an equals, plus, minus or at sign becomes a live formula in a spreadsheet. |
| Conversion parallelism was capped at 4 | fixed | Raised to 8 on 2026-09-04; measured 1.5–1.7x faster. |

## The nine that are open

None writes to a file nobody approved, which is why none blocked a release. In
rough order of what a user could notice:

1. **CX-06** — a valid BOM loses to the entropy gate. The one open finding that
   changes what EC reports about a file.
2. **EC-16** — Settings.xml truncate-in-place. Already caused one smoke-test
   failure that looked like a product bug.
3. **EC-20** — detection reads with sharing that permits concurrent writes.
4. **EC-18** — a redundant full-file decode per pass over BOM-less UTF-16.
5. **EC-15**, **EC-22**, **EC-23**, **EC-19**, **EC-17** — contract and clarity
   issues, each a latent trap rather than a live defect.

EC-08 sits outside that list because it could not be reproduced, and the absence
of a reproduction is not evidence of a fix.
