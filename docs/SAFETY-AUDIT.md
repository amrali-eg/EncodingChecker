# Independent safety audit

This document records the independent corpus evidence for released EC builds.
For the current conversion rules, backups, recovery metadata, and known limits,
see [Safety and recovery](SAFETY.md). For the user workflow, see
[How conversion works](CONVERSION-WORKFLOW.md).

## Method

[CorpusTesters](https://github.com/amrali-eg/CorpusTesters) is a separate, reproducible audit harness. It runs EC against four public corpora:

- [UnicodeTestSuite](https://github.com/amrali-eg/UnicodeTestSuite)
- [chardet test-data](https://github.com/chardet/test-data)
- [char-dataset](https://github.com/Ousret/char-dataset)
- [UTF-unknown](https://github.com/CharsetDetector/UTF-unknown)

It operates on working copies, never source corpora. For each file with authoritative metadata, it compares the exact decoded source text against strict UTF output. It also verifies backup hashes, inventories every file, runs mutation controls, checks codec strictness, and keeps per-file CSV/JSON evidence.

The audit distinguishes detection identity, text-equivalent labels, unsupported or unscored material, mapping/profile differences, and end-to-end text preservation. It does **not** treat one runtime's legacy mapping table as a universal authority: its sampled independent-implementation comparison is recorded separately, and mapping differences remain explicitly qualified.

Current raw artifacts, methodology revisions, and results are published with CorpusTesters. Historical corpus figures must be read in their recorded taxonomy and build context; they are not a substitute for the current product policy in [Safety and recovery](SAFETY.md).

### Why a release record arrives after its own tag

Each record below is committed after the release it describes, so a tagged tree
contains every earlier release's record but not its own. That is forced, not an
oversight.

An audit measures a built binary, and the .NET SDK embeds the commit into the
PDB while the assembly records that PDB's checksum. Committing the record
therefore changes the assembly, even though only a Markdown file changed.
Measured on this repository: commit `522eeb8` builds `19bcbe09...`, and
`336c4f2` — which adds only 50 lines to this file — builds `7f5ced3d...`. The
two are the same 487,424 bytes and differ in 72: the deterministic PE stamp, the
MVID, and the PDB checksum. No compiled code differs. Two clean builds of one
commit are byte-identical, so this is caused by the commit, not by build noise.

Recording the audit before tagging would therefore publish artifacts built from
a commit the audit never measured, and the record inside them would name a hash
they do not contain. That trades a documentation gap for a false provenance
claim, which is worse. The gap is the honest option.

A release's own record is reachable two ways: its GitHub release page links
directly to the section below, and CorpusTesters holds the per-file evidence.

### v3.9.0 audited build

The v3.9.0 release was audited from a clean checkout of the commit it was tagged at, and the binary that was measured is the binary that was published.

```
commit    ef645f20a7bd0db42278e80140170d4829af40fa   (annotated tag v3.9.0)
worktree  clean
platform  .NET 10.0.400 - Windows 11 10.0.26200
built     2026-08-30T22:22:29Z
assembly  EncodingChecker.dll
          a3e82da30bb8635ba71b4326c0f6bb716de388c2e9827771c4f689580fd71e4d
```

That digest is the SHA-256 of the managed assembly the audit loaded and exercised. It is **not** the digest of the apphost `EncodingChecker.exe`, and not of either release ZIP; GitHub publishes the digests for the downloads separately. Cite it as the assembly hash.

Across 5,078 files in the four corpora, EC produced zero silent decoder-side losses and five substantive misdetections. Every other text-changing result was explicitly classified.

| Outcome | Files | Meaning |
|---|---:|---|
| `PASS` | 4520 | Converted, text preserved exactly |
| `ECCodecUnsupported` | 297 | Reference codec EC can name under no spelling - unmeasurable, unscored |
| `MappingDifference` | 103 | Correct codec, different mapping profile |
| `UnknownEncoding` | 42 | Not identified; left untouched |
| `OutOfScope` | 39 | Excluded by EC's own traversal rules |
| `RefusedByPolicy` | 29 | EC declined; nothing written |
| `DecodeError` | 24 | Refused at strict decode; original intact |
| `NoReferenceEncoding` | 16 | No ground truth to judge against |
| `Misdetection` | 5 | Substantive: a different reading the bytes did distinguish |
| `UnknownReferenceEncoding` | 2 | Codec the audit cannot construct |
| `ReferenceDecodeError` | 1 | Corpus defect, not an EC one |
| `SilentDecodeLoss` | 0 | Text lost without EC reporting it |

Text preservation per corpus, excluding refusals from both numerator and denominator: uts3 1279/1280, chardet 2766/2818, charsetnormalizer 413/463, utfunknown26 62/62.

**What this does not establish.** The 297 unsupported files are unscored in both directions - counting them as failures would blame EC for a conversion it was never offered, and counting them as passes would be the flattering half of the same error. The 103 mapping differences changed exact Unicode scalars; 90 are the documented JIS X 0208 vendor split, where JIS, Python and iconv map `0x8160` to U+301C while Microsoft, .NET and WHATWG map it to U+FF5E. Ground truth is the corpora's own metadata with Python as an operational reference decoder, so an EC/Python disagreement on a legacy mapping is a divergence between implementations, not proof about either. The audit supplies each corpus's reference codec explicitly, so it measures whether EC preserves text when told what the bytes are - not whether it can determine that unaided. GUI evidence is three scripted phases, not coverage.

Reproducing it, with the corpora in place:

```bash
git clone https://github.com/amrali-eg/EncodingChecker.git && cd EncodingChecker
git checkout v3.9.0
dotnet build sources/EncodingChecker.sln --configuration Release
sha256sum sources/EncodingChecker/bin/Release/net10.0-windows/EncodingChecker.dll
```

then, in CorpusTesters, `CORPUS_ROOT=<corpora> ./run-all.sh release`. Every run records `ECGitCommit`, `ECGitTreeDirty` and `ECAssemblySha256` in its `run.json`; this was the first run of these corpora with a clean worktree, and three separate builds produced identical counts.

### v3.9.1 — `8ffd79bb9d463fbe345e93efc2821250cb6f50c0`, not re-audited

A defect-fix release. **It was not measured against the four corpora**, and no audited build of it exists, so no assembly hash is quoted; the published artifacts' digests are on its GitHub release page. The v3.9.0 figures above are not evidence about this patch.

Four defects, each verified to no longer reproduce against a build of the tagged commit, with the full suite passing 459/459:

| Defect in v3.9.0 | v3.9.0 behaviour | v3.9.1 behaviour |
|---|---|---|
| Saved plan with a path escaping its recorded root | Unhandled `ArgumentNullException`, exit 127, **after** files were converted; no journal written | Refused at plan load, exit 3, nothing written |
| Saved plan naming a runtime-unsupported codec | Unhandled `NotSupportedException`, exit 127, same point | Refused at plan load, exit 3, named in the message |
| One unreadable file in a scanned folder | No plan produced for any file, exit 3 | Plan written; the unreadable file appears as an explicit `Refuse` / `ScanFailed` |
| Stale-file check | Inspected only files planned for conversion, contradicting its own contract | Inspects every planned file |

The first two shared a cause worth recording: `ConversionJournal.FromRun` runs after the conversion pass, so an exception there destroyed the record of work already completed. The fix rejects the malformed plan before any conversion begins rather than making the journal tolerant — the run that should not have happened no longer happens, instead of being accurately recorded.

**A provenance correction.** A fifth reported defect — a backup left behind when a repeated-BOM refusal aborts the conversion — was investigated as v3.9.0 behaviour and was not. `MultipleLeadingByteOrderMarks` does not exist in the v3.9.0 tag; the reproduction ran against a working-tree build that already carried unreleased work, and the binary was never checked against the tag. The defect was real in that unreleased state and is fixed, but it was never reachable in a released build, and the earlier report describing it as shipped behaviour was wrong.

### v3.9.2 audited build

v3.9.2 was measured against the same four corpora as v3.9.0, from a clean checkout of the commit it was tagged at.

```
commit    bf6065c15fd82c58e634cb53b73c97939c4d8e94   (annotated tag v3.9.2)
worktree  clean
platform  .NET 10 - Windows 11 10.0.26200
assembly  EncodingChecker.dll
          1622eb56d5e008875530e677c66c8e88f63cd7f1449f4ac772227548a86dbea0
run       rel392, compared against rel390 in audit/reports/rel390-vs-rel392/
```

That digest identifies the managed assembly the audit loaded and exercised. It is **not** the digest of the apphost `EncodingChecker.exe`, and not of either release ZIP; GitHub publishes those separately.

**Across all 5,078 files, not one changed outcome from v3.9.0.**

| Metric | v3.9.0 | v3.9.2 |
|---|---|---|
| Detection accuracy | 4640/4646 (99.87%) | 4640/4646 (99.87%) |
| Strict-decoding correctness | 4695/4695 (100.00%) | 4695/4695 (100.00%) |
| Codec conformance | 4592/4695 (97.81%) | 4592/4695 (97.81%) |
| End-to-end text preservation | 4520/4623 (97.77%) | 4520/4623 (97.77%) |

`compare.py` joins per file rather than comparing totals, and reported `changed=0 improved=0 regressed=0 lateral=0` with its distribution alarm armed at one percentage point. The outcome table matches row for row, including the 103 mapping differences and the 297 unscored files, and all four corpora recorded zero implementation defects, zero throws, and zero backup-integrity failures. Both runs covered four complete corpora and the same 5,207 rows; that was checked before the figures were read.

**What the audit establishes here.** That two patches touching the plan, journal, recovery-metadata and scan-coverage paths did not perturb the conversion engine — the claim the release notes make, now measured rather than asserted.

**What it does not.** The corpus exercises direct conversion with an explicit source, so it does not touch the plan validation, `-Include` rejection, coverage counting, or `Prepared`/`Completed` protocol that this patch added. `changed=0` is evidence of no regression, not evidence that the new behaviour works; that rests on the regression suite.

The caveats stated for v3.9.0 apply unchanged: 297 files remain unscored in both directions, and the 103 mapping differences changed exact Unicode scalars.

#### What changed in v3.9.2

Closes two paths where a run could describe more than it had established, and hardens the recovery record:

- A `-Include` value parsing to no usable pattern is rejected rather than silently meaning every file. This is a behaviour change at the CLI boundary: `-Include ""` now exits 1 where it previously ran.
- Files and folders skipped for hidden, system, or reparse-point attributes are counted and reported, so a clean result is distinguishable from files never opened. The counts are informational and do not change the exit code.
- The sidecar is written through a verified temporary file and atomically replaced, and records an installation state — `Prepared` before installation, `Completed` after — with the expected output hash, so a run interrupted between the two can be resolved by hashing the current file.
- An explicit source that disagrees with a BOM-less UTF-16/32 estimate is now recorded and displayed as `ExplicitSourceDiffersFromBomlessUnicodeEstimate` instead of converting silently. The user's choice still wins, as designed; a BOM-confirmed conflict remains a refusal.
- Saved plans preserve automatic-detection provenance. **The plan schema is version 4**; plans written by an earlier release are rejected and must be regenerated.

Verified on the tagged commit: CI and the shared-detector parity job both pass, the full suite passes 485/485 with zero warnings, and the three-repository detector drift check reports no drift. The drift check proves the copies agree, not that they are correct.

The `Prepared`/`Completed` protocol still has no restore command to exercise it, so its recovery value rests on the record being readable by hand rather than on a tested recovery path, and `ExpectedOutputSha256` is recorded but nothing in EC consumes it yet.

### v3.10.0 audited build

v3.10.0 changes conversion policy, so it was measured against the four corpora from a clean checkout of the commit it was tagged at, and compared per file against v3.9.2.

```
commit    522eeb837b9ab843b20ad3e44dcc403493c3119d   (annotated tag v3.10.0)
worktree  clean
platform  .NET 10 - Windows 11 10.0.26200
assembly  EncodingChecker.dll
          19bcbe0958830847771e73fd48bcac5566e10fda1ca3814585667f5fd5e1aa35
run       rel3100, compared against rel392 in audit/reports/rel392-vs-rel3100/
```

As with the earlier records, that digest identifies the managed assembly the audit loaded, not the apphost and not either release ZIP.

**Exactly one file of 5,078 changed outcome, and it improved.**

```
changed=1  improved=1  regressed=0  lateral=0

Misdetection      5 -> 4   (-1)
RefusedByPolicy  29 -> 30  (+1)
```

A file that v3.9.2 misdetected and converted anyway is now refused. That is the entire measured effect of the release: not a trade of safety against capability, but one file that stopped being silently rewritten as characters its author never wrote.

| Metric | v3.9.2 | v3.10.0 |
|---|---|---|
| Detection accuracy | 4640/4646 (99.87%) | 4640/4646 (99.87%) |
| Strict-decoding correctness | 4695/4695 (100.00%) | 4694/4694 (100.00%) |
| Codec conformance | 4592/4695 (97.81%) | 4591/4694 (97.81%) |
| End-to-end text preservation | 4520/4623 (97.77%) | 4520/4623 (97.77%) |

The denominators fall by one because a refused file is no longer scored: nothing was written, so there is nothing to compare. The distribution alarm did not fire — at 0.02 percentage points the movement is far below its one-point threshold. A larger shift was expected before the run; the audit established that the policy change reaches one file rather than a population, which is the kind of number an estimate cannot supply.

Every other category is unchanged, including the 103 mapping differences and the 297 unscored files, and all four corpora recorded zero implementation defects, zero throws, and zero backup-integrity failures.

**What the audit establishes here.** That a deliberate policy change did exactly what it intended and nothing else — one misdetected file refused, no other file's outcome disturbed.

**What it does not.** The corpus supplies each file's reference codec through `-From`, so it never exercises the GUI source chooser this release added. That the chooser offers `utf-16le` and `utf-16be` for an ambiguous refusal was verified by manual GUI smoke phases D and E against the Release build, and by the orchestration regression tests — not by corpus measurement.

#### What changed in v3.10.0

- **BOM-less UTF-16 whose byte order cannot be proven is refused.** Without a BOM these bytes usually decode as valid text in both byte orders, and EC's content verification cannot catch a wrong choice because it decodes and re-encodes through the same codec, so both sides agree on the wrong reading. Conversion is refused when the opposite byte order also strictly decodes the complete file, with reason code `AmbiguousBomlessUtf16` and exit code 5, before any preview, metadata, backup, or write. A refusal leaves no `.bak` and no sidecar even when backups are enabled. Where the opposite order is structurally impossible the byte order is proven and conversion proceeds unchanged.
- **The refusal is answerable rather than final.** `RequiresExplicitSourceChoice` covers this reason code alongside legacy text, so the GUI's source chooser offers `utf-16le` and `utf-16be` for these files and `-From` resolves them on the command line. An explicit choice replaces detection only; strict decoding, verification, backup checks and atomic installation all still apply.
- `--version` reads the assembly rather than a literal, and the release workflow refuses to publish when the tag and assembly versions disagree.
- The documentation is reorganised into focused pages under `docs/`.

Verified on the tagged commit: CI and the shared-detector parity job both pass, the full suite passes 492/492 with zero warnings, and manual GUI smoke phases D and E pass against the Release build. `UnicodeDetector.cs` and `TextValidation.cs` are untouched, so parity with LineEndingNormalizer and CorpusTesters is unaffected.

### v3.10.1 audited build

v3.10.1 corrects an inversion in the v3.10.0 safety and was audited from a clean checkout of the commit it was tagged at.

```
commit    a224356   (annotated tag v3.10.1; audited as 8f373a7 before merge)
worktree  clean
platform  .NET 10 - Windows 11 10.0.26200
assembly  EncodingChecker.dll
          36b923814fb92dae468d45290a251de3871b3cdae24c883b48ff79c3f1ad253e
run       rel3101, compared against rel3100 in audit/reports/rel3100-vs-rel3101/
```

**No file changed outcome.** `changed=0 improved=0 regressed=0 lateral=0`, and all four metrics are identical to v3.10.0: detection 4640/4646, strict decoding 4694/4694, codec conformance 4591/4694, text preservation 4520/4623.

**This result was predicted before the run, and that is the point of recording it.** The corpus supplies each file's source codec from its own reference metadata, and those files are not ambiguous, so the path this release fixes never executes during a corpus run. `changed=0` therefore establishes that the fix did not disturb the conversion engine, and establishes nothing whatever about whether the fix works. That rests on three regression tests, each checked against a deliberately reintroduced bug.

An audit that cannot reach the change it is run for is worth stating plainly rather than quoting as confirmation.

#### What changed in v3.10.1

v3.10.0 refuses BOM-less UTF-16 whose byte order cannot be established from the bytes, then allows the caller to supply one. It reported that choice only when the choice **contradicted** detection's estimate — so it spoke when the caller was right and stayed silent when they were wrong, and the silent case destroyed the file. Measured on a UTF-16BE file that detection reads as little-endian, `-From utf-16le` converted it to the letters `A` and `B` repeated, with an empty reason field and exit 0.

The cause was one variable holding two things: `HasAmbiguousBomlessUtf16` named a fact about the file — this byte order cannot be proven — while being assigned the separate decision to refuse automatically. Supplying a source made the decision false, which erased the fact before the advisory could consult it.

An explicit source given for an unprovable byte order is now always reported, whether or not it agrees with the estimate, because agreeing with an estimate EC cannot prove is not corroboration. The two cases carry different reason codes: `ExplicitSourceOnUnprovableBomlessUnicode` is new and additive, so a run that previously produced an empty reason field may now produce a value.

Refusal without an explicit source, structurally provable byte orders, files carrying a byte-order mark, and non-UTF-16 sources are unchanged.

#### First run of the full verification sequence

Every gate the release checklist requires ran, in order, for the first time:

```text
coverage      both runs 4 corpora, 5,207 rows
provenance    all four run.json name the audited commit, dirty=False, one assembly
integrity     All invariants hold across 5078 rows          exit 0
comparison    changed=0 improved=0 regressed=0 lateral=0    exit 0
```

`check_audit_integrity.py` had never before validated a fresh audit; every earlier use was against stored runs. It is listed in `audit/README.md` as the step to run after a run and was skipped for v3.9.0, v3.9.2 and v3.10.0, during which it was itself broken. It became a checklist item after that was found.

### v3.11.0 audited build

v3.11.0 closes thirty-five findings from two independent reviews and was audited from a clean checkout of the commit it was tagged at.

```
commit    303c74bd829376b7bb1686a13f4f9bc9f065128f   (annotated tag v3.11.0)
worktree  clean
platform  .NET 10 - Windows 11 10.0.26200
assembly  EncodingChecker.dll
          55f80df09ae17e8a1ee9ceb22225a55287ad1b599c254ce9450945af8986a08f
run       rel3110, compared against rel3101 in audit/reports/
```

**No file changed outcome.** `changed=0 improved=0 regressed=0 lateral=0`, and all four metrics are identical to v3.10.1: detection 4640/4646, strict decoding 4694/4694, codec conformance 4591/4694, text preservation 4520/4623. `check_audit_integrity.py` reports all invariants holding across 5,078 rows.

**This result was predicted before the run and recorded before the numbers existed.** The corpus supplies each file's source codec from its own reference metadata on files that are not ambiguous, so a corpus run reaches none of what this release changes: the plan boundary, the command-line validation, the journal status table, or the GUI. `changed=0` therefore establishes that the release disturbed nothing the corpus can see, and establishes nothing whatever about whether the fixes work.

That rests on 633 tests, on each fix being mutation-checked in isolation — the change reverted, the intended test required to fail, the file restored byte-identical — and on the manual GUI smoke test below.

A release whose audit cannot reach it is worth stating plainly rather than quoting as confirmation. This is the second consecutive release for which that is true.

#### What changed in v3.11.0

Two reviewers read v3.10.1 independently and found thirty-five defects between them, overlapping on two. Neither found the other's most severe.

**Applying a plan could convert a file the plan recorded as refused.** A UTF-8 file converted with `-From windows-1252` is refused directly, because the explicit source contradicts a proven Unicode reading. The plan recorded that refusal. Applying it converted the file, exited 0, and wrote a journal saying the action had been `Convert`. The output is valid UTF-8 holding characters nobody wrote, so content verification cannot catch it: both sides decode through the same wrong codec. `PlannedFile` carried the detected codec but not `HasReliableUnicodeDetection`, which is a policy input rather than provenance, so the veto had nothing to fire on at apply time. The plan now records it, and a reviewed decision is a ceiling: re-deciding may refuse more than the review did, never less. Plan schema moves to 5 and semantics to 6, so earlier plans are refused.

**A blank option value was read as an absent option.** `-Plan ""` skipped the preview flag and performed a live conversion. Rejected now, along with seven other options, in the one place every option's value passes through.

**Applying a plan followed a root that had become a reparse point**, writing into a tree the reviewer never saw, while planning refused the same input. **A second conversion destroyed the first backup and left a recovery record positively describing it.** **A journal could overwrite the backup its own run had just created:** `-Backup -Journal <source>.bak` converted the file, verified the backup, replaced it with JSON, and exited 0.

**Read-only modes contradicted conversion.** `-DetectOnly` and `-Validate` reported a BOM-less UTF-16 byte order with full confidence that `-Target` refused seconds later; `-Validate` now reports it as `Invalid`, so `-FailOnChanges` returns 2 where it returned 0.

**The journal was wrong in both directions.** A file EC never opened was recorded as a policy refusal while the run exited 3; a file already replaced was recorded as untouched. Both corrected, with `ConvertedWithWarning` and `InstallationUnknown` added, and `NotAttempted` now reachable outside previews.

**Cancelling a conversion that had already written files left no record of those writes at all.**

Also: the GUI review dialog now shows the advisory v3.10.1 added for a source choice that *matches* an unprovable estimate — it had reached the CSV, the plan and the journal but never the screen, so it warned about the safer choice and stayed silent for the riskier; the refusal message names `utf-16le` and `utf-16be` rather than the ambiguous alias it had just declined to justify; EC's own backups and records are counted when a caller's patterns select them, so `-Include "*.bak"` no longer returns an empty successful report; and six documented-invalid option combinations are rejected instead of ignored.

#### Detector parity is now enforced rather than asserted

Every record from v3.9.0 onward states that parity held at the tagged commit. Those statements were true; nothing made them true. The job ran on pushes to `master` and weekly, so parity was verified only after a change had landed and never as a condition of a tag. It now runs on pull requests, and the release workflow will not publish without it — v3.11.0 is the first release to pass through that gate.

It also now covers `TextValidation.cs`, which these records name as shared but no workflow had ever compared. The two files are identical across all three repositories; that was luck rather than enforcement.

Two ways the comparison could have reported a difference that was not in the code were found and fixed while adding the second file. It read files using the host's default encoding, and one copy carries a non-ASCII character in a comment, so a host defaulting to the system codepage silently mangled the BOM-less copy. And it stripped a leading `using System;` before the byte-order mark rather than after, though one copy begins with a mark immediately followed by that line. Both were latent: the job passes under `pwsh` 7, whose defaults happen to be right. Running the same logic under Windows PowerShell 5.1 reported a divergence that does not exist, which is how they surfaced. A parity check for an encoding detector should not itself depend on an encoding default.

#### The integrity check reported more than it had verified

The first run of `check_audit_integrity.py` for this release printed `All invariants hold across 5078 rows` and exited 0 with **coverage and independent-hash sampling never having executed**. `CORPUS_ROOT` was unset, and the skip appeared only as a `note:` line among the passing sections. The statement was true and materially weaker than it read: coverage is the check the tool lists first, in its own words ordered by how badly a failure would mislead.

Re-run with the corpus root set, coverage passes 1,367 / 478 / 67 / chardet and independent hashes 150 / 150 / 150 / 64. The figures above are from that second run.

This is the fourth defect of this shape recorded here, and the second in the integrity checker itself. The pattern holds: a green result whose scope is narrower than its wording, visible only by reading what did not run.

#### Two of the fixes introduced defects that review caught

Codex reviewed the branch and found nine further defects, two of which this release had itself introduced.

The interrupted-run journal collected completions in a `HashSet` from a callback that `Parallel.ForEach` invokes concurrently — a data race in the code whose purpose is producing a truthful record. And a backup that failed before anything was written reached the new `InstallationUnknown` status, which had been added to stop the journal asserting what nobody had established, and asserted exactly that for a file nothing had touched.

Both were found by re-deriving invariants rather than by a failing test, and neither had a test that could have caught it before the fix.

#### The GUI smoke test

Phases A through E of the checklist passed, plus two added for this release.

Phase F covers the advisory for a source choice matching an unprovable estimate — the one behaviour in this release whose entire purpose is what appears on screen, and which no automated test can observe.

Phase G cancels a 600-file conversion partway. The exported journal was reconciled against the bytes on disk by filename, not by count: the 411 entries recorded `Converted` are exactly the 411 files whose byte-order mark was stripped, the 189 recorded `NotAttempted` are exactly the 189 left intact, every file appears once, and no `NotAttempted` entry carries an after-hash. That also exercises the concurrent completion tracking at real parallelism, which the automated test pins to one worker for determinism.

**Correction, after this record was published.** Both checks were described here as phases F and G. Those were working letters used while running them by hand, and they name nothing durable: the checklist at the time defined only A through E, and the automated suite that has since superseded it uses F and G for two different checks — a stale reviewed file, and a backup failure. The two described above are now phases **H** and **I** of that suite, documented in [GUI-SMOKE-TEST.md](GUI-SMOKE-TEST.md).

The wording above also said the advisory was something no automated test could observe. That was true when written and is no longer: phase H asserts on the review's rendered text. It is corrected here rather than edited away, because what a record claimed at the time is part of what the record is for.

#### Known limits specific to this release

- Window-position restore is covered by unit tests against synthetic monitor layouts. It has not been exercised against a real display change.
- EC's journal, plan and recovery sidecar escape every non-ASCII character to `\uXXXX`. This is valid JSON that round-trips exactly, so nothing is lost or misstated, but these are records meant to be read by a person and EC's domain is non-ASCII text. Deferred to v3.11.1 rather than changing three writers after the audit had run.
- The recovery sidecar's `SourceTextSha256` and `OutputTextSha256` now come from two separate measurements rather than one value written twice. No test can tell the difference, because verification has already established the two are equal; a mutation copying the source digest back into the field passes the whole suite. That was confirmed rather than assumed. The fallback was replaced with a throw, so a silent revert is impossible.

### v3.11.1 — `883cf1f2ef019845043d03b87e7a9a458cc07e85`, not re-audited

A readability and correctness release for what EC *says*, not what it converts. **It was not measured against the four corpora.** The checklist requires a corpus run for a release that changes detection or conversion policy; this changes neither, so no audited build exists and no assembly hash is quoted. The v3.11.0 figures above are not evidence about this release.

The published archives:

```
EncodingChecker-3.11.1-framework-dependent.zip
  825ed4f695e803f2de0bc0defdea2abbb123a1199f3d87aa64c1b4462b6e43cf
EncodingChecker-3.11.1-win-x64-self-contained.zip
  c787072019b4d12bcbb7dfd5d5a7f01e17a49a42efafef05d4ef630acece8abd
```

#### What changed in v3.11.1

| | |
|---|---|
| The journal, plan, and recovery sidecar | Wrote every non-ASCII character and every apostrophe as `\uXXXX`. A recovery record for a Japanese or Arabic filename could not spell it. |
| The About box | Claimed MPL 1.1 while the project ships 2.0; credited `ude` while linking the library actually used; linked a CodePlex domain that no longer resolves. |
| Attribution | `AssemblyCompany` names the current maintainer. The copyright notice adds him beside the original author rather than replacing him, which MPL 2.0 §3.4 requires. |
| The GUI smoke suite | Refuses a build without the review dialog's automation ids instead of running every phase and reporting the absence as a fault in EC. |

None of this alters a converted file. Conversion semantics stay at 6, the plan schema at 5, the journal schema at 4, and v3.11.0 artifacts still load.

#### What was verified, and by what control

Verified on the tagged commit: CI and the shared-detector parity job both pass, and parity is checked *inside* the release job rather than asserted — `UnicodeDetector.cs` and `TextValidation.cs` were confirmed identical across all three repositories at the commit being released. The full suite passes 637/637 with none skipped and no warnings, and the workflow independently asserts the built `--version` matches the tag.

The three new tests assert on each file's raw bytes rather than a deserialized value. That distinction is the whole point: the escaped JSON was always valid and always round-tripped, so a test that deserialized would have passed against the defect. Each was mutation-checked — with the encoder line removed all three fail, and the files were restored byte-identical.

The smoke suite's new refusal was checked in both directions, because a guard only ever seen to stay quiet has not been shown to work: against the published v3.11.0 executable it exits 2 without running a phase, and against this build all nine phases still pass.

#### The GUI smoke test

Nine phases pass against the Release build, with `EcVersion 3.11.1.0` recorded in the evidence, so the report names the build it drove. Phase I converted 36 of 400 files before the cancellation landed, which is what distinguishes a genuine interruption from a run that finished first — the phase passes either way, so the count, not the result, is the evidence.

**This is the first release the suite can run against.** The automation ids it drives were added after v3.11.0 was tagged, so no earlier release can be driven by it, and none was.

#### Known limits specific to this release

- **No corpus measurement backs this release.** The v3.11.0 result establishes that the conversion engine was undisturbed *as of that commit*; it says nothing about this one. What supports v3.11.1 is the unit suite, the GUI suite, and the parity check — not a measurement over 5,078 files.
- **The GUI suite ran locally, not in CI.** Whether a GitHub-hosted Windows runner gives UI Automation an interactive window station is still unestablished, so this gate depends on a person running it before tagging.

  *Corrected after this record was written.* A hosted `windows-latest` runner does provide such a session: `Environment.UserInteractive` is `True`, and phase A drove the review to completion on one. The limit above stands as written for v3.11.1 — the suite did run locally for this release, and no CI gate existed — but the question it calls open is now answered, and a later release can be gated on it.
- **The About box was verified by reading it, not by a test.** No automated check asserts its wording, and each corrected label is a `LinkLabel` whose clickable span is a character range: a later text edit can move a link onto the wrong words without failing anything.

### v3.11.2 — `a77d6dda99560106773c4a5ef1f7327a5061e244`, not re-audited

**It was not measured against the four corpora.** The checklist requires a corpus run for a release that changes detection or conversion policy; this changes neither, so no audited build exists and no assembly hash is quoted. The v3.11.0 figures are not evidence about this release.

The published archives:

```
EncodingChecker-3.11.2-framework-dependent.zip
  ff3f476b40eacbe2aa5b9144912b6caab557aff0ac1fe74368c636021513ac02
EncodingChecker-3.11.2-win-x64-self-contained.zip
  2236dd2461081ef7c51401f6733633359b99d3667e8f365737f6bd446b993948
```

#### The first release the GUI suite gated

Every earlier record could say only that the nine phases passed somewhere before the tag. This one can say they passed *inside the release job*, against the published executable, after packaging and before publication — 646 tests and nine phases in the same run that produced the archives above. A failure would have stopped the release rather than been noticed afterwards.

That closes a gap these records have carried since v3.11.0: the suite was evidence a person chose to gather, and is now evidence the pipeline cannot skip.

#### What changed in v3.11.2

| | |
|---|---|
| A plan rooted at a drive | Was refused whole, every file reported as escaping the plan's own directory. The containment prefix appended a separator to a root that already ended in one. |
| A source choice that cannot be applied | Was dropped in silence, and the run reported a cancellation nobody asked for. It is now refused, in place, with the reason. |
| Conversion parallelism | Raised from 4 to 8; 1.5 to 1.7 times faster, measured. |
| Plan and sidecar reading | Now uses the same options object the writer uses. |

#### What this release says about these records

The drive-root defect **shipped broken in v3.11.0 and v3.11.1**. It was found before v3.11.0, recorded as Confirmed, and reported as closed. Both of those records were written while it was live, and neither caught it, because both trusted a summary of what had been fixed rather than the source.

Nothing in the audit method would have found it either: no corpus run reaches the plan boundary, and the GUI suite has no phase for a drive-root base path. It was rediscovered by accident during an unrelated review. `docs/DEFECT-BACKLOG.md` exists because of that, and records every finding's status re-derived from the code rather than carried forward.

#### Known limits specific to this release

- **No corpus measurement backs it.** What supports it is the unit suite, the GUI suite, the parity check, and a mutation check on each fix.
- **Code signing did not run**, because the signing secrets are not configured, so the GUI suite drove an *unsigned* published executable. The step is correctly gated on the secrets being present; the claim that the suite drives the signed binary remains unproven.
- **One fix has no test.** Coupling the JSON reader to the writer's options changes nothing observable today, and no test can demonstrate it without adding a setting to production code purely to make one fail. Every other fix here was mutation-checked; this one is stated instead.
- **The source-choice refusal is covered by a unit test, not a GUI phase.** A smoke phase for it was attempted and abandoned: the automation driver cannot select a combo inside that dialog, which is a defect in the driver rather than in EC. Both the defect and the fix were reproduced by hand in the window.

### v3.12.0 — `2878a8ef8ab66e5a6b143822ff93c0268c24bcb8`, not re-audited

**It was not measured against the four corpora, and unlike v3.11.2 this is a release that asks for one.** The checklist requires a corpus run for a release changing detection or conversion policy. Detection is untouched — no detector file changed, and the parity workflow passed at this commit — but `ConversionPolicy` did change, so the exemption the previous record claimed is not available here. No audited build exists and no assembly hash is quoted. The v3.11.0 figures are not evidence about this release.

The published archives, each downloaded and hashed rather than trusting GitHub's own report of them:

```
EncodingChecker-3.12.0-framework-dependent.zip
  b48b95692d432106387519b08639240adbd1cac10c5054db58343f5d45d18b18
EncodingChecker-3.12.0-win-x64-self-contained.zip
  f94d4618f04d8fcf186c187dbe8e6feda5f1d3c2869fb79dd5968ed0dcc4b917
```

#### What changed in v3.12.0

| | |
|---|---|
| A target named by an alias | Rewrote every file in a tree already in that codec to identical bytes, resetting every modification time. Identity was the charset's label; it is now the resolved code page. |
| "Already in the target encoding" | Was a whole-file claim made from a 64 KiB detection sample. The whole file is now validated first, at 0.04–0.10 ms per MiB. |
| A preview of a conversion | Promised conversions that would fail, and recorded them in a plan as approved, because nothing decoded the file. The source is now decoded and the entry marked `Refuse`. |
| One file's failure | Could end the whole run. The per-item catch named four exception types; it now excludes only cancellation and `OutOfMemoryException`. |
| An unreadable folder, and a folder skipped by name | Were counted nowhere. Both are counted now. The exit code is deliberately unchanged. |
| A `-Validate` rejection, and a refusal | Could carry no reason, or a reason re-derived beside the decision that already knew it. Both now come from one place. |
| A decode failure | Reported an offset relative to a read chunk, which could be negative. It names the offending bytes instead. |
| Standard output | Was UTF-8 whatever the console was. It now uses the console's encoding; redirected output stays UTF-8. |

#### What this release says about these records

Twelve findings, in a codebase that had already been through two independent reviews and a backlog re-derived from the source rather than from a summary. Eleven were new to that file.

What is worth recording is *why the method missed them*. The corpus harness does convert, and it compares decoded source text against strict output, so it is not blind to conversion. All three defects that touched files still sat outside its reach, each for a different reason. The alias defect **preserves text perfectly** — it rewrites a file to identical bytes, so every comparison the harness makes passes while a modification time is silently lost. The 64 KiB defect needs a file valid for 64 KiB and invalid afterwards, a shape no corpus contains because corpora hold files with authoritative metadata. The preview defect lives in `-Plan`, a mode the harness does not run. A corpus run at full marks would have said nothing about any of the three.

The review that found them also over-rated four of its own findings, and the reader caught each one rather than the review catching itself. The pattern was single: a mechanism proved with an input built to prove it, then described for significance without checking what the product does with such an input. Two were downgraded after measurement against realistic files, one was withdrawn once its fix was shown to break four existing tests, and one was withdrawn once what EC actually reports was checked. That belongs in this file because it is the failure these records exist to catch — a claim true about a mechanism and false about the product.

#### Known limits specific to this release

- **No corpus measurement backs it, and the checklist asked for one.** What supports it is 727 unit tests, the nine-phase GUI suite, the detector parity check, and a mutation check on each fix — the change reverted, the intended test required to fail, the file restored byte-identical and confirmed by hash.
- **Code signing did not run.** The signing secrets are still not configured, so the step was skipped, the archives above are **unsigned**, and the GUI suite drove an *unsigned* published executable. This is the second release to carry the limit unchanged, and the claim that the suite drives the signed binary remains unproven.
- **The preview fix decodes and no more.** A target that cannot represent the source text still fails at conversion time, which reading the source cannot predict. A test named for that case pins the limit rather than hiding it.
- **A run that could not read part of the tree still exits 0.** Unreadable directories are counted and reported now, but the exit code was deliberately left alone; a script that must fail on them has to read the coverage line.
- **Four findings from the same review are open**, the first a silent corruption: a BOM-less UTF-16 file whose every other code unit is a C0 control can be detected as UTF-32 and converted. Output verification cannot catch it, because both sides of the comparison use the same wrong codec. Measured over nineteen realistic file shapes — 41 of 44 detect correctly, and the three that do not are the same degenerate shape. `docs/DEFECT-BACKLOG.md` scores it critical impact, low reach.
- **No accessibility spot check is recorded for this release.** The checklist's scaling, keyboard-only, and high-contrast checks have no automated substitute, and nothing in the release job stands in for them.

#### Audited afterwards, on 2026-09-08

**The statement above stands: v3.12.0 shipped without a corpus run.** Users downloaded
artifacts that no such evidence supported. That is not edited away here, and the heading is
unchanged, because the release page links to it and because it is what happened.

What has changed is that the run has since been made, against the commit this release was
tagged at:

```
commit    2878a8ef8ab66e5a6b143822ff93c0268c24bcb8   (annotated tag v3.12.0)
worktree  clean
platform  .NET 10.0.400 - Windows 11 10.0.26200
assembly  EncodingChecker.dll
          619d384057823932483f5d55be73ae037ecc1bc750fe722b76ee52e243693094
run       v3120retro, compared against rel3110 in audit/reports/v3120retro-vs-rel3110
```

**No file changed outcome.** All four metrics are identical to v3.11.0 and to the v3.13.0
run, with zero implementation defects, zero strict-decode throws and zero backup-integrity
mismatches per corpus.

| | v3.11.0 | v3.12.0 (retro) |
|---|---|---|
| Detection accuracy | 4640/4646 (99.87%) | 4639/4645 (99.87%) |
| Strict decoding | 4694/4694 (100.00%) | 4693/4693 (100.00%) |
| Codec conformance | 4591/4694 (97.81%) | 4590/4693 (97.81%) |
| Text preservation | 4520/4623 (97.77%) | 4519/4622 (97.77%) |

`compare.py` reports `regressed=1`, and it is not one: a UTF-8 fixture present when v3.11.0
was audited is absent from the corpus copy on disk, so it joins as `(absent)`.
`check_audit_integrity.py` was run with `CORPUS_ROOT` set and all invariants hold across
5,077 rows. Source corpora re-hashed afterwards by the same method used for v3.13.0.

**This covers the artifact that shipped, not only the source.** The published single-file
executable was reproduced byte-for-byte from the tagged commit:

```
shipped by the v3.12.0 release  f0bf528d2345ea2dccbeb6cdefa8c0c8ceaf73b0c541b843436c61405344d1e5
rebuilt locally from 2878a8e    f0bf528d2345ea2dccbeb6cdefa8c0c8ceaf73b0c541b843436c61405344d1e5
```

So this is evidence about what users downloaded, not merely about a commit that resembles
it. The audit itself measures the plain Release build rather than the published one, as
every audited-build record here does; the reproduced hash is what ties the two together.

**It does not retire the caution this record was written with.** The harness supplies an
explicit source for every file, so it cannot reach anything that depends on automatic
detection choosing wrongly. A clean result means the conversions it performed preserved
text; it is not a statement that every decision path in v3.12.0 was exercised.

**Why it was run at all.** v3.13.0 required a corpus run, which made v3.12.0 the only
release in this line whose conversion-policy change had shipped with no evidence behind it.
Running it late is worth more than leaving the gap, and less than running it on time. Both
halves of that belong in the record.

### v3.12.1 — `36983998124ab1caabb2f4fc10ba5bb2a83649b7`, not re-audited

**It was not measured against the four corpora, and none is required.** This release changes neither detection nor conversion policy: what EC converts, refuses and reports for a valid input is what v3.12.0 did. No audited build exists and no assembly hash is quoted.

**The gap the v3.12.0 record opened is untouched.** That release *did* change `ConversionPolicy` without a corpus run, and nothing here closes it. Two records in a row saying "none required" must not be read as the requirement having been met.

The published archives, each downloaded and hashed rather than trusting GitHub's own report of them:

```
EncodingChecker-3.12.1-framework-dependent.zip
  d3aab1207e2e95ff32c6523b5ad7aa487f594425c3f5a47ce62a179c0470b512
EncodingChecker-3.12.1-win-x64-self-contained.zip
  1b21967de3e130fead173936018b6fb38bc50371fb22404e9f11f5d972b8a4dc
```

#### What changed in v3.12.1

| | |
|---|---|
| An unwritable `-Journal` or `-Report` destination | Was discovered after conversion had rewritten the files, so a user who asked for a journal ended with changed files and no record of the change. Refused before the run now. Exit code deliberately unchanged at 3. |
| A plan carrying an action no build wrote | Loaded without complaint, reached a mapping whose fallback was `Converted`, and produced exit 0 with a journal asserting work that never happened. Refused at load; the mapping throws. |
| EC's own plan, journal, report and settings | Truncated their destination and wrote into it, while converted files and recovery sidecars were installed atomically. All four now use the mechanism that already shipped. Closes EC-16. |
| The source-choice refusal | Was the only fix carried by a unit test instead of a smoke phase. Smoke phase J now drives it against the built application. |
| The defect backlog | Re-derived from source into a ledger of 61 findings organised by status, with a checker that recomputes every figure in its header. |

#### What this release says about these records

Almost nothing in it was a conversion defect. Two were, both narrow. The rest was
instruments — a smoke phase that could not be written, a backlog whose counts had
drifted, a checker written to stop that drift. And **every instrument in this release was
found broken by being used.**

The backlog's summary had drifted three times, the third while correcting the second. `CX-07` recorded a fix that was never made and should not be, contradicting `docs/CLI.md`, which had said the opposite in the shipped documentation the whole time. The smoke driver could not reach the phase it was needed for, and had been recorded as an open defect rather than fixed. The checker written to end the drift then failed three ways on a stock machine — it named a shell that is not installed, it read `$PSScriptRoot` where Windows PowerShell does not populate it, and it was refused outright by an execution policy — and each of the three was hidden by the way the previous check had been run.

That last one is the sharpest. Every check of the checker ran in a session carrying `PSExecutionPolicyPreference=Bypass` at process scope, which child processes inherit, so the execution policy could not fail however it was invoked. The defect was reachable only by clearing that variable first, which is how it was finally verified — after a user ran the documented command and it did not work.

The rule this file already states about the product applies to the things that check the product: **a claim is worth what the method behind it is worth.** An instrument nobody has seen fail, or has only seen run on the convenient path, is not evidence yet.

#### Known limits specific to this release

- **No corpus measurement backs it**, and none is required. What supports it is 742 unit tests, the ten-phase GUI suite, the detector parity check, and a mutation check on each fix — the change reverted, the intended test required to fail, the file restored byte-identical and confirmed by hash.
- **Code signing did not run.** The signing secrets are still not configured, so the step was skipped, the archives above are **unsigned**, and the GUI suite drove an *unsigned* published executable. This is the third release to carry the limit unchanged.
- **The driver fix removes an offscreen filter in one search and keeps it in the other**, deliberately: a process-wide match on a hidden item could belong to a different collapsed combo holding the same encoding name. That asymmetry is documented in the code, and is the kind of thing a future phase could still trip over.
- **Three backlog findings remain only partly reproducible** and are recorded as such: one needs force-close timing too precise to trigger, one needs a filesystem where `File.Replace` is unsupported, and the historical performance figures were not re-measured.
- **No accessibility spot check is recorded** for this release. The checklist's scaling, keyboard-only, and high-contrast checks have no automated substitute, and nothing in the release job stands in for them.

### v3.13.0 audited build

The first release since v3.11.0 measured against the four corpora, and the first that had
to be: it changes conversion policy, so the exemption v3.11.2, v3.12.0 and v3.12.1 claimed
does not apply. Audited from a clean detached checkout of the commit it is tagged at.

```
commit    08858a68b31cbe769d476e605b2573c4a048a79b   (annotated tag v3.13.0)
worktree  clean
platform  .NET 10.0.400 - Windows 11 10.0.26200
assembly  EncodingChecker.dll
          b5761523da3af57072f735be93f369c2c3a6445cb25106d3328d219c4329869f
run       v3130tag, compared against rel3110 in audit/reports/v3130tag-vs-rel3110
```

**No file changed outcome.** All four metrics are unchanged from v3.11.0, with zero
implementation defects, zero strict-decode throws and zero backup-integrity mismatches per
corpus.

| | v3.11.0 | v3.13.0 |
|---|---|---|
| Detection accuracy | 4640/4646 (99.87%) | 4639/4645 (99.87%) |
| Strict decoding | 4694/4694 (100.00%) | 4693/4693 (100.00%) |
| Codec conformance | 4591/4694 (97.81%) | 4590/4693 (97.81%) |
| Text preservation | 4520/4623 (97.77%) | 4519/4622 (97.77%) |

`compare.py` reports `regressed=1`, and it is not one. A UTF-8 fixture present when
v3.11.0 was audited is **absent from the corpus copy on disk**, so it joins as `(absent)`
rather than as a changed outcome. The local UnicodeTestSuite holds 1,366 files where the
published v3.0 holds 1,367. Every file present in both reached the same outcome.

`check_audit_integrity.py` was run **with `CORPUS_ROOT` set** — the failure the v3.11.0
record describes, where coverage and independent-hash checks are skipped and the run still
prints that all invariants hold. Coverage 3166 / 478 / 67 / 1366, ground truth
729 / 47 / 41 / 35, independent hashes 150 / 150 / 64 / 150: 514 files decoded and
compared from scratch, outside the audit's own code path. All invariants hold across
5,077 rows.

**The source corpora were not modified.** Verified after the run by re-hashing all 5,077
source files against the inventory captured before it: zero modified, zero missing.

#### The audited build and the shipped build

This release links the two by measurement rather than by assertion, which no earlier record
does.

The published single-file executable was **reproduced byte-for-byte** from the same clean
checkout, on a different machine from the one that built it:

```
shipped by the release workflow  76f68a876b3385a711fe967b2243ade80109bb204d9d851f885912727e2760cc
rebuilt locally from 08858a6     76f68a876b3385a711fe967b2243ade80109bb204d9d851f885912727e2760cc
```

The distinction that remains, and that every audited-build record here shares: **the audit
measures the plain Release build, not the published one.** The harness invokes
`bin/Release/net10.0-windows/EncodingChecker.exe`, while the release ships a `win-x64`
single-file publish whose bundled managed assembly is
`ba66a3438883963caaa083400ee10e0aa4599074dedb22e6978503c9a7f3f1b4`. Same commit, same SDK,
different packaging. The reproduced executable hash is what ties the audited source to the
shipped artifact; the assembly hash above identifies what the corpora were actually run
against.

#### What this audit cannot establish

**It does not exercise the change this release makes.** The harness runs with a forced
reference, supplying `-From` for every file, and v3.13.0 refuses BOM-less UTF-32 only when
detection is *automatic*. `UnprovableBomlessUtf32` appears **zero times across 5,077
files**.

That belongs in the same breath as the clean result. A green audit on the first release in
this line to change conversion policy reads as confirmation of the change, and it is not.
It confirms that nothing else broke. What the change does rests on the unit and end-to-end
tests, which drive the automatic path directly.

The audit does measure the way out: 847 files carrying the BOM-less-doubt advisory were
converted with an explicit source and **all 847 preserved their text exactly**. The escape
hatch this release directs users to is measured at scale; the refusal that sends them there
is not.

**Detection is unchanged, and was checked rather than assumed.** Both detection testers
produced reports byte-identical to the committed ones — 1,358 UnicodeTestSuite files and
3,137 chardet files. Their headline accuracy is scored through an "also valid as"
equivalence, so those percentages are used here only as a before-and-after identity check,
never as ground truth.

#### The GUI smoke evidence records no managed-assembly hash

`RELEASE-CHECKLIST.md` states that the report carries "the executable and managed-assembly
hashes", and several records above repeat it. The executable hash is real. The managed one
is **absent**: `gui-smoke-report.json` has no `EcManagedAssemblySha256` key, and the
Markdown renders an empty pair of backticks, while both still name the
`EncodingChecker.dll` path as though the value were there.

It is absent because a single-file publish leaves no loose DLL at that path. That has been
true since single-file publishing began, so **v3.12.0 and v3.12.1 carry the same empty
field** and the claim in their records is wrong in the same way. Filed rather than fixed
here; this release changes no release tooling.

#### What changed in v3.13.0

| | |
|---|---|
| A BOM-less UTF-16 file that also decodes as UTF-32 | Was converted, rewriting U+0041 U+000A as U+A0041 with exit 0. Output verification could not notice: both sides of its comparison used the same wrong codec. Refused now. |
| BOM-less UTF-32 valid under both byte orders | Detection preferred little-endian without saying so. Refused now. |
| Ordinary BOM-less UTF-32 | Refused as well. Nothing in the bytes separates it from the first row, so no test admits one and rejects the other. A BOM or `-From` still converts it. |
| Conversion semantics | 6 to 7. Plans written by v3.12.1 or earlier are refused rather than applied. |

Widening the guard that protects BOM-less UTF-16 would not have closed the first row: those
bytes are invalid under the opposite UTF-32 order, so an opposite-order test passes them
through. What the bytes fail to establish is the codec, not merely its byte order.

No scalar classification changed. Rejecting unassigned or private-use scalars would have
been the smaller-looking fix and would have broken icon fonts, which put private-use
characters in ordinary text files. `TextValidation.cs` and `UnicodeDetector.cs` are
untouched.

#### Known limits specific to this release

- **The audit cannot reach the changed path**, as described above.
- **Code signing did not run.** The signing secrets are still not configured, so the
  archives are unsigned and the GUI suite drove an unsigned executable. Fourth release
  running.
- **The GUI evidence carries no managed-assembly hash**, as described above.
- **The corpus copy is one file short** of the published UnicodeTestSuite v3.0.
- **No accessibility spot check** is recorded for this release.
- **The v3.12.0 gap is not closed by this release.** That build changed `ConversionPolicy`
  and shipped without a corpus run; nothing here re-audits it. Its record stands as
  written.

## Known limits

- No detector can recover an author's historical legacy encoding when the same bytes admit multiple plausible readings. EC refuses automatic legacy conversion instead of guessing.
- Some named legacy codecs have legitimate mapping/profile differences across implementations. An explicit source choice specifies the .NET profile EC will use; strict conversion still verifies that profile's text round trip.
- The final hash check reduces concurrent-writer risk but cannot make a filesystem replacement fully race-free without holding source handles against writers for the entire operation.
- `-Backup` is optional in the CLI for scripting. Use `-Backup` or the plan workflow when an in-place conversion must be recoverable.
