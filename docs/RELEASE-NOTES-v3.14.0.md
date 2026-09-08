# EncodingChecker v3.14.0

Four backlog findings closed, one dropped after measurement, and a change to the shape of
the plan and journal files. That last one is why this is a minor release: the artifacts
changed, so their schema versions say so, and a plan written by v3.13.0 is refused.

No detection or conversion policy changes. What EC converts, refuses and reports for any
input is exactly what v3.13.0 did.

## A plan says what its version means, instead of five constants that could not disagree

Every plan and journal carried five booleans — `StrictDecoding`, `StrictEncoding`,
`OutputVerification`, `AtomicInstall` and `LegacyRequiresExplicitSource` — hardcoded
`true`, written on every run, and read back by nothing. The same build wrote the flags and
the semantics version they sat beside, so they could never disagree with it. What they
could do is invite a reader to take a serialized `true` as evidence that a particular check
ran on that file.

They are gone. In their place is `SemanticsDescription`: the sentence
`ConversionSemantics.Describes` already held, written beside the version number, so whoever
opens a plan reads what the version means rather than five constants to interpret.

`SemanticsVersion` stays the only value `Load` enforces, and a test proves the description
decides nothing — altering it inside a plan file changes nothing about whether that plan
loads.

**This is artifact clarity, not a conversion-safety fix.** EC always executed the strict
behaviour those flags described. Only the file implied that the flags were the reason.

### What it costs

The artifacts changed shape, so their versions say so: plan schema 5 to 6, journal schema 4
to 5. **A plan written by v3.13.0 or earlier is refused** and has to be re-planned and
reviewed again — even though this build would make exactly the decisions that plan records,
because conversion semantics are unchanged at 7.

Landing a day earlier this would have cost nothing: semantics moved 6 to 7 in v3.13.0, so
every plan older than that release was already refused. It did not land earlier. This is a
real cost paid for a clearer artifact, and a reader deciding whether to upgrade should
weigh it that way.

## A file already cleared is not examined again

`ScanEngine` cached only a *positive* BOM-less ambiguity result:
`entry.HasAmbiguousBomlessUtf16 || IsAmbiguousBomlessUtf16(...)` short-circuits when the
stored value is true, so a stored false ran the full opposite-order decode again on every
pass.

The classification is now stored as a nullable value — null means not yet classified,
`None` means classified with nothing found. Both are kept, and a file is examined once.

**This is not a performance fix.** Measurement found no meaningful cost in the old form,
because a provable file fails the opposite-order decode inside its first buffer. The defect
was a stored state that could not distinguish "no" from "not asked". Nothing observable
changes, which is why it carries no test of its own.

## Plan application names the invariant it rests on

Applying a plan used `plan.ResolvePath(f)!`. The dereference was safe — `FindStaleFiles`
rejects a plan whose paths resolve outside its directory, twenty-nine lines earlier. It was
safe because of the order of two calls, and nothing said so.

It is now an explicit throw naming that invariant: reaching it means the check did not run.
Under the present flow nothing changes, so there is nothing new to assert and no test
accompanies it. EC-06 is what happened the last time an invariant of this shape was left
implicit.

## Release evidence promises only what the build being driven can show

`RELEASE-CHECKLIST.md` and `GUI-SMOKE-TEST.md` both stated that the smoke report carries
"the executable and managed-assembly hashes". Checked against real release evidence:
`gui-smoke-report.json` carried no managed-assembly hash and the Markdown rendered an empty
pair of backticks, while both printed the `EncodingChecker.dll` path as though a value
followed.

The release workflow drives the *published* executable, and a single-file publish leaves no
loose DLL beside it. That has held since single-file publishing began, so **the v3.12.0 and
v3.12.1 evidence carries the same empty field**, and the claim in their records is wrong the
same way.

The report now records the assembly only when there is one to hash, and says there is none
when there is not. Both documents describe both cases, because both happen: driven against
an ordinary Release build the suite does find a loose assembly and hashes it; driven against
the published single-file executable, as the release workflow does, it does not.

Hashing the publish intermediate was considered and rejected: it exists only during the
build and no user ever receives it, so recording it would document a byproduct rather than
the release. The executable is the artifact — v3.13.0 showed the stronger form by
reproducing its hash byte-for-byte from the tagged commit.

Nothing about conversion was involved either way. This is evidence hygiene.

## A fix that was written, measured, and dropped

CX-06 records that the entropy guard returns before the byte-order mark is examined, so a
high-entropy file can be reported as unknown even when it declares its encoding. It was
scored Occasional. That score had never been measured.

It has been now. Across all four corpora, 3,620 files are large enough to be gated at 512
bytes and 47 trip the 7.4-bit threshold — every one of them binary: 35 fixtures under
`13_Binary/`, plus images and a spreadsheet. The highest-entropy *text* file among 3,568
candidates is dense Chinese XML in gb2312 at **6.8133**, a margin of 0.59 below the
threshold. Base64 caps at 6.0 by construction. Reaching 7.4 needs a near-uniform byte
distribution, which prose does not produce in any encoding.

**The fix worked and was dropped anyway.** Making the guard yield to a byte-order mark is a
few lines, and it costs something measurable: `CheckBom` returns a codec from the marker
bytes alone, so BOM-prefixed binary began detecting as `utf-16` instead of `(Unknown)`, and
a scan containing one moved from exit 0 to exit 3. Strict validation still caught it and no
bytes changed. But that is a measured behaviour change bought against a benefit no file in
3,568 demonstrates.

The row is re-scored Occasional to Theoretical and stays open. Reopen it on evidence of real
text at or above the threshold — not on the mechanism, which was never in doubt.

## Compatibility

**The plan schema moves from 5 to 6 and the journal schema from 4 to 5. A plan written by
v3.13.0 or earlier is refused.** Re-run `-Plan` and review the result.

Conversion semantics stay at **7**.

| Situation | Before | After |
|---|---|---|
| A plan from v3.13.0 or earlier | applied | refused, 1 |
| `Semantics` in a plan or journal | five `true` flags | `SemanticsDescription`, one sentence |

Every exit code, reason code, report column and other journal field is unchanged, and
detection and conversion behave exactly as they did in v3.13.0.

## Verification

- 755 tests pass, none skipped (was 751); release build with no warnings
- The four new tests were mutation-checked one at a time. Each mutation built cleanly and
  failed exactly the test aimed at it: the plan stops carrying the description, the removed
  flags come back, the journal stops carrying the description, and `Load` starts consulting
  the description. Both source files restored byte-identical, confirmed by SHA-256
- The GUI smoke suite passes all ten phases against **both** builds — the ordinary Release
  build, where it finds a loose managed assembly and hashes it, and the published
  single-file executable, where it reports that there is none. That is the evidence fix in
  both of its branches, checked rather than argued
- `docs/Test-DefectBacklog.ps1` passes at **62 findings — 50 fixed, 8 open**
- **EC-18 and EC-23 carry no tests, deliberately.** Neither changes observable behaviour, so
  a test would pin the shape of the code rather than anything EC promises. The backlog
  records that reasoning rather than leaving them to look untested by oversight
- **No four-corpus audit was run.** This release changes no detection or conversion policy,
  so the checklist does not require one. The CX-06 measurement above did read all four
  corpora, but as measurement, not as an audit
