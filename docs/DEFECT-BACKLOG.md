# Defect backlog

This is the current ledger for defects and review findings in EncodingChecker.
It is organised by status, not discovery date, so the open work is visible in
one place. Longer evidence and history follow the ledger.

<!-- backlog-counts total=63 fixed=52 open=7 not-reproduced=1 withdrawn=1 not-a-defect=1 decision=1 -->

**Derived count: 63 findings — 52 fixed, 7 open, 1 not reproduced,
1 withdrawn, 1 not a defect, and 1 design decision.** Recompute and validate
these figures with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/Test-DefectBacklog.ps1
```

`powershell` rather than `pwsh` because it is present on every Windows machine; the script
is ASCII-only and needs no BOM, so either shell runs it. `-ExecutionPolicy Bypass` because
this repository ships no signed scripts and a stock machine refuses to run them at all. It
applies to that one process and changes nothing on the machine.

The checker reads the canonical tables below, verifies unique IDs and known
statuses, and requires both impact and reach for every open finding.

## Status and scoring rules

- **Fixed** means the responsible code is gone and a current source location,
  regression test, or current-build probe demonstrates the replacement.
- **Open** means the behavior remains reachable or a source path to it remains.
- **Not reproduced** means a current attempt did not trigger the proposed
  behavior; that is weaker than proof that it cannot occur.
- **Withdrawn** means the proposed defect was disproved.
- **Not a defect** means the implementation matches an intentional contract.
- **Decision** records a deliberate design difference that needs a choice, not
  a product correction.

Impact asks what a user loses if a finding occurs. Reach asks how readily the
preconditions occur. They are kept separate because a severe but constructed
case and a harmless common case require different decisions.

## Canonical ledger

This is the only place that assigns a current status to an individual finding.
Existing `EC-nn` and `CX-nn` IDs are unchanged. `BL-nn` IDs were assigned during
the 2026-09-08 reformat to findings that previously had only a sentence.

<!-- backlog-ledger:start -->

### Open findings

| ID | Finding | Status | Impact | Reach | Details |
|---|---|---|---|---|---|
| EC-17 | A text-validation comment contradicts the calculation | Open | Low | Common | [EC-17](#ec-17) |
| EC-20 | Detection permits concurrent writes and deletes | Open | Low | Rare | [EC-20](#ec-20) |
| CX-06 | The entropy gate runs before BOM detection | Open | Medium | Theoretical | [CX-06](#cx-06) |
| BL-05 | Force-closing during a run can race UI callbacks | Open | Low | Rare | [BL-05](#bl-05) |
| BL-19 | NUL-heavy ASCII can be reported as BOM-less UTF-16 | Open | Medium | Rare | [BL-19](#bl-19) |
| BL-20 | Hard-linked paths are processed independently | Open | Low | Rare | [BL-20](#bl-20) |
| BL-21 | Detection can accept a truncated trailing sequence that conversion rejects | Open | Low | Rare | [BL-21](#bl-21) |

### Closed and other findings

| ID | Finding | Status | Impact | Reach | Details |
|---|---|---|---|---|---|
| EC-01 | Applying a plan could convert a file the plan refused | Fixed | — | — | [EC-01](#ec-01) |
| EC-02 | Read-only validation disagreed with the BOM-less Unicode safety policy | Fixed | — | — | [EC-02](#ec-02) |
| EC-03 | The GUI omitted the source-choice advisory | Fixed | — | — | [EC-03](#ec-03) |
| EC-04 | `-Plan` could exit successfully after scan failures | Fixed | — | — | [EC-04](#ec-04) |
| BL-01 | Ambiguous BOM-less UTF-32 can be converted under the wrong byte order | Fixed | — | — | [BL-01](#bl-01) |
| EC-08 | A constructed include pattern could hang a scan indefinitely | Fixed | — | — | [EC-08](#ec-08) |
| EC-24 | The GUI smoke gate could select in the wrong combo | Fixed | — | — | [EC-24](#ec-24) |
| EC-18 | A negative BOM-less UTF-16 ambiguity result is recomputed | Fixed | — | — | [EC-18](#ec-18) |
| EC-23 | Plan application relies on a null-forgiving path dereference | Fixed | — | — | [EC-23](#ec-23) |
| BL-27 | Release evidence records no managed-assembly hash | Fixed | — | — | [BL-27](#bl-27) |
| EC-15 | Serialized conversion guarantees are not enforced individually | Fixed | — | — | [EC-15](#ec-15) |
| BL-18 | BOM-less UTF-16 can be detected and converted as UTF-32 | Fixed | — | — | [BL-18](#bl-18) |
| EC-05 | An unreadable non-conversion plan entry made a plan unusable | Fixed | — | — | [EC-05](#ec-05) |
| EC-06 | A drive-root base path made every plan unusable | Fixed | — | — | [EC-06](#ec-06) |
| EC-07 | A refusal advised the same ambiguous encoding it rejected | Fixed | — | — | [EC-07](#ec-07) |
| EC-09 | Excluded EC artifacts were uncounted | Fixed | — | — | [EC-09](#ec-09) |
| EC-10 | A scan failure was journaled as a policy refusal | Fixed | — | — | [EC-10](#ec-10) |
| EC-11 | Plan application leaked a Ctrl+C handler | Fixed | — | — | [EC-11](#ec-11) |
| EC-12 | The GUI counted skipped files as unchanged | Fixed | — | — | [EC-12](#ec-12) |
| EC-13 | A mixed-source plan could report one source encoding for the run | Fixed | — | — | [EC-13](#ec-13) |
| EC-14 | The output-text hash copied the source-text hash | Fixed | — | — | [EC-14](#ec-14) |
| EC-16 | Settings were written by truncating the live file | Fixed | — | — | [EC-16](#ec-16) |
| EC-19 | Double-BOM handling depended on the encoding instance | Not reproduced | — | — | [EC-19](#ec-19) |
| EC-21 | Save dialogs were not disposed | Fixed | — | — | [EC-21](#ec-21) |
| EC-22 | Plan and metadata readers used different JSON options from writers | Fixed | — | — | [EC-22](#ec-22) |
| CX-01 | Empty option values were silently treated as absent | Fixed | — | — | [CX-01](#cx-01) |
| CX-02 | A failed second conversion could destroy the first recovery record | Fixed | — | — | [CX-02](#cx-02) |
| CX-03 | Applying a plan followed a root replaced by a junction | Fixed | — | — | [CX-03](#cx-03) |
| CX-05 | The journal could not represent uncertainty after installation | Fixed | — | — | [CX-05](#cx-05) |
| CX-07 | Old plans, journals, and reports should be excluded from scans | Not a defect | — | — | [CX-07](#cx-07) |
| CX-08 | Documentation and validation disagreed about `-DetectOnly` conflicts | Fixed | — | — | [CX-08](#cx-08) |
| CX-09 | Cancelling after writes produced no journal | Fixed | — | — | [CX-09](#cx-09) |
| CX-10 | Plan summaries said detection was bypassed when it ran | Fixed | — | — | [CX-10](#cx-10) |
| CX-11 | GUI startup could fail before settings error handling began | Fixed | — | — | [CX-11](#cx-11) |
| CX-12 | Saved window positions ignored the current monitor layout | Fixed | — | — | [CX-12](#cx-12) |
| CX-13 | Detector parity was not a pull-request or release gate | Fixed | — | — | [CX-13](#cx-13) |
| BL-02 | CSV cells need formula neutralization | Withdrawn | — | — | [BL-02](#bl-02) |
| BL-03 | Conversion parallelism was capped at four | Fixed | — | — | [BL-03](#bl-03) |
| BL-04 | A ticked source choice could be discarded without explanation | Fixed | — | — | [BL-04](#bl-04) |
| BL-06 | Target identity was compared by label rather than codec | Fixed | — | — | [BL-06](#bl-06) |
| BL-07 | A whole-file unchanged claim came from a 64 KiB sample | Fixed | — | — | [BL-07](#bl-07) |
| BL-08 | Preview could approve a source that conversion could not decode | Fixed | — | — | [BL-08](#bl-08) |
| BL-09 | One unexpected file exception could stop the whole run | Fixed | — | — | [BL-09](#bl-09) |
| BL-10 | An unreadable folder was invisible to machine output | Fixed | — | — | [BL-10](#bl-10) |
| BL-11 | Folders skipped by name were uncounted | Fixed | — | — | [BL-11](#bl-11) |
| BL-12 | Some validation failures had no reason code | Fixed | — | — | [BL-12](#bl-12) |
| BL-13 | Refusal reasons were re-derived after the policy decision | Fixed | — | — | [BL-13](#bl-13) |
| BL-14 | Decode failures could report a negative chunk offset | Fixed | — | — | [BL-14](#bl-14) |
| BL-15 | Console output ignored the console's encoding | Fixed | — | — | [BL-15](#bl-15) |
| BL-16 | Help and CLI documentation stated an old parallelism default | Fixed | — | — | [BL-16](#bl-16) |
| BL-17 | The lifetime of `<file>.bak` was undocumented | Fixed | — | — | [BL-17](#bl-17) |
| BL-22 | An unwritable report or journal path was discovered after conversion | Fixed | — | — | [BL-22](#bl-22) |
| BL-23 | An undefined plan action was reported as a conversion | Fixed | — | — | [BL-23](#bl-23) |
| BL-24 | Plans, journals, reports, and settings used truncate-in-place writes | Fixed | — | — | [BL-24](#bl-24) |
| BL-25 | EC and LEN use different transient content-hash machinery | Decision | — | — | [BL-25](#bl-25) |
| BL-26 | The GUI smoke driver rejected an exact offscreen combo item | Fixed | — | — | [BL-26](#bl-26) |

<!-- backlog-ledger:end -->

## Open-finding evidence

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

### EC-15

**Fixed as an artifact-clarity simplification, not a conversion-safety defect.**
The five flags are gone. Plans and journals now carry `SemanticsDescription` -
the sentence `ConversionSemantics.Describes` already held - beside the version
number, so a reader gets what the version means instead of five constants.
`SemanticsVersion` remains the only enforced compatibility value, and a test
proves the description is never consulted: altering it in a plan file changes
nothing about whether that plan loads.

Was: `StrictDecoding`, `StrictEncoding`, `OutputVerification`, `AtomicInstall`
and `LegacyRequiresExplicitSource` were serialized into every plan and journal,
hardcoded `true`, and read by nothing. They could not disagree with the version,
so they were a second encoding of it that a reader could mistake for evidence a
particular check had run. EC always executed its strict behaviour; only the
artifact implied otherwise.

The artifacts changed shape, so their versions say so: plan schema 5 to 6,
journal schema 4 to 5. That rejects plans written by v3.13.0 as well as older
ones - semantics 7 shipped in that release, so this is not the free change it
would have been a day earlier.

### EC-17

**The printable-ratio comment says the opposite of the calculation.**
`TextValidation.cs` increments the total rune count before its category switch.
Control and private-use scalars do not increment `printable`, so they lower the
ratio; they are not ignored. The behavior is the intended binary-rejection
behavior. The comment is shared byte-for-byte with LineEndingNormalizer and
CorpusTesters, so its correction must be synchronized.

### EC-18

**Fixed.** The classification is cached as a nullable value, so null means not
yet classified and `None` means classified with no doubt found. Both are stored,
and the file is examined once.

Was: only a positive result was kept, so an already-cleared file ran the full
opposite-order check again on every pass. Measurement found no meaningful cost,
because a provable file fails that decode in its first buffer; the defect was
redundant work and a state that could not distinguish "no" from "not asked".

No observable behaviour changes, so this carries no test of its own.

### EC-20

**The detector has a looser sharing mode than the paths that rely on its result.**
`TextEncoding.DetectFromFile` opens with `FileShare.ReadWrite |
FileShare.Delete`; validation and source-snapshot paths use `FileShare.Read`.
Detection can therefore describe bytes while another process changes or deletes
them. This affects detection and validation output; conversion later takes its
own bound snapshot before writing.

### EC-23

**Fixed.** The null-forgiving dereference is now an explicit throw that names the
invariant it rests on: `FindStaleFiles` rejects a plan whose paths resolve
outside its directory, and reaching the dereference means that check did not
run.

Under the present flow nothing changes, which is why there is nothing new to
assert and no test accompanies it. EC-06 is what happened when an invariant of
this shape was left implicit.

### CX-06

**High entropy can hide an otherwise valid BOM.** The entropy guard returns
before `UnicodeDetector.DetectFromBuffer` examines the BOM. The result can be an
unknown or wrong encoding even when the file declares it. This changes what EC
reports; it is not evidence that conversion writes the file.

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

Reopen this on evidence of real text at or above the threshold, not on the
mechanism, which is not in doubt.

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

### BL-05

**A second close request can outlive the form while its worker still uses it.**
`OnFormClosing` deliberately allows a confirmed second close because
cancellation is cooperative. A worker can subsequently call the synchronous
confirmation `Invoke`, and completion accesses form controls. Finished files
have already been installed independently and the in-flight file remains
protected; the expected symptom is an exception during exit, not lost file
content. The timing-dependent path was source-confirmed but not reproduced.

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

**The UTF-16 structure heuristic can claim NUL-heavy ASCII.** The earlier
document said 2.3% of all bytes; that was wrong. A current 65,536-byte ASCII
fixture with a NUL every 100 bytes—1.001% overall—was reported as UTF-16BE. The
detector threshold is 2% in one putative UTF-16 byte channel, approximately 1%
of all bytes for this shape. Conversion refused with exit 5 and the source hash
did not change, so the wrong claim currently reaches detection and validation,
not installation.

### BL-20

**Filesystem aliases are treated as separate selected paths.** Two hard links
to one UTF-8 file were both converted in a current probe. Both retained exact
text, and each received its own verified `.bak` and `.ecmeta.json`; the normal
Windows `File.Replace` path broke their link relationship. This can duplicate
work and does not preserve hard-link identity. The fallback
`File.Move(..., overwrite: true)` path could not be forced on this platform, so
no claim is made about that branch.

### BL-21

**Sample detection and complete conversion intentionally use different flush
semantics.** A UTF-8 file ending in incomplete bytes `E2 82` was reported as
UTF-8 by `-DetectOnly`, because sample detection does not flush an incomplete
tail. Conversion flushed the strict decoder, returned `SourceDecodeError` and
exit 3, and left the source unchanged. The safety path is correct; the detector
can still bless a complete file that conversion rejects.

## Evidence for the original review findings

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

### EC-16

**Settings use the same atomic artifact writer as other records.** An
interruption before replacement leaves the previous preferences intact. This
was closed incidentally by the v3.12.1 artifact-writer refactor, not by a
settings-specific change.

### EC-19

**The proposed encoding-instance gap did not reach conversion.** Conversion
re-resolves the codec name through `Encoding.GetEncoding`, whose UTF-8 instance
has the expected preamble. A current file beginning with two UTF-8 BOMs was
refused with `MultipleLeadingByteOrderMarks` and exit 5 through both automatic
detection and `-From utf-8`.

### EC-21

**All three save dialogs have deterministic disposal.** Each construction site
uses `using var`.

### EC-22

**Each JSON store shares its reader and writer options.** Plan and recovery
metadata no longer serialize and deserialize through mismatched option objects.

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

## Evidence for later findings

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

**The policy owns refusal reason codes.** `ApplyConversion` formerly repeated
the condition already reduced to `SourceInterpretation`. The copies were
textually identical, but nothing tied them together; a future refusal could
fall through the caller's bolt-on guard to `LegacySourceRequired`.
`ConversionPolicy.ReasonCodeFor` now owns the mapping. Tests cover all 256
reachable input combinations and require every refusal to carry a reason.

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

### BL-22

**Requested report and journal destinations are checked before mode dispatch.**
Previously, a missing output directory or an existing directory used as the
output path was discovered after source files had been rewritten, leaving the
requested record absent. All four combinations were reproduced. Preflight now
returns processing exit code 3 before conversion. It does not create a probe
file, which would itself leave artifacts and still could not promise a later
write.

### BL-23

**Undefined plan enums are rejected before a source is touched.**
`System.Text.Json` accepts any number for an enum. An action value of 99 formerly
fell through the result mapper as `Converted`: apply exited 0, reported one
conversion, and journaled action 99 even though the source hash was unchanged.
Plan loading now validates both `Action` and `SourceInterpretation`, while the
result mapper exhaustively names known actions and throws for anything else.

### BL-24

**All durable EC artifacts use replacement writes.** Converted files and
recovery sidecars already used temporary files and replacement; plans, journals,
reports, and settings truncated their live destinations. A failed plan write
could destroy the reviewed plan immediately before use. `AtomicArtifactFile`
now handles all four. The sidecar retains its stronger dedicated writer and
read-back verification. This same refactor addressed EC-16.

### BL-25

**EC and LineEndingNormalizer make the same safety argument with different
transient machinery.** Both use SHA-256 for source bytes and backup evidence.
EC also uses SHA-256 for content digests and persists them as
`SourceTextSha256` and `OutputTextSha256`; LEN uses XxHash3 for a private
normalized-content digest that is discarded. EC compares hexadecimal digest
strings with `string.Equals(..., OrdinalIgnoreCase)`; LEN compares digest bytes
with `CryptographicOperations.FixedTimeEquals`.

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

### EC-24

**Fixed by setting the drop-down instead of hunting its popup.** The combo
supports `ValuePattern` and reports `IsReadOnly` false, so the driver asks the
control for the value. That opens no popup and reaches no other window, so
neither mechanism below is available to fail.

Was: the driver expanded the combo and searched for a list item, first under the
combo and then across the process. How a provider exposes a drop-down's items
varies with popup state and environment, so the first search could miss. The
second accepted any visible item in the process carrying a matching name, and
this application has two encoding combos — `lstConvert` on the main window and
`lstSourceEncoding` in the review — holding the same names, so it could select
in the wrong one and leave the combo under test unchanged.

**Found by the release gate failing on bytes that then passed.** The v3.14.0
release job failed at phase E with `'utf-16BE' was not selected in
'lstSourceEncoding'`, and a re-run of the same commit passed all ten phases.
Nothing in that release touched the driver, the review form, or the encoding
list.

This is [BL-26](#bl-26) arriving a second time. That fix made phase J work and
left the mechanism in place; the v3.12.1 record said the asymmetry it kept "is
the kind of thing a future phase could still trip over," and phase E is that
phase. The keyboard fallback went with the searches: it foregrounded a window
and typed into whatever held focus, and could not have repaired either cause.

**The unsafe mechanism is proven; the cause of the runner incident is not.** An
independent review built a harness holding two controls that offer the same item
name, and reproduced the old process-wide fallback selecting the wrong control
and leaving the combo under test unchanged — the exact shape of the reported
failure. That establishes the defect. It does not establish that the release
runner exposed that state, and no reproduction of the incident itself exists: on
this machine the popup sits inside the combo's subtree and only one process-wide
match is visible, so the ambiguity never arises here. This entry closes the
mechanism, not the incident.

**What the replacement was shown to do.** On the current .NET 10 WinForms
provider, setting the value selects the matching item rather than only changing
displayed text — phase E completes end to end, so the choice reaches conversion
and the output text is preserved. An unknown value is a no-op on this provider,
which the existing postcondition catches; no membership check is added, since one
would reinstate the popup dependency this removes. Two mutations — never setting
the value, and setting a different one — each built cleanly and failed phase E
with the message the release job produced, and the file restored byte-identical
by SHA-256.

**A timeout now names the error it retried.** `WaitFor` discarded
`ElementNotAvailableException`, `InvalidOperationException` and `COMException`
and then reported only a generic timeout, so a probe that threw on every attempt
looked exactly like one that simply never became true. It now keeps the last such
error and `WaitUntil` reports it, so the next failure of this gate is
diagnosable from its message.

**Recorded, not fixed here.** `ConfigureScan` asks for `utf-8` on `lstConvert`
while that control is still disabled, and succeeds only because `utf-8` is
already selected, so the method returns before setting anything: the suite never
exercises the new path on that combo, and a changed application default would
fail every phase at setup. Separately, `Current.IsEnabled` was observed stale for
five seconds on a control that then accepted `Invoke`, and the driver uses
`IsEnabled` as a readiness signal elsewhere. Neither was reproduced as a
failure.

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

### Three hashing optimisations were measured and rejected

Conversion can read the same file several times, but each read answers a
different question: does the source still match approval, did the backup really
land, and does the installed output contain the verified text? Three throwaway
variants were interleaved against the same 292 MiB, 60-file workload with backup
and journal enabled:

| Variant | Measurement | Safety or usability cost |
|---|---|---|
| Baseline | 1030 ms median | None |
| Digest backup while copying | 872 ms; 15.3% faster | Nothing independently re-reads the restore point on disk |
| Hash source and output while streaming | 3.5% faster in its own batch | Both values derive from intended I/O rather than independent reads |
| XxHash128 instead of SHA-256 | 1078 ms; 4.7% slower | Recorded hashes lose `Get-FileHash` interoperability and collision resistance |

The source can be read up to seven times per converted file, and one hash is
computed twice over bytes already held in memory. Those reads are still not
redundant. The only materially faster variant removed the backup re-read after
`Flush(flushToDisk: true)`, which is the only independent check that the restore
point exists intact. XxHash128 itself measured 16,447 MiB/s against SHA-256 at
2,429 MiB/s—6.8x—but made the parallel I/O-bound workload slower. On that
machine SHA-256 also beat SHA-1 (981 MiB/s), MD5 (754 MiB/s), and SHA-512
(805 MiB/s).

These figures are conditional: a 24-core machine, fast local disk, warm cache,
and eight workers. Cold or network storage may increase the benefit of removing
a read while also increasing the value of verifying it. Even a different speed
result—even 40%—would not change what each check proves. The observed ceiling
was about 15%, and that was the variant which removed the strongest backup
evidence. Only a single-worker run on a slow CPU is likely to make hashing itself
visible. Future throughput work should make the backup re-read cheaper rather
than delete it.

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

## 2026-09-08 recheck record

Every pre-reformat row was checked against source, a current regression test, a
current Release-build probe, or a clearly marked historical measurement. The
source inspection used `c071c10`; the factual recheck is preserved as local
commit `902f567` immediately before this reformat.

For chronology, BL-06 through BL-17 came from the independent review of
`74d5b3d` after v3.11.2. BL-22 through BL-24 came from a later independent review
of released commit `518a844` after v3.12.0. These source identities are retained
because the observations were made against those builds, even though the
current statuses were rechecked against `c071c10`.

Current behavioral probes established:

- the EC-08 hostile regex exceeded a three-second child-process budget;
- automatic and explicit UTF-8 paths both refused a double BOM;
- an old plan in the scan root was scanned as ASCII;
- a filename beginning `=1+1` still produced an absolute-path CSV cell;
- BL-01 and BL-18 both changed Unicode under the wrong automatic interpretation;
- BL-19 and BL-21 were reported inconsistently but refused or failed before a
  write; and
- both names of a hard-linked file preserved exact text and received separate
  recovery artifacts through the normal Windows replacement path.

No old row was skipped. Three portions remain only partly reproducible:

- BL-05 requires precise force-close timing and was inspected rather than
  triggered;
- BL-20's move fallback requires a platform where `File.Replace` is unsupported;
  and
- the historical timing figures above were not rerun; their current code and
  safety properties were checked.

## Pre-reformat row mapping

The task expected 78 rows, 45 with an existing ID and 33 without one. The exact
`c071c10` input had 79 data-shaped rows, 45 of which mentioned an existing ID.
The disjoint breakdown is 35 original canonical rows, 21 unique anonymous
finding or decision rows, 13 duplicate or correction rows, 7 benchmark or
comparison-evidence rows, and 3 malformed blank-cell headers. Four findings
existed only as prose, and one GUI-driver finding existed only in a narrative
section. That yields the 61 unique ledger entries above. The mapping below
accounts for every old location without pretending that a table header or
benchmark variant is a new defect.

| Old location | Canonical finding or destination | Note |
|---|---|---|
| R001 | [EC-01](#ec-01) | Original ledger row |
| R002 | [EC-02](#ec-02) | Original ledger row |
| R003 | [EC-03](#ec-03) | Original ledger row |
| R004 | [EC-04](#ec-04) | Original ledger row |
| R005 | [EC-05](#ec-05) | Original ledger row |
| R006 | [EC-06](#ec-06) | Original ledger row |
| R007 | [EC-07](#ec-07) | Original ledger row |
| R008 | [EC-08](#ec-08) | Original ledger row |
| R009 | [EC-09](#ec-09) | Original ledger row |
| R010 | [EC-10](#ec-10) | Original ledger row |
| R011 | [EC-11](#ec-11) | Original ledger row |
| R012 | [EC-12](#ec-12) | Original ledger row |
| R013 | [EC-13](#ec-13) | Original ledger row |
| R014 | [EC-14](#ec-14) | Original ledger row |
| R015 | [EC-15](#ec-15) | Original ledger row |
| R016 | [EC-16](#ec-16) | Original ledger row |
| R017 | [EC-17](#ec-17) | Original ledger row |
| R018 | [EC-18](#ec-18) | Original ledger row |
| R019 | [EC-19](#ec-19) | Original ledger row |
| R020 | [EC-20](#ec-20) | Original ledger row |
| R021 | [EC-21](#ec-21) | Original ledger row |
| R022 | [EC-22](#ec-22) | Original ledger row |
| R023 | [EC-23](#ec-23) | Original ledger row |
| R024 | [CX-01](#cx-01) | Original ledger row |
| R025 | [CX-02](#cx-02) | Original ledger row |
| R026 | [CX-03](#cx-03) | Original ledger row |
| R027 | [CX-05](#cx-05) | Original ledger row |
| R028 | [CX-06](#cx-06) | Original ledger row |
| R029 | [CX-07](#cx-07) | Original ledger row |
| R030 | [CX-08](#cx-08) | Original ledger row |
| R031 | [CX-09](#cx-09) | Original ledger row |
| R032 | [CX-10](#cx-10) | Original ledger row |
| R033 | [CX-11](#cx-11) | Original ledger row |
| R034 | [CX-12](#cx-12) | Original ledger row |
| R035 | [CX-13](#cx-13) | Original ledger row |
| R036 | [BL-01](#bl-01) | Previously anonymous finding |
| R037 | [BL-02](#bl-02) | Previously anonymous finding |
| R038 | [BL-03](#bl-03) | Previously anonymous finding |
| R039 | [BL-04](#bl-04) | Previously anonymous finding |
| R040 | [BL-05](#bl-05) | Previously anonymous finding |
| R041 | [BL-06](#bl-06) | Previously anonymous finding |
| R042 | [BL-07](#bl-07) | Previously anonymous finding |
| R043 | [BL-08](#bl-08) | Previously anonymous finding |
| R044 | [BL-09](#bl-09) | Previously anonymous finding |
| R045 | [BL-10](#bl-10) | Previously anonymous finding |
| R046 | [BL-11](#bl-11) | Previously anonymous finding |
| R047 | [BL-12](#bl-12) | Previously anonymous finding |
| R048 | [BL-13](#bl-13) | Previously anonymous finding |
| R049 | [BL-14](#bl-14) | Previously anonymous finding |
| R050 | [BL-15](#bl-15) | Previously anonymous finding |
| R051 | [BL-16](#bl-16) | Previously anonymous finding |
| R052 | [BL-17](#bl-17) | Previously anonymous finding |
| R053 | [BL-22](#bl-22) | Previously anonymous finding |
| R054 | [BL-23](#bl-23) | Previously anonymous finding |
| R055 | [BL-24](#bl-24) | Previously anonymous finding |
| R056 | [CX-07](#cx-07) | Duplicate correction row |
| R057 | [BL-02](#bl-02) | Duplicate withdrawal row |
| R058 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | Baseline data, not a finding |
| R059 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | Backup-read variant, not a separate finding |
| R060 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | Streaming-hash variant, not a separate finding |
| R061 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | XxHash variant, not a separate finding |
| R062 | [BL-25](#bl-25) | Malformed comparison-table header, not a finding |
| R063 | [BL-25](#bl-25) | Raw-hash comparison evidence |
| R064 | [BL-25](#bl-25) | Content-digest comparison evidence |
| R065 | [BL-25](#bl-25) | Backup-comparison evidence |
| R066 | [Canonical ledger](#canonical-ledger) | Malformed score-table header, not a finding |
| R067 | [CX-06](#cx-06) | Duplicate score row |
| R068 | [BL-01](#bl-01) | Duplicate score row |
| R069 | [EC-08](#ec-08) | Duplicate score row |
| R070 | [BL-02](#bl-02) | Duplicate score row |
| R071 | [EC-16](#ec-16) | Duplicate score row |
| R072 | [EC-20](#ec-20) | Duplicate score row |
| R073 | [EC-15](#ec-15) | Duplicate score row |
| R074 | [EC-17](#ec-17) | Duplicate score row |
| R075 | [EC-23](#ec-23) | Duplicate score row |
| R076 | [EC-18](#ec-18) | Duplicate score row |
| R077 | [BL-25](#bl-25) | Design-decision row |
| R078 | [EC-19](#ec-19) | Malformed not-reproduced table header, not a finding |
| R079 | [EC-19](#ec-19) | Duplicate evidence row |
| P001 | [BL-18](#bl-18) | Former prose-only finding |
| P002 | [BL-19](#bl-19) | Former prose-only finding |
| P003 | [BL-20](#bl-20) | Former prose-only finding |
| P004 | [BL-21](#bl-21) | Former prose-only finding |
| S001 | [BL-26](#bl-26) | Former narrative-only GUI-driver finding |
