# Defect backlog

This is the current ledger for defects and review findings in EncodingChecker.
It is organised by status, not discovery date, so the open work is visible in
one place. Longer evidence and history follow the ledger.

<!-- backlog-counts total=66 fixed=54 open=8 not-reproduced=1 withdrawn=1 intentional-behavior=1 decision=1 -->

**Derived count: 66 findings — 54 fixed, 8 open, 1 not reproduced, 1 withdrawn,
1 intentional behavior, and 1 design decision.** Recompute and check these
figures with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/Test-DefectBacklog.ps1
```

The checker reads the tables below, verifies that IDs are unique and statuses
known, requires both impact and likelihood for every open finding, and confirms
each row links to its own detail heading. Why that exact command, rather than any
other, is explained at the top of the script.

## What the statuses mean

- **Fixed** - the problem has been corrected, and a current source location,
  regression test or current-build probe shows the correction in place.
- **Open** - the problem is still possible: the behaviour remains reachable, or
  a source path to it remains.
- **Not reproduced** - a current attempt did not trigger it. That is weaker than
  proof that it cannot happen.
- **Withdrawn** - later evidence showed the original report was wrong.
- **Intentional behavior** - the implementation matches a deliberate contract.
- **Decision** - two defensible approaches exist and a maintainer may reconsider
  the choice. It is not a product correction.

Two scores are kept apart. **Impact** asks what a user loses if the finding
occurs. **Likelihood** asks how readily its preconditions arise - not how often
it is seen in the wild, which nobody here has measured. A severe but constructed
case and a harmless everyday one need different decisions, and one number cannot
carry both.

## Canonical ledger

This is the only place that assigns a current status to an individual finding.
Existing `EC-nn` and `CX-nn` IDs are unchanged. `BL-nn` IDs were assigned during
the 2026-09-08 reformat to findings that previously had only a sentence.

<!-- backlog-ledger:start -->

### Open findings

| ID | Finding | Status | Impact | Likelihood | Details |
|---|---|---|---|---|---|
| EC-17 | A comment describes the text check backwards | Open | Low | Common | [EC-17](#ec-17) |
| EC-20 | A file can change while EC is detecting its encoding | Open | Low | Rare | [EC-20](#ec-20) |
| EC-26 | The smoke driver trusts an enabled flag that has been seen stale | Open | Low | Rare | [EC-26](#ec-26) |
| CX-06 | EC checks for random-looking data before checking for a BOM | Open | Medium | Theoretical | [CX-06](#cx-06) |
| BL-05 | Force-closing during a conversion can produce an error on exit | Open | Low | Rare | [BL-05](#bl-05) |
| BL-19 | ASCII with many NUL bytes can be reported as UTF-16 | Open | Medium | Rare | [BL-19](#bl-19) |
| BL-20 | Two hard-link names for one file are converted separately | Open | Low | Rare | [BL-20](#bl-20) |
| BL-21 | Detection can accept a cut-off final character that conversion rejects | Open | Low | Rare | [BL-21](#bl-21) |

### Fixed and resolved findings

| ID | Finding | Status | Impact | Likelihood | Details |
|---|---|---|---|---|---|
| EC-01 | A saved plan could convert a file it had marked Refused | Fixed | — | — | [EC-01](#ec-01) |
| EC-02 | Validation could approve a file that conversion would refuse | Fixed | — | — | [EC-02](#ec-02) |
| EC-03 | The GUI omitted the source-choice advisory | Fixed | — | — | [EC-03](#ec-03) |
| EC-04 | `-Plan` could exit successfully after scan failures | Fixed | — | — | [EC-04](#ec-04) |
| BL-01 | Ambiguous BOM-less UTF-32 could be converted under the wrong byte order | Fixed | — | — | [BL-01](#bl-01) |
| EC-08 | A constructed include pattern could hang a scan indefinitely | Fixed | — | — | [EC-08](#ec-08) |
| EC-24 | The GUI smoke gate could select in the wrong combo | Fixed | — | — | [EC-24](#ec-24) |
| EC-25 | The smoke suite never set the main window's target encoding | Fixed | — | — | [EC-25](#ec-25) |
| EC-27 | The smoke driver read a status line the window had not written yet | Fixed | — | — | [EC-27](#ec-27) |
| EC-18 | EC repeated a BOM-less UTF-16 safety check unnecessarily | Fixed | — | — | [EC-18](#ec-18) |
| EC-23 | Plan application assumed a required path existed instead of checking it | Fixed | — | — | [EC-23](#ec-23) |
| BL-27 | GUI smoke reports could show an empty or misleading build hash | Fixed | — | — | [BL-27](#bl-27) |
| EC-15 | Plans and journals showed fixed safety flags as if they recorded checks | Fixed | — | — | [EC-15](#ec-15) |
| BL-18 | BOM-less UTF-16 could be detected and converted as UTF-32 | Fixed | — | — | [BL-18](#bl-18) |
| EC-05 | An unreadable skipped or refused entry blocked the whole plan | Fixed | — | — | [EC-05](#ec-05) |
| EC-06 | Plans rooted at a drive letter could not be applied | Fixed | — | — | [EC-06](#ec-06) |
| EC-07 | A refusal advised the same ambiguous encoding it rejected | Fixed | — | — | [EC-07](#ec-07) |
| EC-09 | Excluded EC artifacts were uncounted | Fixed | — | — | [EC-09](#ec-09) |
| EC-10 | A scan failure was journaled as a policy refusal | Fixed | — | — | [EC-10](#ec-10) |
| EC-11 | Plan application leaked a Ctrl+C handler | Fixed | — | — | [EC-11](#ec-11) |
| EC-12 | The GUI counted skipped files as unchanged | Fixed | — | — | [EC-12](#ec-12) |
| EC-13 | A mixed-source plan could report one source encoding for the run | Fixed | — | — | [EC-13](#ec-13) |
| EC-14 | The journal recorded the source text hash as the output text hash | Fixed | — | — | [EC-14](#ec-14) |
| EC-16 | An interrupted settings save could erase the previous settings | Fixed | — | — | [EC-16](#ec-16) |
| EC-19 | Double-BOM handling depended on the encoding instance | Not reproduced | — | — | [EC-19](#ec-19) |
| EC-21 | Save dialogs were not disposed | Fixed | — | — | [EC-21](#ec-21) |
| EC-22 | EC read some JSON files differently from how it wrote them | Fixed | — | — | [EC-22](#ec-22) |
| CX-01 | Empty option values were silently treated as absent | Fixed | — | — | [CX-01](#cx-01) |
| CX-02 | A failed second conversion could destroy the first recovery record | Fixed | — | — | [CX-02](#cx-02) |
| CX-03 | A saved plan could follow a folder path redirected after approval | Fixed | — | — | [CX-03](#cx-03) |
| CX-05 | The journal could not describe an uncertain result after replacement | Fixed | — | — | [CX-05](#cx-05) |
| CX-07 | Old plans, journals, and reports should be excluded from scans | Intentional behavior | — | — | [CX-07](#cx-07) |
| CX-08 | Documentation and validation disagreed about `-DetectOnly` conflicts | Fixed | — | — | [CX-08](#cx-08) |
| CX-09 | Cancelling after writes produced no journal | Fixed | — | — | [CX-09](#cx-09) |
| CX-10 | Plan summaries said detection was bypassed when it ran | Fixed | — | — | [CX-10](#cx-10) |
| CX-11 | GUI startup could fail before settings error handling began | Fixed | — | — | [CX-11](#cx-11) |
| CX-12 | Saved window positions ignored the current monitor layout | Fixed | — | — | [CX-12](#cx-12) |
| CX-13 | CI did not verify that EC, LEN and CorpusTesters shared the detector | Fixed | — | — | [CX-13](#cx-13) |
| BL-02 | CSV cells need formula neutralization | Withdrawn | — | — | [BL-02](#bl-02) |
| BL-03 | Conversion parallelism was capped at four | Fixed | — | — | [BL-03](#bl-03) |
| BL-04 | The GUI could silently ignore a selected source encoding | Fixed | — | — | [BL-04](#bl-04) |
| BL-06 | Encoding aliases could cause unnecessary rewrites | Fixed | — | — | [BL-06](#bl-06) |
| BL-07 | A whole-file unchanged claim came from a 64 KiB sample | Fixed | — | — | [BL-07](#bl-07) |
| BL-08 | Preview could approve a source that conversion could not decode | Fixed | — | — | [BL-08](#bl-08) |
| BL-09 | One unexpected file exception could stop the whole run | Fixed | — | — | [BL-09](#bl-09) |
| BL-10 | An unreadable folder was invisible to machine output | Fixed | — | — | [BL-10](#bl-10) |
| BL-11 | Folders skipped by name were uncounted | Fixed | — | — | [BL-11](#bl-11) |
| BL-12 | Some validation failures had no reason code | Fixed | — | — | [BL-12](#bl-12) |
| BL-13 | Different code paths could give different reasons for one refusal | Fixed | — | — | [BL-13](#bl-13) |
| BL-14 | Decode failures could report a negative chunk offset | Fixed | — | — | [BL-14](#bl-14) |
| BL-15 | Console output ignored the console's encoding | Fixed | — | — | [BL-15](#bl-15) |
| BL-16 | Help and CLI documentation stated an old parallelism default | Fixed | — | — | [BL-16](#bl-16) |
| BL-17 | The lifetime of `<file>.bak` was undocumented | Fixed | — | — | [BL-17](#bl-17) |
| BL-22 | An unwritable report or journal path was found only after files changed | Fixed | — | — | [BL-22](#bl-22) |
| BL-23 | A damaged plan could report an unknown action as Converted | Fixed | — | — | [BL-23](#bl-23) |
| BL-24 | An interrupted write could erase a plan, journal, report or settings file | Fixed | — | — | [BL-24](#bl-24) |
| BL-25 | EC and LEN use different internal methods to verify converted text | Decision | — | — | [BL-25](#bl-25) |
| BL-26 | The GUI smoke test could reject an encoding below the visible list | Fixed | — | — | [BL-26](#bl-26) |

<!-- backlog-ledger:end -->

## Finding details

One entry per finding, ordered by ID. The status is not repeated here - the
ledger above is the only place that assigns it, so these cannot disagree with it.

### BL-01

**Fixed.** BOM-less UTF-32 is no longer converted automatically in any mode; it
is refused with reason code `UnprovableBomlessUtf32`, and `-DetectOnly` and
`-Validate` report the same code. `ConversionSemantics` moved from 6 to 7,
because a plan approved under the previous behaviour may list files this build
refuses.

Was: the ambiguity guard covered code pages 1200 and 1201, not 12000 and 12001.
A fixture of UTF-32BE `00 00 01 00` units was detected as little-endian and
converted from U+0100 to U+10000 with exit 0.

Widening the opposite-order test would not have been enough on its own — see
[BL-18](#bl-18), whose bytes are *invalid* under the opposite UTF-32 order. Both
are closed by refusing BOM-less UTF-32 outright, at the cost of refusing
ordinary BOM-less UTF-32 as well; nothing in the bytes separates the two.

### BL-02

**No reachable report field begins with a spreadsheet formula marker.**
`DirectoryTraversal` resolves the `File` value with `Path.GetFullPath`, so it
begins with a drive letter or UNC prefix. A current file named `=1+1.txt`
produced `C:\...\=1+1.txt`. Encoding, BOM, target, result, reason code, and
diagnostic are product-controlled values. Reopen this only if a reachable field
starting with `=`, `+`, `-`, or `@` is demonstrated.

### BL-03

**The named default cap is eight.** `ScanEngine.MaxParallelismCap` and
`DocumentedParallelismDefaultTests` keep code, help, and `docs/CLI.md` aligned.
The change from four was measured on 2026-09-04 at 1.5–1.7x faster; that
historical timing was not rerun during the 2026-09-08 source recheck.

### BL-04

**A source choice that cannot be scoped remains visible.** Each review row
carries its resolved path. `DescribeUnusableScope` detects a ticked row whose
path is unavailable, keeps the review open, names how many rows are affected,
and asks the user to run View again. The unit test
`ASourceChoiceThatCannotBeAppliedIsRefusedRatherThanDropped` and GUI smoke phase
J verify the message and unchanged bytes.

Before this correction, choosing an encoding after the review's directory had
changed could close the dialog and report “Conversion cancelled. No files were
modified,” although the user had not cancelled. EC-06 made every row hit that
path when the review root was a drive root.

### BL-05

**Force-closing EC mid-conversion can produce an error as it exits.** If the user
confirms a second close while the run is still stopping, a background task can
try to use a window that has already gone.

No file content is lost. Files already converted were installed one at a time and
stay installed, and the file in flight stays protected. The expected symptom is
an exception while EC closes.

Evidence: `OnFormClosing` deliberately allows a confirmed second close, because
cancellation is cooperative. A background task can then call the synchronous
confirmation `Invoke`, and its completion touches controls on the form.

Still open because it was confirmed by reading the current code rather than by
triggering it; the timing it needs has not been reproduced.

### BL-06

**Already-target identity is canonical codec identity, not spelling.** The old
comparison used `WebName` against the caller's label. `-Target unicode`,
`ucs-2`, or `utf-16le` could therefore decode, re-encode, verify, and reinstall
files already in UTF-16LE with identical bytes, changing timestamps and creating
backups and sidecars. Under `-FailOnChanges`, spelling alone changed the exit
code; on BOM-less UTF-16 it could change a no-op into a refusal. The decision now
compares nonzero resolved code pages. ASCII-to-UTF-8 behavior was left separate
until full-file validation made folding it safe.

### BL-07

**An unchanged decision validates the complete file.** Detection examines at
most 64 KiB. Previously, a matching source and target label skipped every later
byte, so a file valid for 64 KiB and invalid afterward was `Unchanged` under one
target and `Error` under another. Conversion already captures a whole-file
snapshot hash; the added validation measured at 0.04–0.10 ms per MiB.

### BL-08

**A preview reads the source it promises to convert.** The old `WhatIf` branch
returned before decoding, so `-Plan` could approve an unreadable source and defer
failure until `-Apply`, after approval and potentially partway through a batch.
The source now receives strict full-file decode validation and an unreadable
entry is planned as `Refuse`. This remains source-only preflight: target
representability is tested by actual conversion, and a dedicated test pins that
limit.

### BL-09

**Unexpected per-file exceptions are isolated.** The old `Parallel.ForEach`
worker caught four named exception types; a `SecurityException`, regex timeout,
or product defect escaped as `AggregateException` and ended work on files the
run had not reached. The CLI outer catch had the same four-name limit. The
worker now propagates only cancellation and `OutOfMemoryException`.
`RunParallel` is internal so a test can inject the otherwise difficult
exceptions and prove another file still runs.

### BL-10

**Unreadable directories are visible as coverage loss.** An unreadable file
already produced a `ScanFailed` row and exit 3. An unreadable folder formerly
produced only an optional stderr warning—and no GUI trace because the GUI passes
no warning callback. In the original deny-ACE measurement, a tree with one
unreadable folder reported “1 file(s) processed” and exit 0; a wholly unreadable
root produced a header-only CSV and exit 0. `DirectoriesUnreadable` now counts
both traversal failure points separately from intentional exclusions. The exit
code deliberately remains unchanged, so strict automation must inspect coverage
output.

### BL-11

**Folders skipped by reserved name have their own counter.** `.git`, `bin`,
`obj`, `build`, and the other documented names were skipped without appearing in
coverage. They are now counted separately from hidden, system, and reparse-point
folders, preserving the truth of both messages. An include pattern still cannot
override these exclusions, matching both user documents.

### BL-12

**Every validation rejection names its cause.** A charset outside the allowed
list now uses `CharsetNotAllowed`; a file EC cannot identify uses
`UnknownEncoding`. Previously both reached `Invalid` with no reason, forcing a
consumer to re-derive information the producer already had.

### BL-13

**Before:** EC decided whether to refuse in one place and worked out the reason to
report in another. The two were textually identical but nothing held them
together, so they could drift - and a future refusal could fall through the
caller's separate follow-up check and be reported as `LegacySourceRequired`.

**Now:** `ConversionPolicy.ReasonCodeFor` produces both the decision and its
reason.

Evidence: `ApplyConversion` formerly repeated the condition already reduced to
`SourceInterpretation`. Tests now cover all 256 reachable input combinations and
require every refusal to carry a reason.

### BL-14

**Decode errors name offending bytes, not a misleading chunk offset.**
`DecoderFallbackException.Index` is relative to one decoder call and can be
negative when an invalid sequence began in carried bytes. A truncated UTF-8
tail reported offset -2. The diagnostic now reports the byte sequence. An
absolute file offset would require restructuring the streaming loop and is not
claimed.

### BL-15

**Interactive output follows the console; redirected output remains UTF-8.**
Reattaching to a parent console formerly rebuilt writers with UTF-8 even when
`Console.OutputEncoding` was IBM437, turning “Grüße aus München” into
“Gr├╝├ƒe aus M├╝nchen”. The console now receives its own encoding, where
unrepresentable characters become visibly lossy `?`; redirected CSV remains
UTF-8, and `-Report` is UTF-8 with BOM. EC does not mutate global console state.

### BL-16

**The parallelism default has one code identity and checked documentation.** The
help and `docs/CLI.md` both used to say four after the implementation moved to
eight. A test reads the statement in each document and verifies its digits equal
`ScanEngine.MaxParallelismCap`.

### BL-17

**A backup is the version replaced by the most recent run.** Re-converting a
file replaces `<file>.bak` and removes its old sidecar. This long-standing
behavior was tested but undocumented. The documents now say it plainly.

The review initially proposed refusing to overwrite a nonmatching backup. That
proposal was rejected after it broke four existing tests and would have blocked
an ordinary “wrong target, convert again” workflow until the user manually
deleted recovery files. CX-02 instead ensures stale metadata cannot describe a
new backup.

### BL-18

**Fixed** by the same rule as [BL-01](#bl-01): BOM-less UTF-32 is refused
rather than converted, so a UTF-16 file that happens to decode as UTF-32 is left
alone instead of rewritten as different text.

Was: a BOM-less UTF-16 file with one character per LF-terminated line puts a C0
control in every second code unit, so each four-byte group is an in-range
unassigned scalar and the file decodes as valid UTF-32. Converting it wrote
different text, and output verification could not notice, because both sides of
its comparison used the same wrong codec.

This is the case an opposite-order test cannot catch: those bytes are *invalid*
as UTF-32BE, so the check that protects BOM-less UTF-16 returns false. What the
bytes fail to establish is the codec, not merely its byte order — which is why
the fix refuses on the absence of a BOM rather than on ambiguity.

No scalar classification changed. Private-use characters are what icon fonts put
in ordinary text files, so rejecting unassigned or private-use scalars would have
broken real sources; a test converts U+E000, U+F8FF, U+E0B0, U+F00C and U+F0000
and checks all five survive.

### BL-19

**An ASCII file carrying many NUL bytes can be reported as UTF-16.** A current
65,536-byte ASCII fixture with a NUL every 100 bytes — 1.001% of all bytes — was
reported as UTF-16BE.

Conversion then refused it with exit 5 and the source hash did not change. So the
wrong answer reaches what EC *reports*, not what it writes. This is the one open
finding where EC answers its own central question incorrectly.

Evidence: the UTF-16 structure heuristic triggers at 2% of bytes within one
candidate UTF-16 channel, which for this shape is roughly 1% of all bytes. An
earlier version of this document said 2.3% of all bytes; that figure was wrong.

### BL-20

**Two names for one file are converted twice, and stop being the same file.**
Windows can expose a single file under two hard-link names. EC treats them as two
separate files and converts each.

Text is preserved either way: in a current probe both names of one UTF-8 file
were converted, both kept their text exactly, and each received its own verified
`.bak` and `.ecmeta.json`. What is lost is the link itself — the normal Windows
`File.Replace` path breaks it, so afterwards the two names refer to different
files. The work is also done twice.

No claim is made about the `File.Move(..., overwrite: true)` fallback, which
could not be forced on this platform.

Still open partly as a question rather than a defect: whether EC should preserve
hard-link identity or treat selected paths independently is a design choice
nobody has made.

### BL-21

**Detection can accept a cut-off final character that conversion then rejects.**
A UTF-8 file ending in the incomplete bytes `E2 82` was reported as UTF-8 by
`-DetectOnly`. Conversion refused the same file, returned `SourceDecodeError`
and exit 3, and left it unchanged.

The safety path is correct — the file is refused, not converted. The
inconsistency is that detection had already reported it as fine.

Evidence: detection reads a sample and does not treat an incomplete tail as an
error. Conversion reaches the real end of the file and flushes the strict
decoder, which does. The difference is deliberate on both sides.

### BL-22

**Requested report and journal destinations are checked before mode dispatch.**
Previously, a missing output directory or an existing directory used as the
output path was discovered after source files had been rewritten, leaving the
requested record absent. All four combinations were reproduced. Preflight now
returns processing exit code 3 before conversion. It does not create a probe
file, which would itself leave artifacts and still could not promise a later
write.

### BL-23

**Before:** a damaged or hand-edited plan could carry an action value no build
ever wrote, and EC reported it as a conversion. An action of 99 exited 0, said
"1 converted", and wrote a journal recording action 99 - while the source hash
was unchanged. The journal asserted work that never happened.

**Now:** EC rejects an unknown action or source interpretation before touching any
source file.

Evidence: `System.Text.Json` accepts any number for an enum, and the result
mapper's fallback arm was `Converted`. Plan loading now validates both `Action`
and `SourceInterpretation`, and the mapper names every known action and throws
for anything else.

### BL-24

**Before:** plans, journals, reports and settings were written by erasing the
existing file first. If the write then failed, the previous record was gone - and
for a plan, that meant destroying the reviewed plan immediately before it was to
be applied.

**Now:** EC writes a complete temporary file beside the destination and replaces
the old one only after that write succeeds. Converted files and recovery sidecars
already worked this way; all four saved-file types now use the same mechanism,
`AtomicArtifactFile`.

The recovery sidecar keeps its own writer, which also reads back and verifies what
it wrote - more than the shared one does, and not worth reducing. The same change
closed EC-16.

### BL-25

**EC and LineEndingNormalizer both verify that conversion preserved the content,
using different internal methods.** No defect was found; this records a deliberate
difference so a future maintainer does not mistake it for one.

Both use SHA-256 for source bytes and backup evidence. EC also uses SHA-256 for
content digests and saves them as `SourceTextSha256` and `OutputTextSha256`; LEN
uses XxHash3 for a private normalized-content digest that it discards. EC compares
hexadecimal digest strings with `string.Equals(..., OrdinalIgnoreCase)`; LEN
compares digest bytes with `CryptographicOperations.FixedTimeEquals`.

| Evidence | EC | LEN |
|---|---|---|
| Raw source and backup | SHA-256 | SHA-256 |
| Content digest | SHA-256, persisted | XxHash3, discarded |
| Backup comparison | Case-insensitive hexadecimal strings | Fixed-time byte comparison |

Neither comparison is wrong for accidental corruption. This is recorded so a
future maintainer can decide whether safety machinery should converge; detector
parity does not cover it.

### BL-26

**The smoke driver now treats an exact combo item consistently even when UI
Automation calls it offscreen.** Phase J selected `windows-1252`, which was below
the visible part of the source dropdown. The combo-scoped search rejected it,
then a keyboard fallback foregrounded the disabled main form instead of the
modal review. Phase C had not exposed this because its `iso-8859-1` choice was
inside the visible part of the same dropdown. Both combo-scoped and process-wide
exact-name searches now follow the same documented rule, and fallback input
uses the supplied window. Phase J drives the source-choice refusal against the
built application.

This fix kept the search-for-an-item design and made it consistent.
[EC-24](#ec-24) later found that searching for items at all was unreliable, and
replaced the approach rather than adjusting it again.

### BL-27

**Fixed by promising only what the build being driven can show.** The report
hashes a loose managed assembly when one sits beside the executable, and says
there is none when it does not, rather than printing a path with an empty hash
after it. `RELEASE-CHECKLIST.md` and `GUI-SMOKE-TEST.md` describe both cases.
The executable *is* the artifact, so its hash is the provenance that matters;
v3.13.0 demonstrated the stronger form by reproducing that hash byte-for-byte
from the tagged commit.

Was: `gui-smoke-report.json` carried no `EcManagedAssemblySha256` key and the
Markdown rendered an empty pair of backticks, while both printed the
`EncodingChecker.dll` path as though a value followed — because a single-file
publish leaves no loose DLL where the suite looked. That held since single-file
publishing began, so the v3.12.0 and v3.12.1 evidence carries the same empty
field.

The alternative, hashing the publish intermediate `win-x64/EncodingChecker.dll`,
was rejected: it exists only during the build and no user ever receives it, so
recording it would document a byproduct rather than the release. Nothing about
conversion was involved either way; this is evidence hygiene.

### CX-01

**A present option must carry a usable value.** Empty values for all value-taking
flags are rejected with exit 1; `BlankOptionValueSafetyTests` verifies that
nothing changes.

### CX-02

**A stale sidecar cannot survive backup replacement.**
`RemoveBeforeBackupReplacement` removes the old record before replacing the
backup, including a read-only record.

### CX-03

**Applied plans re-check every path component.**
`HasReparsePointInPath` rejects a root or descendant replaced by a junction;
applied-plan integrity tests cover the final component and outside-root cases.

### CX-05

**The journal can say what is and is not known after installation.**
`ConvertedWithWarning` distinguishes a completed install with a later warning;
`InstallationUnknown` represents a failure after the replacement outcome can no
longer be proved.

### CX-06

**EC decides a file looks like random data before it looks for a byte-order
mark.** In principle a file could be reported as unknown, or as the wrong
encoding, even though its first bytes say what it is.

This changes what EC *reports*. It is not evidence that conversion writes a file
it should have refused.

Evidence: the entropy guard returns before `UnicodeDetector.DetectFromBuffer`
examines the mark.

**Re-scored Occasional to Theoretical on 2026-09-08, after measurement.** No
text reaches the gate. Across all four corpora, 3,620 files are large enough to
be gated (512 bytes) and 47 trip the 7.4-bit threshold — every one of them
binary: 35 fixtures under `13_Binary/`, plus images and a spreadsheet in
directories the corpora label `None`. The highest-entropy *text* file among 3,568
candidates is dense Chinese XML in gb2312 at **6.8133**, a margin of 0.59 below
the threshold, with UTF-16 Chinese just behind it. Base64 caps at 6.0 by
construction. Reaching 7.4 needs a near-uniform byte distribution, which prose
in any encoding does not produce.

**A fix was written and then dropped**, which is the part worth recording. Making
the guard yield to a byte-order mark works, and costs something measurable:
`CheckBom` returns a codec from the marker bytes alone, so BOM-prefixed binary
began detecting as `utf-16` instead of `(Unknown)`, and a scan containing one
moved from exit 0 to exit 3. Strict validation still caught it and no bytes
changed, so nothing was corrupted — but that is a measured behaviour change
bought against a benefit no corpus file demonstrates.

It stays open at Theoretical. Re-score it only on evidence of real text at or
above the threshold - not on the mechanism, which is not in doubt.

### CX-07

**Old JSON and CSV artifacts are intentionally ordinary input.** The earlier
ledger claimed they were excluded and even described a correction that was
never made. `docs/CLI.md` deliberately says old plans, journals, and reports are
scanned because a user may wish to convert them. A current `old-plan.json` probe
was detected as ASCII. Only backups, sidecars, temporary files, and the current
command's output paths are excluded.

This false correction was discovered by re-deriving the row from source rather
than trusting its own note.

### CX-08

**CLI mode conflicts are executable documentation.**
`DocumentedOptionContractTests` pins the rejected combinations around
`-DetectOnly`, validation, conversion, plan, and apply.

### CX-09

**An interrupted GUI write run still produces a journal.** Unit coverage and
GUI smoke phase I reconcile completed and unattempted entries.

### CX-10

**An explicit choice does not erase detection history.** Plan summaries now say
“chosen by you; detection still ran and is recorded,” with provenance tests.

### CX-11

**Settings-path creation is inside startup error handling.** A failure no longer
escapes before the guarded settings load begins.

### CX-12

**Window restoration checks the monitors that exist now.**
`WindowPosition.IsReachable` requires a useful title-bar intersection, with
tests for removed, left-side, and secondary displays.

### CX-13

**Detector parity is enforced before integration and release.** The parity
workflow runs on pull requests, and the release workflow declares it as a job
dependency.

### EC-01

**A reviewed refusal is binding.** `PlannedFile.HasReliableUnicodeDetection`
carries the policy input that was formerly lost at the plan boundary.
`AppliedPlanFidelityTests.ThePlanCarriesTheDetectionReliabilityTheVetoDependsOn`
pins it.

### EC-02

**Read-only modes use the conversion safety decision.**
`ReadOnlyModeAmbiguityTests` proves that an unprovable BOM-less Unicode source is
not reported valid when conversion would refuse it.

### EC-03

**The v3.10.1 advisory reaches the real window.** GUI smoke phase H asserts on
the rendered source-choice text rather than only on an internal decision.

### EC-04

**Plan failures control the exit code.** The plan branch returns processing
failure before considering `-FailOnChanges`; `PlanPreflightReportingTests`
covers the ordering.

### EC-05

**Only scheduled conversions require a source hash.** Plan loading no longer
makes an unreadable skipped or refused entry render the whole plan unusable.

### EC-06

**Drive roots resolve without manufacturing `C:\\`.**
`ConversionPlan.ResolvePath` now uses a root-aware containment check, with
theories for `C:\`, nested paths, and outside paths.

This defect was found before v3.11.0, recorded as confirmed, reported as closed,
and shipped broken in both **v3.11.0 and v3.11.1**. It was rediscovered during an
unrelated review. This history is why status is now derived from a checkable
ledger rather than a summary.

### EC-07

**The refusal gives two actionable choices.**
`BomlessUnicodeSafety.DescribeRefusal` offers both UTF-16 byte orders instead of
recommending the unproved estimate.

### EC-08

**Fixed by evaluating wildcard masks with .NET's non-backtracking engine.** The
translation is unchanged — `*` becomes `.*`, `?` becomes `.`, and a
separator-free mask still matches at any depth — so include and exclude results
are the same. What changes is that matching runs in time proportional to the
input rather than retrying an exponential number of alternatives.
`NonBacktracking` replaces `Compiled`; the two are mutually exclusive.

Was: `CompilePatterns` built a `Compiled` regex, which uses
`Regex.InfiniteMatchTimeout`. A mask carrying twelve separated wildcards, matched
against a nonmatching forty-character run of `a`, did not finish within three
seconds and had to be terminated, while realistic names answered in 0–5 ms. The
trigger needs both inputs to be deliberately hostile, and the mask comes from the
operator rather than an untrusted file — which is why reach stayed Theoretical.
An unbounded runtime is still worth removing for the price of one option.

**It is not free, and the earlier note here that it cost nothing was wrong.**
Measured over 3,937 files, median of five warm runs: 128 ms with `Compiled`
against 148 ms with `NonBacktracking` — about 16%, or roughly 5 µs per file, and
the same figure for a plain `*.txt` mask as for `*a*b*.txt`. That is the price of
a bounded worst case, recorded so the next reader weighs it instead of
rediscovering it.

An independent differential comparison of the two engines over 500 generated
masks against 200 generated paths — 100,000 pairs, including separators and the
regex-special characters EC escapes — found no case where they disagreed.

`PathAwarePatternTests.WildcardPatternsUseTheNonBacktrackingEngine` asserts the
option before exercising the hostile input, so restoring the old engine fails in
9 ms instead of hanging the run. That mutation was performed, and the file
restored byte-identical by SHA-256.

Three dead ends stay recorded: a matching filename stops at the first success and
proves nothing; `*` crossing directory separators is deliberate and tested; and
replacing `.*` with `[^/]*` does not prevent backtracking within a separator-free
filename. `PathAwarePatternTests.PathQualifiedPattern_MatchesOnlyTheIntendedSubtree`
pins the intended `src/*.cs` directory behavior.

### EC-09

**Selected EC artifacts are counted.** `.bak`, `.ecmeta.json`, and temporary
conversion files update `TraversalCounters.FilesExcludedAsEcArtifact`, pinned by
`ArtifactExclusionCoverageTests`.

### EC-10

**A failed snapshot is an error, not a policy decision.** Journal outcome tests
pin `ScanFailed` to `Error` rather than `Refused`.

### EC-11

**Both console cancellation subscriptions have bounded lifetimes.** Each Ctrl+C
handler is removed in `finally`, so it cannot retain a disposed token source.

### EC-12

**Skipped and unchanged are separate GUI counts.** The tally is pinned by
`SkippedFilesAreNotCountedAsUnchanged`.

### EC-13

**Mixed batches describe source choice per file.** `DescribeSourceChoice` no
longer presents one run-wide explicit encoding when several were used.

### EC-14

**Source and output text hashes come from separate reads.** The conversion
record accepts the output digest produced by verification and rejects a missing
one; `RecordedProvenanceTests` compares the installed output independently.

### EC-15

**Before:** plans and journals carried five safety flags that were always `true`.
EC never read them when applying a plan, so they proved nothing about whether any
individual check had run - but a reader could easily take a serialized `true` as
proof that one had.

**Now:** the files carry one safety-rules version and a sentence describing it.
EC checks the version; the sentence exists only to tell a reader what the version
means.

This was a clarity fix, not a conversion-safety fix. EC always executed its strict
behaviour; only the artifact implied the flags were the reason.

Evidence: `StrictDecoding`, `StrictEncoding`, `OutputVerification`,
`AtomicInstall` and `LegacyRequiresExplicitSource` were replaced by
`SemanticsDescription`, the sentence `ConversionSemantics.Describes` already
held. `SemanticsVersion` remains the only enforced compatibility value, and a
test confirms the description is never consulted: altering it inside a plan file
changes nothing about whether that plan loads.

The artifacts changed shape, so their versions say so: plan schema 5 to 6,
journal schema 4 to 5. That rejects plans written by v3.13.0 as well as older
ones - semantics 7 shipped in that release, so this is not the free change it
would have been a day earlier.

### EC-16

**Settings use the same atomic artifact writer as other records.** An
interruption before replacement leaves the previous preferences intact. This
was closed incidentally by the v3.12.1 artifact-writer refactor, not by a
settings-specific change.

### EC-17

**A comment describes the text check backwards.** Control and private-use
characters *lower* the printable-text ratio. The comment beside the calculation
says they are ignored.

Nothing a user sees is wrong: the binary-rejection behaviour is the intended one.
What is wrong is what the next person reads, which is why reach is Common.

Evidence: `TextValidation.cs` increments the total rune count before its category
switch, so those scalars count toward the denominator while never incrementing
`printable`.

Still open because the fix is not confined to one repository. The comment is
byte-identical in EncodingChecker, LineEndingNormalizer and CorpusTesters, and
the parity check requires it to stay that way, so all three must change together.

### EC-18

**Before:** EC remembered only when it *had* found a BOM-less UTF-16 ambiguity.
When it found none, that answer was forgotten, and the full check could run again
on the next pass.

**Now:** both answers are remembered, so each file is examined once. The stored
value is nullable: null means not yet classified, and `None` means classified
with no doubt found.

Nothing a user sees changes, which is why this carries no test of its own. It was
not a speed problem either: measurement found no meaningful cost, because a
provable file fails the opposite-order decode inside its first buffer. The defect
was redundant work and a state that could not tell "no" from "not asked".

### EC-19

**The proposed encoding-instance gap did not reach conversion.** Conversion
re-resolves the codec name through `Encoding.GetEncoding`, whose UTF-8 instance
has the expected preamble. A current file beginning with two UTF-8 BOMs was
refused with `MultipleLeadingByteOrderMarks` and exit 5 through both automatic
detection and `-From utf-8`.

### EC-20

**A file can change while EC is working out what encoding it is.** Detection
opens the file in a mode that lets another program write to it or delete it at
the same time, so the encoding EC reports can describe bytes that have already
changed.

No source file is altered by this. Before conversion writes anything it takes its
own copy of the bytes, bound to a hash. What can go stale is what detection and
validation *report*.

Evidence: `TextEncoding.DetectFromFile` opens with
`FileShare.ReadWrite | FileShare.Delete`, while the validation and
source-snapshot paths use `FileShare.Read`.

Still open because the obvious correction is not obviously right. Tightening
detection to match would make EC fail on any file another program holds open,
which includes ordinary log files.

### EC-21

**All three save dialogs have deterministic disposal.** Each construction site
uses `using var`.

### EC-22

**Each JSON store shares its reader and writer options.** Plan and recovery
metadata no longer serialize and deserialize through mismatched option objects.

### EC-23

**Before:** applying a plan assumed an earlier check had already produced a valid
file path. The assumption held, but nothing said so - and had later code bypassed
that check, the failure would have surfaced as an unexplained null reference.

**Now:** EC stops with an explicit error naming the check that must have run.

Under the present flow nothing changes, which is why nothing new is asserted and
no test accompanies it. EC-06 is what happened the last time an invariant of this
shape was left implicit.

Evidence: `FindStaleFiles` rejects a plan whose paths resolve outside its
directory; reaching the dereference means that check did not run.

### EC-24
**Before:** the smoke driver picked an encoding by searching for a list item -
first under the intended drop-down, then across the whole EC process for any
visible item with a matching name. Both encoding drop-downs contain names such as
`utf-16BE`, so that second search could select the item in the wrong control and
leave the one under test unchanged. `lstConvert` is on the main window,
`lstSourceEncoding` in the review.

**Now:** the driver sets the value on the intended drop-down. It opens no list,
searches no other window, changes no foreground focus and sends no keystrokes, so
neither the popup-location dependency nor the name collision is reachable. The
combo supports `ValuePattern` and reports `IsReadOnly` false.

**Found by the release gate failing on bytes that then passed.** The v3.14.0
release job failed at phase E with `'utf-16BE' was not selected in
'lstSourceEncoding'`; a re-run of the same commit passed all ten phases. Nothing
in that release touched the driver, the review form or the encoding list.

**The mechanism is proven; the cause of that one run is not.** An independent
review built a harness with two controls offering the same item name and
reproduced the old fallback selecting the wrong one - the same observable result.
That establishes the defect. It does not establish that the release runner was in
that state, and the incident itself was never reproduced: on this machine the
popup sits inside the combo's subtree and only one process-wide match is visible,
so the ambiguity never arises here. This entry closes the mechanism, not the
incident.

**Evidence.** On the current .NET 10 WinForms provider, setting the value selects
the matching item rather than only changing displayed text: phase E completes end
to end, so the choice reaches conversion and the output text is preserved. An
unknown value is a no-op on this provider, which the existing postcondition
catches - no membership check is added, since one would reinstate the popup
dependency this removes. Two mutations, never setting the value and setting a
different one, each built cleanly and failed phase E with the message the release
job produced, and the file restored byte-identical by SHA-256.

**A timeout now names the error it retried.** `WaitFor` discarded
`ElementNotAvailableException`, `InvalidOperationException` and `COMException`
and reported only a generic timeout, so a probe that threw every time looked
exactly like one that never became true. It keeps the last such error and
`WaitUntil` reports it.

**Relation to [BL-26](#bl-26).** That fix kept the search-for-an-item design and
made it consistent. The v3.12.1 record said the asymmetry it kept "is the kind of
thing a future phase could still trip over," and phase E is that phase. The
keyboard fallback went with the searches: it foregrounded a window and typed into
whatever held focus, and could not have repaired either cause.

Two loose ends were found while making this change and neither is fixed by it.
They carry their own IDs rather than sitting inside a closed entry where the
ledger cannot give them a status: [EC-25](#ec-25) and [EC-26](#ec-26).

### EC-25

**Before:** `ConfigureScan` asked for `utf-8` on `lstConvert` before scanning,
while that control is still disabled. It succeeded only because `utf-8` was
already selected, so the call returned before setting anything. The selection path
[EC-24](#ec-24) replaced was therefore never driven on that combo, and a changed
application default would have failed every phase during setup rather than in the
phase that cares.

**Now:** the suite states the assumption and exercises the path. `ConfigureScan`
requires the window to open on `utf-8` and fails with one message naming that if
it does not. Phase A sets the target to `us-ascii` and back after cancelling its
review, where the run is over and the phase already proves no bytes moved.

`us-ascii` rather than a BOM variant on purpose: `utf-8-bom` and `utf-16BE` share
a prefix with other entries, so a setter that matched on prefix would have made
the check pass while selecting something else.

**EC is unchanged.** The gap was in the suite. It is worth noting what the
assertion protects, though: `MainForm` selects `utf-8` only when
`FindStringExact` finds it and falls back to the first entry otherwise, so the
default every phase depends on is conditional in the product rather than
guaranteed.

**Both halves are load-bearing**, checked by mutation with the build required to
succeed and the compiled binary's hash required to change first. Expecting a
different default fails in 750 ms with `GuiDriverException`; making the setter a
no-op is caught by the round trip. Fifteen consecutive full runs passed after the
change - fewer than the twenty-five behind [EC-26](#ec-26), because altering the
suite retires the evidence gathered for the previous one.

### EC-26

**The driver treats an enabled flag as a readiness signal, and it has been seen
stale.** `Current.IsEnabled` reported a control disabled for five seconds while
that same control accepted `Invoke` and closed the review.

Four readiness checks still rest on it: that a scan has finished, that writing has
begun, whether cancellation is still possible, and that the main window has
returned to idle. None of them confirms the thing it is waiting for; each asks the
flag instead.

What it means is that a phase could time out waiting for a control that was ready
the whole time, and report a defect in EC that is not there - the same shape of
wrong answer the preflight check exists to prevent.

**This was briefly closed on the strength of [EC-27](#ec-27), and should not have
been.** That fix stopped one phase reading a status the window had not written
yet, by waiting for the text rather than the button. It removed a consequence of
trusting the flag in one place; it did not remove the dependency, and the four
checks above are unchanged. Twenty-five clean runs say nothing about a flag that
was seen misreporting once.

Closing it means readiness checks that confirm the operation rather than consult
the flag. Nothing has been changed for that yet.

### EC-27

**Before:** the driver treated an enabled button as "the run has finished". The
window enables its buttons before it assigns the final status text, so a phase
that read the status the moment the buttons came back could read the previous
message, or none at all.

**Now:** the driver waits for the status a phase is about to assert, rather than
for the button. Phase I counts the files actually rewritten, waits for that figure
to appear as `N converted` and, when the run stopped early, for `M not attempted`,
and only then reads the line. No sleep is involved: a sleep makes a race less
likely rather than absent.

**EC is unchanged.** Enabling the buttons before assigning the status is a
reasonable order for a window, and nothing a user sees depends on it. The defect
was in the instrument's idea of "finished".

**Reproduced, which is what closed it.** On 2026-09-09 a full run failed phase I
with *"The 324 unreached file(s) are missing from the status"*, quoting a status
that was the window's control names with no run message among them - the
assignment had not happened yet. It then passed three times in isolation and twice
in full runs. That is what makes this shape dangerous: the phase reports a defect
in EC, in the alarming direction, and then disappears when you look again. Roughly
one failure in fifteen runs before the fix.

Twenty-five consecutive full runs passed afterwards. That is corroboration rather
than proof - at the observed rate, twenty-five clean runs happen by chance about
one time in five - so what closes this is the identified cause and a wait on the
text being read, with the runs agreeing.

`MainForm.UpdateControlsOnActionDone` enables `btnView` on its first line and
assigns `actionStatus.Text` forty-five lines later. A driver polling every 50 ms
lands between the two often enough to matter.

This is one consequence of [EC-26](#ec-26) removed, not EC-26 itself. The driver
still consults the flag in four other readiness checks.

## Decisions and mistakes that must remain visible

### A known defect shipped after being reported closed

EC-06 was present before v3.11.0, recorded as confirmed, and reported as closed.
It shipped broken in v3.11.0 and v3.11.1, then was rediscovered from scratch
during unrelated work. Neither the corpus audit nor the GUI suite covered a
drive-root plan. The record failed because it trusted a summary instead of the
source.

### A correction was recorded for code that should not change

CX-07 said old plans, journals, and reports had been excluded. No such change
had been made, and `docs/CLI.md` intentionally promises the opposite. The false
record was corrected on 2026-09-07 only after the row was re-derived from code.

### Aggregate counts drifted repeatedly

The open count had already stopped reconciling with its rows three times. The
2026-09-08 recheck found a separate error: “27 fixed” counted CX-07 as fixed
although the ledger called its alleged behavior not a defect. The current header
is generated from the canonical rows, and `Test-DefectBacklog.ps1` fails if it
drifts again.

### A later review over-rated four of its own findings

The independent review of `74d5b3d` that produced BL-06 through BL-17 correctly
found mechanisms, then described their importance before checking realistic
product behavior. The user caught all four corrections: two findings were
downgraded after measurement, one proposed correction was rejected when it
broke four existing tests, and one finding was withdrawn after checking the
actual CSV. This is kept visible because a true mechanism does not automatically
justify the claimed product risk.


## The source-choice refusal is covered by both a unit test and smoke phase J

Before BL-04 was addressed, a user could scan one directory, point the main
window at another without scanning again, tick a refused file, choose an
encoding, and confirm. The review closed and the status said “Conversion
cancelled. No files were modified.” The user's choice had been discarded.

The unit test constructs that state directly. Smoke phase J drives it through
the built application: the review must remain open, name
`..\scanned\french.txt`, explain that it is no longer inside the review, and
leave every source byte unchanged. BL-26 records the automation-driver defect
found while making that phase reliable.


## Historical records

The recheck that produced this ledger, the measurements behind three rejected
throughput changes, and the pre-reformat row mapping are kept in
[DEFECT-BACKLOG-HISTORY.md](DEFECT-BACKLOG-HISTORY.md). They are audit trail
rather than current status, and moving them changed nothing but their address.
