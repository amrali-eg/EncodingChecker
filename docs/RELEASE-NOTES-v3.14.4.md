# EncodingChecker v3.14.4

Zero product changes. This release updates the legacy-encoding detection library and
fixes a misleading comment; what EC detects, converts, refuses, or reports has not moved.

## What changed

- `UTF.Unknown` (the legacy charset detection library) is updated from 2.6.0 to 2.7.0,
  which adds .NET 10 support and a `DetectFromBytes(ReadOnlySpan<byte>)` overload.
  `TextEncoding.cs` now calls that overload directly instead of first copying the
  sample into a `byte[]`. Detection policy is unchanged.
- Fixes **EC-17**: the comment beside the control/private-use character check in
  `TextValidation.cs` said these characters are "ignored" in the printable-text ratio.
  They are not — they count toward the denominator but never the numerator, which
  lowers the ratio. Comment-only; the calculation itself is untouched. This file is
  byte-identical (parity-checked) across EncodingChecker, LineEndingNormalizer and
  CorpusTesters, so the same fix landed in all three repositories.

A package update to a detection dependency is not assumed behavior-neutral just
because its own release notes don't advertise detector changes — see Verification.

## Compatibility

Nothing changes. Conversion semantics stay at **7**, the plan schema at **6**, the
journal schema at **6**. Plans and journals from any 3.14.x release still apply
unchanged.

## Verification

- 851 tests pass, none skipped; release build with no warnings
- The GUI smoke suite passes all ten phases against an ordinary Release build
- `docs/Test-DefectBacklog.ps1` passes with unchanged counts
- The **Shared Unicode detector parity** check confirms `TextValidation.cs` and
  `UnicodeDetector.cs` are still identical across EncodingChecker, LineEndingNormalizer
  and CorpusTesters
- **The four-corpus regression audit was run**, because this release changes a
  detection dependency. 5,078 files across UnicodeTestSuite v3.0, chardet
  `test-data`, the char-dataset corpus, and UTF.unknown's own 2.6 test corpus were
  compared between a build on UTF.Unknown 2.6.0 (baseline) and a build on 2.7.0
  (this release): **0 changed / 0 improved / 0 regressed / 0 lateral** outcomes across
  all four fidelity metrics (DetectionAccuracy, StrictDecoding, CodecConformance,
  TextPreservation). Source corpora were left unmodified; the audit's own integrity
  checks reported zero defects on both runs.
