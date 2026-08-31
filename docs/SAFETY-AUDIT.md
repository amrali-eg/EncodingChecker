# EncodingChecker safety and audit

This document is the technical companion to the main [README](../README.md). It explains what EC's conversion pipeline guarantees, what it does not guarantee, and how those claims are checked independently.

## Conversion safety boundary

For each file that EC is allowed to convert, the engine:

1. strictly decodes the source encoding;
2. strictly encodes the requested target encoding into a temporary file;
3. strictly decodes that temporary output and compares the exact Unicode scalar sequence with the source text;
4. if backups are enabled, creates and verifies `<file>.bak` plus recovery metadata;
5. installs the verified temporary file atomically where the platform supports it.

Any decode, encode, verification, backup, or write failure leaves the source file unchanged. No normalization, case folding, whitespace rewriting, or replacement-character fallback is used to make a conversion appear successful.

The source is not rewritten in place. File attributes and timestamps are applied to the temporary output before installation. EC skips its own backups and temporary files on subsequent scans.

## Source-encoding policy

Encoding identification and text preservation are different questions. A sequence of legacy bytes often cannot prove which historical single-byte code page produced it.

EC therefore has a simple policy. For a file that needs conversion:

| Source interpretation | EC's action |
| --- | --- |
| Unicode or ASCII detected automatically | Convert if needed |
| Legacy codec selected explicitly by the user | Convert if needed, subject to every safety check; refuse if the choice conflicts with fully validated UTF-8 or BOM-confirmed UTF-16/32 |
| Legacy codec detected automatically | Refuse conversion until you choose or confirm the source codec |
| Unknown or unreadable source | Do not convert |

A file that already matches the target encoding and BOM is reported as **Unchanged** and
is not decoded or rewritten. No source choice is needed because no conversion occurs.

`-From` and the GUI source chooser replace detection only. They do not bypass strict decoding, output verification, backup verification, or atomic installation.

## Plans, confirmation, and recovery

`-Plan` writes a conversion plan without changing files. Detection and hashing read the same source snapshot. The plan contains those source hashes, paths relative to its declared root, target and BOM policy, source-selection mode, backup setting, and conversion-semantics version.

`-Apply` rejects changed, missing, relocated, or incompatible planned work as a whole; it does not silently apply the remaining files. EC also rechecks the source hash immediately before installation. That narrows, but cannot eliminate, a narrow concurrent-writer TOCTOU window between the final check and replacement.

The GUI uses the same policy and plan model. It displays a review before writing, and a changed source while that review is open invalidates the run.

With backups enabled, each conversion has a portable `<file>.ecmeta.json` sidecar. The sidecar records the source codec actually used, whether it was detected or explicitly selected, source and backup hashes, the expected converted-file hash, target/BOM policy, preparation state, timestamp, and version. It is written as `Prepared` before installation and changed to `Completed` only after installation succeeds. If a run stops between those steps, the current file's hash shows whether it is still the original or the verified output. The backup and sidecar provide independently verifiable recovery metadata; EC does not currently provide a restore command.

`-Journal` provides the batch-level record: EC's detected source, the source codec actually selected, whether that selection was explicit, any canonical-code-page disagreement between the two, the policy decision, planned action, actual outcome, stable reason code, diagnostic, and before/after hashes for every file—including skipped and refused ones. The GUI exports the immutable journal returned by the completed run rather than reconstructing history from mutable controls.

## Strict-codec defect fixed in v3.6.0

The independent audit found that assigning `Decoder.Fallback` or `Encoder.Fallback` after calling `GetDecoder()` or `GetEncoder()` does not reliably make .NET `CodePagesEncodingProvider` codecs strict. Some malformed legacy input could be silently substituted while EC's old downstream content check still reported success.

EC now constructs strict code-page encodings with exception fallbacks at `Encoding.GetEncoding(...)` construction time. Permanent regression tests cover the previously permissive decoder and encoder paths.

## Independent audit

[CorpusTesters](https://github.com/amrali-eg/CorpusTesters) is a separate, reproducible audit harness. It runs EC against four public corpora:

- [UnicodeTestSuite](https://github.com/amrali-eg/UnicodeTestSuite)
- [chardet test-data](https://github.com/chardet/test-data)
- [char-dataset](https://github.com/Ousret/char-dataset)
- [UTF-unknown](https://github.com/CharsetDetector/UTF-unknown)

It operates on working copies, never source corpora. For each file with authoritative metadata, it compares the exact decoded source text against strict UTF output. It also verifies backup hashes, inventories every file, runs mutation controls, checks codec strictness, and keeps per-file CSV/JSON evidence.

The audit distinguishes detection identity, text-equivalent labels, unsupported or unscored material, mapping/profile differences, and end-to-end text preservation. It does **not** treat one runtime's legacy mapping table as a universal authority: its sampled independent-implementation comparison is recorded separately, and mapping differences remain explicitly qualified.

Current raw artifacts, methodology revisions, and results are published with CorpusTesters. Historical corpus figures must be read in their recorded taxonomy and build context; they are not a substitute for the current product policy above.

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

## Known limits

- No detector can recover an author's historical legacy encoding when the same bytes admit multiple plausible readings. EC refuses automatic legacy conversion instead of guessing.
- Some named legacy codecs have legitimate mapping/profile differences across implementations. An explicit source choice specifies the .NET profile EC will use; strict conversion still verifies that profile's text round trip.
- The final hash check reduces concurrent-writer risk but cannot make a filesystem replacement fully race-free without holding source handles against writers for the entire operation.
- `-Backup` is optional in the CLI for scripting. Use `-Backup` or the plan workflow when an in-place conversion must be recoverable.
