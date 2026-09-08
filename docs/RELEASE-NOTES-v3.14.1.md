# EncodingChecker v3.14.1

One product fix. Everything else in this release is invisible to anyone running
`EncodingChecker.exe`, and the notes say so rather than padding the list.

## A constructed include pattern can no longer hang a scan

`-Include` and `-Exclude` masks are translated to regular expressions. That translation
is unchanged — `*` still becomes `.*`, `?` still becomes `.`, and a mask with no
separator still matches at any directory depth. What changed is how .NET evaluates the
result.

The old engine could be made to retry an exponential number of alternatives. A mask
carrying twelve separated wildcards, matched against a nonmatching forty-character run of
the separating character, did not finish within three seconds and had to be killed.
Realistic names answered in 0–5 ms throughout. The new engine returns the correct answer
in 0.427 ms, and runs in time proportional to the input, so no mask can stall a scan.

**Include and exclude results are identical.** An independent differential comparison ran
500 generated masks against 200 generated paths — 100,000 pairs, including directory
separators and the regex-special characters EC escapes — and found no case where the two
engines disagreed. A regression test asserts the engine before exercising the hostile
input, so restoring the old one fails in 9 ms rather than hanging the test run.

**It is not free.** Measured over 3,937 files, median of five warm runs: 128 ms before,
148 ms after — about 16%, or roughly 5 µs per file, and the same figure for a plain
`*.txt` mask as for `*a*b*.txt`. That is the price of a bounded worst case, and it is
recorded here so nobody has to measure it again.

Both inputs have to be deliberately hostile for the old behaviour to appear, and the mask
comes from the operator rather than from an untrusted file, which is why the defect was
scored Theoretical. An unbounded runtime was still worth removing for the price of one
option.

## What else is in this release, and why you will not notice it

- **The release gate is deterministic.** The GUI smoke suite could select an encoding
  from the wrong drop-down, because it searched the whole application for any visible item
  with a matching name and two combos hold the same names. It failed once during the
  v3.14.0 release and passed on a re-run of the same commit. It now sets the value
  directly. This is test machinery; `EncodingChecker.exe` is unaffected.
- **That suite now runs on every pull request**, not only at tag time, so a GUI regression
  is found by the change that caused it.
- **Every `.cs` file changed by one byte.** Source files now agree on encoding — UTF-8
  without a byte-order mark, CRLF — with an `.editorconfig` recording the choice. No
  compiled behaviour changes; the assembly hash does, because the sources did.
- **The defect ledger and release documentation were rewritten** to be read by a
  maintainer rather than decoded, and four claims in them turned out to be untrue and were
  corrected.

## Compatibility

Nothing changes. Conversion semantics stay at **7**, the plan schema at **6**, the journal
schema at **5**. Plans and journals written by v3.14.0 apply unchanged. Every exit code,
reason code, report column and journal field is what it was, and the set of files any given
`-Include` or `-Exclude` mask selects is unchanged.

## Verification

- 756 tests pass, none skipped (was 755); release build with no warnings
- The GUI smoke suite passes all ten phases, against both an ordinary Release build and the
  published single-file executable
- The regex fix was mutation-checked: restoring `RegexOptions.Compiled` fails the new test
  in 9 ms, on the engine assertion rather than by hanging, and the file restored
  byte-identical by SHA-256
- Phase B now also asserts that the ASCII file receives a verified recovery record. EC
  converts ASCII to UTF-8 even though the bytes are identical, and until now nothing
  checked that the restore point was written for that case
- `docs/Test-DefectBacklog.ps1` passes at **65 findings — 52 fixed, 9 open**
- **No four-corpus audit was run.** This release changes no detection or conversion policy,
  so the checklist does not require one
