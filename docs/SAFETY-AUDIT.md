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

EC therefore has a simple policy:

| Source interpretation | Automatic conversion |
| --- | --- |
| Unicode or ASCII | Allowed |
| Legacy codec supplied explicitly by the user | Allowed, subject to all safety checks; rejected if it conflicts with a fully validated Unicode reading |
| Legacy codec detected automatically | Refused; choose the source codec first |
| Unknown or unreadable source | Not converted |

`-From` and the GUI source chooser replace detection only. They do not bypass strict decoding, output verification, backup verification, or atomic installation.

## Plans, confirmation, and recovery

`-Plan` writes a conversion plan without changing files. Detection and hashing read the same source snapshot. The plan contains those source hashes, paths relative to its declared root, target and BOM policy, source-selection mode, backup setting, and conversion-semantics version.

`-Apply` rejects changed, missing, relocated, or incompatible planned work as a whole; it does not silently apply the remaining files. EC also rechecks the source hash immediately before installation. That narrows, but cannot eliminate, a narrow concurrent-writer TOCTOU window between the final check and replacement.

The GUI uses the same policy and plan model. It displays a review before writing, and a changed source while that review is open invalidates the run.

With backups enabled, each conversion has a portable `<file>.ecmeta.json` sidecar. The sidecar records the source codec actually used, whether it was detected or explicitly selected, source and backup hashes, target/BOM policy, conversion timestamp, and version. The backup and sidecar provide independently verifiable recovery metadata; EC does not currently provide a restore command.

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

## Known limits

- No detector can recover an author's historical legacy encoding when the same bytes admit multiple plausible readings. EC refuses automatic legacy conversion instead of guessing.
- Some named legacy codecs have legitimate mapping/profile differences across implementations. An explicit source choice specifies the .NET profile EC will use; strict conversion still verifies that profile's text round trip.
- The final hash check reduces concurrent-writer risk but cannot make a filesystem replacement fully race-free without holding source handles against writers for the entire operation.
- `-Backup` is optional in the CLI for scripting. Use `-Backup` or the plan workflow when an in-place conversion must be recoverable.
