---
name: encodingchecker-forensic-review
description: >
  Perform a full forensic review of the EncodingChecker C#/.NET repository.
  Use for whole-repository audits, release-readiness reviews, hidden-regression
  investigations, encoding/conversion correctness, Unicode/BOM handling,
  false passes or false failures, diagnostic accuracy, test gaps, concurrency,
  data-preservation risks, or independent review from scratch. Prioritize
  correctness and reproducible defects over style or refactoring.
---

# EncodingChecker Forensic Review

Perform an evidence-driven review of EncodingChecker.

The goal is to determine whether EC can incorrectly detect, validate, convert,
verify, or report text files — especially when failures could cause silent
corruption, false success, false failure, or misleading diagnostics.

Do not optimize for number of findings.

Prefer a few proven defects over many speculative observations.

## Review priorities

Review in this order:

1. Silent text corruption or data loss
2. False-success paths
3. Conversion verification correctness
4. Encoding validation and decoder/encoder fallback
5. BOM and BOM-less Unicode handling
6. Detection correctness and uncertainty
7. Legacy encoding behavior
8. Exception propagation and diagnostics
9. Regression-test quality
10. Concurrency and cancellation
11. GUI/CLI behavioral consistency
12. C# design, maintainability, and performance

Style-only findings are out of scope unless they create a credible correctness
or maintenance risk.

---

# 1. Understand the pipeline first

Before reporting defects, trace the actual EC data flow:

`bytes → detection → validation → selected encoding → decode → Unicode → encode → write → verify → result`

Identify:

- detection entry points;
- validators;
- conversion code;
- verification code;
- BOM handling;
- fallback behavior;
- backup/file replacement logic;
- GUI and CLI paths;
- concurrency model;
- relevant tests.

Do not review isolated methods without understanding how their result is used.

---

# 2. Core correctness invariants

## No silent corruption

EC must not silently:

- replace malformed bytes;
- drop bytes or characters;
- introduce replacement characters;
- accept invalid Unicode through permissive fallback;
- change Unicode content during conversion unless explicitly intended.

Pay special attention to default .NET decoder/encoder fallback behavior.

When strict validity is claimed, verify that strict fallbacks are actually used.

## Detection is not validation

A detector returning an encoding does not prove that the complete byte stream is
valid under that encoding.

Check whether EC incorrectly treats:

- detector confidence as validity;
- "most likely" as certain;
- syntactic decodability as proof of original encoding.

## Validation is not identification

A valid UTF-8 byte stream is not necessarily intended UTF-8.

ASCII is compatible with many encodings.

Report ambiguity honestly where certainty is impossible.

## Verify Unicode semantics, not encoding labels

Successful conversion should normally mean that intended Unicode content is
preserved.

Do not accept verification based only on:

- output existence;
- target BOM;
- target encoding re-detection;
- byte counts;
- hashes;
- permissive decoding.

Where applicable, reason about:

`strict_decode(source, source_encoding)`

versus:

`strict_decode(output, target_encoding)`

and compare intended Unicode content separately from BOM policy.

---

# 3. Encoding-specific review

Inspect at minimum:

## UTF-8

- malformed continuation bytes;
- truncated sequences;
- overlong encodings;
- surrogate code points;
- values above U+10FFFF;
- BOM;
- duplicate BOM;
- embedded U+FEFF;
- ASCII-only input.

## UTF-16

- LE and BE;
- BOM and BOM-less;
- odd byte lengths;
- surrogate pairs;
- lone surrogates;
- endian mistakes;
- zero-byte heuristics;
- short inputs.

## UTF-32

- LE and BE;
- BOM and BOM-less;
- invalid scalar values;
- surrogate-range values;
- truncated units;
- length not divisible by four;
- false-positive zero patterns.

## BOM handling

Check:

- no BOM;
- correct BOM;
- duplicate/triple BOM;
- BOM-only files;
- BOM inconsistent with selected encoding;
- embedded BOM/U+FEFF.

Distinguish encoding-signature bytes from actual decoded U+FEFF content.

Do not automatically treat duplicate BOMs as harmless.

## BOM-less detection

Challenge heuristics with:

- short files;
- ASCII;
- UTF-16/32 without BOM;
- non-Latin text;
- NUL-heavy binary;
- random/binary data;
- legacy encodings that resemble Unicode.

Ask whether detection is independently validated before conversion.

## Legacy encodings

Check:

- undefined mappings;
- best-fit/replacement fallback;
- multibyte truncation;
- codec/provider differences;
- platform-dependent mappings;
- round-trip asymmetry.

Do not assume two codec implementations using the same encoding name necessarily
produce identical Unicode mappings.

---

# 4. False-pass review

Actively search for paths where EC reports success when it should fail.

Examples:

- replacement fallback hides malformed input;
- verification itself is permissive;
- worker exceptions are lost;
- failed verification does not alter final status;
- cancellation is reported as completion;
- partial output is treated as successful;
- unsupported encoding silently falls back;
- exception is logged but execution continues with a success-like result.

Trace suspicious paths all the way to the final GUI/CLI result.

---

# 5. False-failure review

Also identify valid inputs EC rejects incorrectly.

Examples:

- valid Unicode rejected by incorrect surrogate handling;
- valid BOM treated as content;
- correct legacy mapping classified as corruption;
- valid empty/BOM-only files mishandled;
- reference-codec disagreement mistaken for EC failure.

False failures are correctness defects too.

---

# 6. Conversion and data-preservation review

Trace the complete write path.

Check:

