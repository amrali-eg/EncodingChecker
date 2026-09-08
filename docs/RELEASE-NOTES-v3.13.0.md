# EncodingChecker v3.13.0

Two silent-corruption paths closed by one rule: BOM-less UTF-32 is no longer converted on
detection alone. This is the first release since v3.12.0 that changes conversion policy, the first in
this line to advance the conversion semantics version, and the first since v3.11.0
measured against the four corpora.

## BOM-less UTF-32 is refused rather than guessed

Two findings, one cause.

**A BOM-less UTF-16 file could be converted as UTF-32.** Text with one character per
LF-terminated line puts a C0 control in every second code unit, so each four-byte group is
an in-range unassigned scalar and the whole file decodes as valid UTF-32. EC converted it
and wrote **different text** — U+0041 U+000A became U+A0041 — with exit 0 and no warning.
Output verification could not notice, because it decodes the source with the codec EC
chose and the output with the target codec, so both sides of its comparison used the same
wrong codec.

**Genuine BOM-less UTF-32 could be valid under both byte orders.** Bytes whose scalars are
all multiples of 0x100 read equally well either way, and detection preferred little-endian
without saying so.

The rule that closes both: **without a byte-order mark, EC does not accept UTF-32 on
detection alone.** It reports `Refused` with reason code `UnprovableBomlessUtf32` and
leaves the bytes alone, in every mode.

**Why the existing UTF-16 guard could not be widened to cover it.** That guard refuses
BOM-less UTF-16 when the *opposite* byte order also decodes the file. The UTF-16 file
above is **invalid** as UTF-32BE, so the same test returns false and would have passed it
straight through. What the bytes fail to establish is the codec, not merely its byte
order — so the refusal rests on the absence of a BOM, not on ambiguity.

## The cost

**Ordinary BOM-less UTF-32 is refused too**, even when its content is unremarkable.
Nothing in the bytes separates it from the UTF-16 file above, so there is no test that
admits one and rejects the other. Two rows moved out of the conversion matrix's
valid-conversion set to say so.

Two ways through, both unchanged and both tested: add a byte-order mark, or name the
source with `-From utf-32` or `-From utf-32BE`. A BOM settles the codec, so UTF-32 with
one still converts automatically.

## What did not change

**No scalar classification.** The misdetection is possible because the text heuristic
accepts unassigned scalars, and the tempting fix is to reject unassigned or private-use
ones. That would be wrong: private-use characters are what icon fonts put in ordinary text
files, so it would break real sources. A test converts U+E000, U+F8FF, U+E0B0, U+F00C and
U+F0000 and checks all five survive.

`TextValidation.cs` and `UnicodeDetector.cs` are untouched. Both are held byte-identical
across three repositories by the parity workflow, and a fix that needed to change them
would have been a synchronised change in all three.

## Compatibility

**Conversion semantics move from 6 to 7. Plans written by v3.12.1 or earlier are
refused.** Re-run `-Plan` and review the result. This is the point of the version: such a
plan can list files this build will not convert, and applying it would extend approval
given for one behaviour to another.

The plan schema stays at **5** and the journal schema at **4**.

| Situation | Before | After |
|---|---|---|
| BOM-less UTF-32, any mode | `utf-32`, converted, 0 | `Refused`, `UnprovableBomlessUtf32`, 5 |
| BOM-less UTF-16 that decodes as UTF-32 | converted to different text, 0 | `Refused`, 5 |
| A plan from v3.12.1 or earlier | applied | refused, 1 |

`UnprovableBomlessUtf32` is a **new reason code**, separate from
`AmbiguousBomlessUtf16` rather than reusing it: the doubt and the remedy differ, and a
script filtering on the UTF-16 code would otherwise start matching a condition it was
never written for.

Everything else is unchanged — UTF-16 behaviour, legacy refusals, exit codes for every
other outcome, and every other report column and journal field.

## Verification

