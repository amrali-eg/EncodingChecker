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
- **The About box was verified by reading it, not by a test.** No automated check asserts its wording, and each corrected label is a `LinkLabel` whose clickable span is a character range: a later text edit can move a link onto the wrong words without failing anything.

## Known limits

- No detector can recover an author's historical legacy encoding when the same bytes admit multiple plausible readings. EC refuses automatic legacy conversion instead of guessing.
- Some named legacy codecs have legitimate mapping/profile differences across implementations. An explicit source choice specifies the .NET profile EC will use; strict conversion still verifies that profile's text round trip.
- The final hash check reduces concurrent-writer risk but cannot make a filesystem replacement fully race-free without holding source handles against writers for the entire operation.
- `-Backup` is optional in the CLI for scripting. Use `-Backup` or the plan workflow when an in-place conversion must be recoverable.