- backup timing;
- temporary files;
- overwrite behavior;
- source/destination identity;
- partial writes;
- cleanup after failure;
- file locks and permissions;
- cancellation;
- verification before destructive replacement.

A failed conversion must not leave the original file less recoverable.

Data preservation has higher priority than convenience.

---

# 7. Exception and diagnostic review

Inspect important catch paths.

For each, determine:

- what can throw;
- whether the exception is intentionally handled;
- what result is returned;
- whether processing incorrectly continues;
- whether the final user-visible diagnosis is accurate.

Differentiate where possible:

- detection uncertainty;
- invalid source data;
- unsupported encoding;
- decode failure;
- encode failure;
- write failure;
- verification failure;
- BOM-policy failure;
- backup failure;
- cancellation;
- internal defect.

Look especially for exceptions that become:

- `true`;
- `false` with ambiguous meaning;
- empty/default values;
- skipped files;
- warnings;
- log-only events.

---

# 8. Concurrency review

Where EC uses parallelism, inspect:

- shared mutable state;
- concurrent collections;
- counters;
- exception aggregation;
- cancellation;
- progress reporting;
- ordering assumptions;
- shared encoder/detector objects;
- UI thread interaction.

Verify that single-threaded and parallel execution cannot produce different
correctness outcomes.

Worker failures must not disappear from the final status.

---

# 9. Test-suite review

Evaluate tests by behavioral risk, not coverage percentage.

Look for:

- tests reproducing the implementation instead of independently verifying it;
- permissive decoding in expected-value generation;
- duplicated tests with little additional coverage;
- fixture-only regression tests that miss the broader bug class;
- assertions on labels/status instead of actual Unicode content;
- tests that cannot fail for the intended reason.

High-priority regression coverage should include:

- malformed UTF-8/16/32;
- BOM and duplicate BOM;
- BOM-less Unicode;
- supplementary characters;
- legacy-codepage edge cases;
- conversion semantic equality;
- backup/write failure;
- cancellation;
- concurrency;
- false-success paths.

For every confirmed bug, require a regression test that fails before the fix.

---

# 10. Use specialist skills when available

This skill defines EC-specific review requirements.

When available, use supporting skills for focused passes:

- `code-review` — broad implementation review;
- `diagnosing-bugs` — reproduce and prove suspicious behavior;
- `test-anti-patterns` — independent test-suite audit;
- `modern-csharp-coding-standards` — C# correctness and API issues;
- `type-design-performance` — meaningful allocation/performance issues;
- `csharp-docs` — documentation claims versus implementation;
- `computer-use` — GUI smoke and end-to-end validation.

Do not treat another skill's output as automatically correct.

Validate findings against EC behavior.

---

# 11. Independent-review mode

When asked for an independent or from-scratch review:

- inspect the current repository directly;
- do not adopt conclusions from prior reviews;
- treat historical fixes as hypotheses until verified;
- do not assume passing tests prove correctness;
- avoid relying on prior model memory when evaluating disputed behavior.

Historical bugs may be checked afterward as regression targets.

---

# 12. Evidence standard

Every substantive finding must include:

**Location**  
File and symbol.

**Severity**  
Critical / High / Medium / Low.

**Confidence**  
Confirmed / High / Unconfirmed.

**Problem**  
What is wrong.

**Trigger**  
Concrete condition that exposes it.

**Impact**  
What EC or the user experiences.

**Evidence**  
Code path, reproducible test, runtime behavior, or authoritative framework behavior.

**Fix**  
Smallest root-cause correction.

**Regression test**  
Specific test needed.

Do not label speculation as a confirmed bug.

When uncertain about .NET encoding behavior, run a minimal experiment rather
than guessing.

---

# Severity

**Critical**
- credible irreversible data loss;
- widespread silent corruption.

**High**
- false success after corrupted conversion;
- semantic source/output mismatch;
- common malformed input silently accepted;
- major backup/data-preservation failure;
- lost worker failures.

**Medium**
- meaningful false detection or false rejection;
- misleading diagnostics;
- significant missing regression coverage;
- GUI/CLI inconsistency affecting behavior.

**Low**
- narrow diagnostic issue;
- credible maintainability risk;
- non-critical performance issue;
- misleading documentation.

Do not inflate severity.

---

# Final report

Produce:

## Executive verdict

Include:

- overall assessment;
- release readiness;
- Critical/High/Medium/Low counts;
- largest remaining risk.

Classify release readiness as:

- **READY**
- **READY WITH MINOR FOLLOW-UP**
- **NOT READY**
- **INSUFFICIENT EVIDENCE**

## Confirmed findings

Order by severity.

For each:

### EC-REV-XXX — Title

**Severity:**  
**Confidence:**  
**Category:**  
**Location:**

**Problem**

**Trigger**

**Impact**

**Evidence**

**Recommended fix**

**Regression test**

## Unconfirmed concerns

Keep plausible-but-unproven concerns separate.

State what test or evidence would resolve each one.

## Test gaps

List only meaningful missing behavioral tests.

## Before next release

Separate into:

- **Must fix**
- **Should fix**
- **Can defer**

## Residual risk

State what the review could not prove.

---

# Completion rule

Do not claim a full EC review is complete unless you have addressed:

- detection;
- validation;
- UTF-8/16/32;
- BOM handling;
- BOM-less detection;
- legacy encodings;
- conversion;
- semantic verification;
- data preservation;
- exception handling;
- diagnostics;
- concurrency;
- tests;
- GUI/CLI consistency where applicable;
- release readiness.

If context or access prevents completion, clearly identify the unreviewed areas.

Correctness and diagnostic honesty take priority over style, modernization, and
passing existing tests.