- 751 tests pass, none skipped (was 746); release build with no warnings
- Verified end to end against the built executable in Detect, Validate and Convert: the
  same reason code in all three, refused sources byte-identical before and after, and all
  five private-use scalars preserved through a conversion
- Mutation-checked: removing the UTF-32 arm fails 3 of the 6 core tests, and the file
  restores byte-identical by hash
- The defect ledger records BL-01 and BL-18 as fixed with this evidence;
  `docs/Test-DefectBacklog.ps1` passes at 46 fixed and 11 open
- The four-corpus audit was run against this commit from a clean tree. Its results, and
  the one thing it cannot establish, are below.

## The four-corpus audit

Run from a clean tree over UnicodeTestSuite v3.0, chardet `test-data`,
charset-normalizer `char-dataset`, and the UTF.unknown 2.6 tests — 5,077 files. The exact
commit and assembly hash are recorded in `docs/SAFETY-AUDIT.md`, which is written after
this release is tagged: committing it changes the assembly through the PDB checksum, so a
hash quoted here would describe a build this file is part of.

Four metrics, reported separately because one number would hide which question failed:

| | v3.11.0 | v3.13.0 |
|---|---|---|
| Detection accuracy | 4640/4646 (99.87%) | 4639/4645 (99.87%) |
| Strict decoding | 4694/4694 (100.00%) | 4693/4693 (100.00%) |
| Codec conformance | 4591/4694 (97.81%) | 4590/4693 (97.81%) |
| Text preservation | 4520/4623 (97.77%) | 4519/4622 (97.77%) |

Per corpus, and unchanged: **zero implementation defects, zero strict-decode throws, zero
backup-integrity mismatches.** The comparison is joined per file rather than by totals.
One file differs, and it is not a behaviour change: a UTF-8 fixture present when v3.11.0
was audited is **absent from the local corpus copy now**, so it appears as `(absent)`
rather than as a changed outcome. Every other file reached the same outcome as before.

The remaining divergences are the long-standing ones, unrelated to this release: 145
UTF-7 files EC has no codec for, and 103 legacy mapping differences dominated by
`U+301C → U+FF5E` and `U+2212 → U+FF0D` in shift_jis and euc_jp.

**Source corpora were not modified.** Each is copied into a working directory and only the
copy is converted. Verified afterwards by re-hashing all 5,077 source files against the
inventory captured before the run: zero modified, zero missing.

### What this audit cannot establish

**It does not exercise the change.** The harness runs with a forced reference, supplying
`-From` for every file, and this release only refuses BOM-less UTF-32 when detection is
*automatic*. `UnprovableBomlessUtf32` therefore appears **zero times** across all 5,077
files. The audit is evidence that the change broke nothing; it is not evidence that the
change works. That comes from the unit and end-to-end tests, which drive the automatic
path directly.

What the audit does exercise is the way out: 847 files carrying the BOM-less-doubt
advisory were converted with an explicit source, and **all 847 preserved their text
exactly**. The escape hatch this release tells users to reach for is measured at scale,
even though the refusal that sends them there is not.

**Detection is provably unchanged.** Both detection testers produced reports
byte-identical to the committed ones — 1,358 UnicodeTestSuite files and 3,137 chardet
files — which is the expected result for a change that touches no detector file.

Those testers print an overall accuracy scored through an "also valid as" equivalence
relation. That relation is not used here to establish ground truth; the figures are used
only as a before-and-after identity check, which is what they can support.

### One defect found during review, worth recording

The first version of this change reported the correct reason code from conversion and the
**wrong one** from `-DetectOnly` and `-Validate`: both branches held the UTF-16 code as a
literal while the condition reaching them had widened. All 746 unit tests passed while the
three modes disagreed about the same file. A CLI run found it.

That is the mode-asymmetry class this project already knows about, reproduced by the fix
for it. The three now read one mapping, and a test covers the read-only modes.
