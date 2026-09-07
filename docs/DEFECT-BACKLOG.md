# Defect backlog

Status of the thirty-five findings from the two independent reviews that
preceded v3.11.0, plus what has been found since.

**Of the original thirty-five: 26 fixed, 8 open, 1 could not be reproduced.**
Six further findings have been raised since v3.11.1, two of them already fixed.
Twelve more since v3.11.2, all twelve fixed.
**Twelve open in total.**

## Why this file exists

The original review lived in a generated HTML report and in a chat transcript.
Neither survived into the repository, so weeks later the only way to answer
"what is still open?" was to trust a summary — and the summary was wrong.

EC-06 is the demonstration. It was found before v3.11.0, recorded as Medium and
Confirmed, reported as closed, and shipped broken in **both** v3.11.0 and
v3.11.1. It was rediscovered from scratch during an unrelated review, filed as a
new finding, and only then recognised as a known one.

A finding that is written down but not tracked is a finding that gets found
twice and fixed late. Hence this file.

## How each status was reached

`fixed` means the code that caused it is demonstrably gone — a named
replacement, a test that pins the behaviour, or a check re-run against the
current build. `open` means the cited code is still present and was read again
on 2026-09-04. `not reproduced` means an attempt to trigger it failed, which is
weaker than either.

Statuses were re-derived from the source, not carried over from the earlier
report.

## The thirty-five

| ID | Finding | Status | Evidence |
|---|---|---|---|
| EC-01 | Applying a plan converts a file the plan refused | fixed | `PlannedFile.HasReliableUnicodeDetection` carries the flag the plan boundary was dropping. |
| EC-02 | `-Validate` marks an unprovable file valid; `-Target` refuses it | fixed | v3.11.0 reports unprovable BOM-less UTF-16 as `Invalid`. |
| EC-03 | The GUI never shows the advisory v3.10.1 added | fixed | GUI smoke phase H asserts on the rendered advisory text. |
| EC-04 | `-Plan` exits 0 when files failed the scan | fixed | The plan branch returns 3 before considering 2. |
| EC-05 | A plan holding an unreadable file can never be applied | fixed | A hash is required only for `Convert` entries. |
| EC-06 | A drive-root base path makes every plan unusable | fixed | Fixed 2026-09-04. **Shipped broken in v3.11.0 and v3.11.1.** |
| EC-07 | The refusal advises the very encoding it cannot justify | fixed | `DescribeRefusal` offers both byte orders. |
| EC-08 | An include pattern can hang the scan indefinitely | **open** | Reproduced 2026-09-06, but only against inputs built for it. `CompilePatterns` still emits `RegexOptions.Compiled` with `Regex.InfiniteMatchTimeout`. The 2026-09-04 attempt used a *matching* filename, which stops at the first success and cannot show it. Scored below. |
| EC-09 | `.bak` files are excluded, uncounted, and unreported | fixed | `TraversalCounters.FilesExcludedAsEcArtifact`. |
| EC-10 | A scan failure is journaled as a refusal | fixed | A failed snapshot is recorded as `Error`, not `Refused`. |
| EC-11 | `ApplyPlan` leaks its Ctrl+C handler onto a disposed token source | fixed | Both handler sites unsubscribe in a `finally`. |
| EC-12 | The GUI status line counts skipped files as unchanged | fixed | Pinned by a test naming EC-12. |
| EC-13 | The plan's explicit-source field can name one encoding for a run that used several | fixed | `DescribeSourceChoice` reports per-file choices. |
| EC-14 | `OutputTextSha256` is a copy of `SourceTextSha256` | fixed | The record takes the digest verification computed, and throws if absent. |
| EC-15 | The five conversion-semantics booleans are written everywhere and read nowhere | **open** | Only `SemanticsVersion` is enforced on load. |
| EC-16 | Settings.xml is written with truncate-in-place | **open** | `MainForm.Settings.cs:100` still opens `FileMode.Create` and serialises into it. |
| EC-17 | The text-validation comment contradicts its code | **open** | Control characters are penalised, not ignored. Behaviour is right, comment is wrong — and the file must stay byte-identical across three repos, so the fix is a synchronised change. |
| EC-18 | Ambiguity is recomputed on every pass over a BOM-less UTF-16 file | **open** | The or-expression short-circuits only when the flag is already true. |
| EC-19 | The double-BOM guard's reach depends on which object supplied the codec | *not reproduced* | The detector's BOM-less instance never reaches the guard; both paths refuse. Tested. |
| EC-20 | `DetectFromFile` opens with looser sharing than every other read path | **open** | Still permits concurrent writes and deletes. |
| EC-21 | Three save-dialog instances are never disposed | fixed | All three use `using var`. |
| EC-22 | Plan serialisation and deserialisation use different options objects | fixed | Reader and writer now share one options object, in the plan store and the metadata store. |
| EC-23 | `ApplyPlan` dereferences `ResolvePath` with a null-forgiving operator | **open** | `Program.CliExecution.cs:90`. EC-06 was what happens when that invariant breaks. |
| CX-01 | An empty option value is silently ignored | fixed | Blank values are rejected with exit 1. |
| CX-02 | A failed second conversion destroys the first backup | fixed | `RemoveBeforeBackupReplacement` runs before the backup is replaced. |
| CX-03 | `-Apply` follows a plan root replaced by a junction | fixed | `HasReparsePointInPath` checks the whole path. |
| CX-05 | The journal cannot represent a post-install failure | fixed | `ConvertedWithWarning` and `InstallationUnknown` added. |
| CX-06 | The entropy gate outranks a valid BOM | **open** | `TextEncoding.cs:175` returns null before `UnicodeDetector.DetectFromBuffer` at line 182 reads the BOM. |
| CX-07 | Older plans, journals and reports are ordinary scan candidates | **not a defect; the record was wrong** | The fix recorded here was never made, and should not be: `docs/CLI.md` states that EC does *not* exclude plans, journals or reports left by earlier runs, and that is deliberate - a user may well want to convert them. What is excluded is `.bak`, `.ecmeta.json`, temporary conversion files, and the output paths of the running command. An `old-plan.json` in the scan root is detected as ASCII and scanned, which matches the documentation and contradicted this row. Found by re-deriving the row from the source rather than from its own note. |
| CX-08 | Documentation and validation disagree about `-DetectOnly` | fixed | Conflicting option combinations are rejected. |
| CX-09 | Cancelling a partly completed GUI run produces no journal | fixed | GUI smoke phase I covers it. |
| CX-10 | Plan summaries say "detection bypassed" when detection still ran | fixed | Now "chosen by you; detection still ran and is recorded". |
| CX-11 | GUI startup can fail if the settings directory cannot be created | fixed | `GetSettingsFileName()` moved inside the `try`. |
| CX-12 | A saved window position is not validated against current monitors | fixed | `WindowPosition.IsReachable` tests the title bar against attached monitors. |
| CX-13 | Detector parity is not a pull-request check or a release gate | fixed | Parity runs on pull requests, and the release workflow declares `needs: parity`. |

## Found after v3.11.1

| Finding | Status | Note |
|---|---|---|
| Ambiguous BOM-less UTF-32 converts silently | **open** | The ambiguity guard covers only code pages 1200 and 1201, so the UTF-32 detector's prefer-little-endian wins with no refusal. Demonstrated end to end; reaching it needs every scalar to be a multiple of 0x100, so real text is unlikely to trigger it. |
| CSV report does not neutralise leading formula characters | *withdrawn* | It does not reproduce. `DirectoryTraversal` resolves every file through `Path.GetFullPath`, so the `File` column always begins with a drive letter or a UNC prefix and can never begin with `=`, `+`, `-` or `@`. A source named `=1+1.txt` produces a cell reading `C:\...\=1+1.txt`. Every other column is a controlled value. Reopen only with a demonstrated reachable field. |
| Conversion parallelism was capped at 4 | fixed | Raised to 8 on 2026-09-04; measured 1.5–1.7x faster. |
| A ticked file can be dropped from a source choice in silence | fixed | Each row in the review's refused list carries its resolved path. `TickedFiles()` filters out rows whose path is null and says nothing, so a file the user ticked is left refused with no message. This was live until EC-06 was fixed: with a drive-root base directory every row resolved to null, so choosing an encoding reported "Conversion cancelled. No files were modified." The trigger is gone; the silent drop is not. |
| Force-closing during a run can throw on the way out | **open** | The second close request abandons a run deliberately, which is correct. But the worker may then marshal its next confirmation to a form that no longer exists, and the completion handler runs against disposed controls. An error dialog at exit rather than lost work — finished files are installed and the one in flight is untouched. Reasoned from the code, not reproduced: it needs precise timing. |

## Found after v3.11.2

An independent review of 74d5b3d, run from the source rather than from this file.
Everything it found and fixed is below, one commit each, carrying that commit's
measurement and mutation result. Ordered by what a reader needs first: what EC
did to files, then what it reported, then what it documented.

| Finding | Status | Note |
|---|---|---|
| "Already in the target encoding" was decided by label, not by codec | fixed | `Decide` compared the detected charset's `WebName` against whatever the caller typed, so every accepted alias for one code page failed the test. `-Target unicode`, `ucs-2` or `utf-16le` on a tree already in UTF-16LE decoded, re-encoded, verified and reinstalled every file to produce **identical bytes**, resetting every modification time and, with `-Backup`, leaving a `.bak` and an `.ecmeta.json` beside each. Under `-FailOnChanges` the same tree exits 0 for `-Target utf-16` and 2 for `-Target unicode`; on BOM-less UTF-16 the alias reaches the ambiguity guard and exits 5, so the spelling alone moved a clean run to a refusal. Now compares resolved code pages; a zero code page proves nothing and never matches. ASCII to UTF-8 was deliberately left a conversion, on reasoning the 64 KiB finding below then disproved — folding them together is now safe and is not yet done. |
| "Already in the target encoding" was a whole-file claim made from a 64 KiB sample | fixed | Detection reads at most 64 KiB, and when the source codec matched the target nothing read further. A file clean for 64 KiB and invalid afterwards was reported `Unchanged`, and whether EC noticed depended only on which target was named: the same corrupt file was `Error` under `-Target utf-16` and `Unchanged` under `-Target utf-8`. `-Validate` always read the whole file; Convert never reached that check once it had decided it had nothing to do. Costs almost nothing, and not for the expected reason — Convert already reads every byte, because `CaptureSourceSnapshot` hashes the whole stream before anything is decided. Measured at 0.04–0.10 ms per MiB. |
| A preview promised conversions that would fail, and a plan recorded them as approved | fixed | `ApplyConversion` returned at the `whatIf` branch before the converter ran, so nothing decoded the file. `-Plan` sets `WhatIf`, so a plan recorded `Action=Convert` with no reason for a source that cannot be read, exited 0, and showed the reviewer nothing; the failure surfaced at `-Apply`, after approval and part-way through the batch. `FindStaleFiles` can prove the bytes have not changed since review and cannot prove they are readable, because that needs a decode nobody performed. The entry is now marked `Refuse`, not merely given an error, because the plan records the *action*. Decode only: a target that cannot represent the text still fails at conversion time, which reading the source cannot predict, and a test named for that case pins the limit. |
| One file's failure could end the whole run | fixed | The per-item catch in `RunParallel` named four exception types. Anything else — a `SecurityException` from an ACL the enumerator did not surface, a regex timeout, a defect in EC itself — escaped `Parallel.ForEach` as an `AggregateException` and took every file the run had not reached with it. The CLI's outer catch names the same four, so it would have surfaced as a crash rather than exit 3. Now everything except cancellation and `OutOfMemoryException`, since carrying on after the latter would be pretending to process. `RunParallel` became internal so the isolation could be tested at all: no file can be made to throw the exceptions that mattered, which is exactly what made them dangerous. |
| A folder EC could not read left no trace a machine could see | fixed | An unreadable *file* becomes a row with `ScanFailed` and drives exit 3. An unreadable *directory* produced a warning on stderr and nothing else: no row, no counter, exit 0 — and nothing whatever in the window, which passes no warning callback. Measured against a deny ACE: `-Validate -FailOnChanges` over a tree with one denied folder reported "1 file(s) processed" and exited 0, and a scan whose entire base directory was unreadable printed a header-only CSV and exited 0. A run that examined none of the tree could report success. `DirectoriesUnreadable` is now counted at both catch blocks, apart from the two exclusion counters, which record folders EC *chose* not to enter. The exit code is deliberately unchanged and the documentation now says what that means for a script. |
| Folders skipped by name were counted nowhere | fixed | Twelve directory names are skipped deliberately and that is documented, but unlike attribute-excluded folders they incremented no counter. A scan of a tree whose only content sat under `build/` reported one file, zero exclusions and no warning — while `docs/CLI.md` promised that EC reports how many files each exclusion skipped. Counted separately from the attribute exclusions, whose message says "(hidden, system, or reparse point)" and would become untrue if the two were merged. What is scanned is unchanged: letting an explicit include reach into these folders was considered and declined, because both documents state they are skipped. |
| `-Validate` rejections could carry no reason at all | fixed | Four ways to return `Invalid`, two of them explained. A charset outside the allowed list and a file EC could not identify both arrived as a bare `Invalid` with an empty reason, though they are not the same situation: one means widen the list or convert the file, the other means EC could not tell what it is, which `-DetectOnly` already calls `UnknownEncoding`. `CharsetNotAllowed` is new; `UnknownEncoding` is reused deliberately, because two names for one condition depending on which mode ran would be its own defect. This was the only outcome in the product where the reader had to re-derive a reason the producer already knew. |
| A refusal's reason was re-derived instead of read from the decision | fixed | `ApplyConversion` worked the reason code out again from the four raw facts `Decide` had already reduced to a `SourceInterpretation`. The two copies were textually identical and their operands never changed between them, so they could not disagree — but nothing tied them together, and a fourth refusal reason added to the policy would have fallen through to `LegacySourceRequired` at the call site: a correct refusal carrying the wrong explanation, with nothing to fail. That shape had already needed one bolt-on `when` guard. Now `ConversionPolicy.ReasonCodeFor`, verified equivalent across all 256 reachable combinations of `Decide`'s inputs, with a test that fails if any refusal ever produces no reason. |
| A decode failure reported a position no file has | fixed | `DecoderFallbackException.Index` is relative to the decoder call, not the file, and goes negative when the bad sequence began in bytes carried over from the previous call. A UTF-8 file ending in a truncated three-byte sequence produced "offset -2 within the failing read chunk", a message naming a frame it did not describe. The offending bytes are reported instead, which mean the same thing wherever the failure happened. An absolute file offset would need the streaming loop restructured to keep each chunk's base position in scope. |
| Standard output was UTF-8 whatever the console was | fixed | After attaching to the parent console, both writers were rebuilt with `StreamWriter`'s default encoding. On the machine this was found on `Console.OutputEncoding` is `ibm437`, and the per-file CSV rendered "Grüße aus München" as "Gr├╝├ƒe aus M├╝nchen" — the tool producing in its own output the failure it exists to detect. A redirected stream stays UTF-8, matching the `-Report` file apart from its BOM; a console gets its own encoding, so characters it cannot represent become "?", which is visibly lossy rather than quietly wrong. No global console state is mutated. |
| The documented parallelism default was the old one | fixed | `DefaultMaxParallelism` was raised from `min(CPU, 4)` to `min(CPU, 8)` with the measurement recorded beside it, and both statements of it were left saying 4: the built-in help and `docs/CLI.md`, which are the two places someone tuning `-MaxParallelism` against a slow share would look. The cause was an unnamed literal, with no identity a document could be checked against; it is now `ScanEngine.MaxParallelismCap`. A test finds the one line in each document that states the default, extracts every run of digits from it, and asserts the set equals the cap, so a stale number cannot hide beside a fresh one. |
| The lifetime of `<file>.bak` was undocumented | fixed | `<file>.bak` is a fixed name holding the version the most recent run replaced, so converting the same file again replaces it and removes its sidecar. That is deliberate, and pinned by `BackupIntegrityTests.Backup_OverwritesAnyPreviousBackupFile` since the first commit of the test suite — but no document said so, and a user converting twice lost the original with nothing having warned them. Raised in review as a defect and **withdrawn**: refusing to overwrite a non-matching `.bak` breaks four existing tests and would block an ordinary "wrong target, convert again" run until the user deleted the backups by hand. See CX-02, whose fix accepted the replacement and removed the stale record instead. |

### From the same review, and not tracked here

Four findings the same review left open are recorded nowhere else in this file.
The first is the one worth reading:

- **A BOM-less UTF-16 file can be detected as UTF-32 and converted.** Silent, and
  output verification cannot catch it, because both sides of the comparison use
  the same wrong codec. It needs a file in which every other UTF-16 code unit is
  a C0 control — one character per line with LF endings, say. Measured over
  nineteen realistic file shapes: 41 of 44 detect correctly, and the three that
  do not are the same degenerate shape. Scored Critical impact, low reach.
- ASCII text with 2.3% or more NUL bytes is labelled `utf-16`. Conversion is
  refused by the ambiguity guard in every case constructed, so the wrong label
  reaches `-DetectOnly` and `-Validate` only.
- Two hard links to one file are converted twice, once per name. Both runs
  succeeded when tested, because `File.Replace` breaks the link; the `File.Move`
  fallback would not.
- Detection accepts a truncated trailing sequence, because it decodes without
  flushing, while conversion flushes and rejects it. `-DetectOnly` can therefore
  bless a file conversion refuses.

## Hashing: three optimisations measured and rejected

Conversion looked as though it hashed the same bytes several times over. Three
variants were built on throwaway branches and measured against the same
baseline, interleaved to cancel machine drift (292 MiB, 60 large files, backup
and journal enabled).

| Variant | Median | vs baseline | What it costs |
|---|---|---|---|
| Baseline | 1030 ms | — | — |
| Digest the backup while copying | 872 ms | −15.3% | The `.bak` is no longer read back, so nothing proves the restore point on disk is intact. |
| Hash source and output while streaming | −3.5% (own batch) | −3.5% | Two independent measurements become values derived from what EC intended to write. |
| XxHash128 in place of SHA-256 | 1078 ms | **+4.7%, slower** | Recorded hashes stop being verifiable with `Get-FileHash`, and lose collision resistance. |

**The reads are not redundant.** Each is an independent measurement: the source
re-read proves the file still matches what was approved, the backup re-read
proves the restore point is real, the output re-read proves what landed on disk.
Removing them is the same defect class as EC-14, which this project fixed
deliberately.

**Hashing is not the bottleneck.** In isolation XxHash128 runs at 16,447 MiB/s
against SHA-256's 2,429 — 6.8x — yet replacing it made no difference at all,
because at eight-way parallelism the hashing hides behind the I/O it accompanies.
SHA-256 is also the fastest algorithm available here: hardware acceleration puts
it ahead of SHA-1 (981 MiB/s), MD5 (754) and SHA-512 (805), so every "lighter"
cryptographic option is slower as well as weaker.

**What this means for future work.** Conversion is bound by cold reads, not by
CPU. The only variant that helped removed a read of a file that had just been
flushed to disk. Optimise reads, and treat the hashes as the verifications they
are.

### If you are reading this because you want to try again

This idea looks obviously right from the source: the same bytes are read up to
seven times per converted file, and one of the hashes is computed twice over
data already in memory. It reads like waste. It is not, and the window in which
it would pay is narrower than it appears.

**The ceiling is 15%, and it is the expensive 15%.** Every variant was measured,
not estimated. The two that preserve safety bought 3.5% and nothing at all. The
one worth having costs the only check that proves the restore point on disk is
intact — on a tool whose entire proposition is that it can undo what it did.

**These numbers are conditional, and the conditions favour the status quo.** They
were taken on a 24-core machine with a fast local disk, a warm cache, and
eight-way parallelism. Change those and the results move, but mostly in ways that
do not help: on cold or network storage the read-elimination wins grow, yet so
does the value of verifying what actually landed there. Only a single-worker run
on a slow CPU would make the hashing itself visible, and that is not how EC runs.

**The safety argument does not depend on the measurement.** Even if a future
machine made these changes worth 40%, the source re-read would still be the only
thing proving the file matches what was approved, and the backup re-read the only
thing proving the restore point exists. Speed is not the reason to decline; it is
merely the reason not to have to argue about it.

If you still want the throughput, the honest target is the read that costs most —
the `.bak` read immediately after its `Flush(flushToDisk: true)` — and the honest
approach is to make that read cheaper, not to delete it.

## Hash handling differs from LineEndingNormalizer

LEN uses two algorithms, split by whether the value is durable: SHA-256 for the
raw source bytes and the backup check, XxHash3 for the normalised-content digest
that lives in a private record and is discarded after the run.

EC uses SHA-256 for both, and persists its content digests as `SourceTextSha256`
and `OutputTextSha256`. That is defensible — EC-14 exists precisely to keep those
two independent — but the two tools now justify the same safety claim by
different means, and nothing checks that they agree:

| | EC | LEN |
|---|---|---|
| Raw file / backup hash | SHA-256 | SHA-256 |
| Content digest | SHA-256, persisted | XxHash3, discarded |
| Backup comparison | `string.Equals(..., OrdinalIgnoreCase)` on hex | `CryptographicOperations.FixedTimeEquals` on bytes |

Neither comparison is wrong for an accidental-corruption model. The point is the
drift: the detector-parity job exists to stop exactly this happening to the
shared detector, and nothing plays that role for the safety machinery around it.
**Open** — decide whether the two should converge, and on which.

## The source-choice refusal is covered by a unit test, not a smoke phase

Both the defect and its fix were reproduced manually. That is how the defect was
finally confirmed at all — until then it existed only as a reading of the code.

**Before the fix:** scan a directory, point the window at a different one without
scanning again, tick the refused file, choose an encoding, press confirm. The
review closes and the status bar reads "Conversion cancelled. No files were
modified." The choice is discarded and blamed on a cancellation nobody made.

**After the fix:** the review stays open and says which ticked files are no
longer inside its directory, and what to do about it.

A smoke phase for the sequence was attempted and abandoned. The setup drives
correctly — the refused row appears labelled `..\scanned\french.txt`, which only
happens when the plan's root does not contain the file — but `SelectCombo` times
out on the source-encoding dropdown in that dialog state, while the identical
call in phase C succeeds. Driving the review to the foreground and ticking the
row first were both tried; neither changed it.

**The dropdown works perfectly by hand**, so this is a defect in the automation
driver, not in EC. One candidate: `SelectCombo`'s keyboard fallback calls
`SetForegroundWindow` on the *main* window, which is the wrong target while a
modal review is open.

**The refusal is covered instead by a unit test**, not by a manual step. The
decision is now a method on the form — `DescribeUnusableScope` — so a test can
build a plan rooted outside its own files, tick the row, and assert on the
refusal without showing a window. `PerformClick` does nothing on a control that
is not effectively visible, and a test that had to show one would need an
interactive desktop, which is exactly what the unit suite must not require.

The driver defect stays **open** on its own account: it will bite any future
phase that touches a combo inside a dialog. The refusal itself is closed.

## What is still open, scored

Two axes, because one number hides the thing that matters. **Impact** is what a
user loses when it happens — the original review's rule, severity by consequence
and not by how hard the fix is. **Reach** is how easily it happens at all. A
critical impact nobody can trigger is not a crisis, and a low impact everyone
trips over is not noise.

| | Finding | Impact | Reach | Note |
|---|---|---|---|---|
| CX-06 | Entropy gate outranks a valid BOM | Medium | Occasional | EC reports the wrong encoding for a file that says what it is. The only open item that changes what EC tells you. |
| — | Ambiguous BOM-less UTF-32 converts silently | **Critical** | **Theoretical** | Rewrites on an unproven byte order — the exact thing this release line exists to prevent. Needs every scalar to be a multiple of 0x100, so real text will not reach it. Scored high on impact and dismissed on reach, deliberately. |
| EC-08 | An include pattern can hang the scan indefinitely | Medium | **Theoretical** | Availability, not data: a scan no token can cancel, and if it hangs partway through a conversion the tree is left partly converted with no journal. Needs *both* halves built on purpose — a mask of ~10+ wildcards separated by one character, and a filename carrying ~24+ mostly consecutive repeats of that same character. Measured at twelve wildcards: every realistic name answered in 0–5 ms; forty consecutive `a` took >20 s. The mask comes from the operator's own command line, so there is no untrusted path. Fix measured and not taken: see below. |
| — | CSV report does not neutralise leading formula characters | — | *withdrawn* | Does not reproduce: the `File` column is always an absolute path, so it cannot begin with a formula character. See above. |
| EC-16 | Settings.xml written truncate-in-place | Low | Occasional | Loses preferences, not data, and reverts toward safer defaults. Already caused one smoke-test failure that looked like a product bug. |
| EC-20 | Detection reads with looser file sharing | Low | Rare | Detect and validate only; nothing is written. Can describe bytes another process is changing. |
| EC-15 | Semantics booleans written as a contract, enforced nowhere | Low | Common | Cannot weaken behaviour — EC ignores the claim and always does the strict thing. The risk is a reader treating `OutputVerification: true` as evidence a check ran. |
| EC-17 | Text-validation comment contradicts its code | Low | Common | Behaviour is right, comment is wrong, and the file must stay byte-identical across three repos. A maintainer "correcting" it the wrong way would weaken binary rejection. |
| EC-23 | Null-forgiving dereference of `ResolvePath` | Low | Rare | Correct today only because `FindStaleFiles` runs 29 lines earlier. EC-06 is what happened when that invariant broke. |
| EC-18 | Ambiguity recomputed per pass | Low | Common | Measured: no cost. The probe aborts at the first invalid sequence, so a provable file settles in the first buffer. Untidy, not slow. |
| — | Automation driver cannot select a combo inside a dialog | Low | — | Blocks a smoke phase, not a user. Suspect: `SelectCombo`'s keyboard fallback targets the main window while a modal review is open. |
| — | Hash handling has drifted from LineEndingNormalizer | — | — | A decision, not a defect. See above. |

Nothing here writes to a file nobody approved, which is why none of it blocked a
release.

### EC-08: the fix that was measured and not taken

`RegexOptions.NonBacktracking` in place of `Compiled` removes the blow-up
entirely, agrees with the current engine on every mask tested, and costs nothing
measurable beside per-file I/O. It was implemented with tests and then reverted
deliberately: the defect needs a mask *and* a filename both built for it, and
neither arrives from anywhere but the operator's own hands.

Three dead ends, recorded so nobody walks them again. Testing a pathological
mask against a **matching** filename proves nothing, because the engine stops at
the first success — that is how this was first recorded "not reproduced". `*`
crossing directory separators is deliberate rather than a Windows-wildcard bug:
`src/*.cs` is meant to scope a subtree, pinned by
`PathAwarePatternTests.PathQualifiedPattern_MatchesOnlyTheIntendedSubtree`. And
`[^/]*` in place of `.*` does not reduce the backtracking, because a filename
contains no separator for it to bound.

### Not reproduced

| | Finding | Why it is not listed as open |
|---|---|---|
| EC-19 | The double-BOM guard's reach depends on which object supplied the codec | An inspection-only finding that traced the wrong object. `ConvertFiles` re-resolves the codec by name through `Encoding.GetEncoding`, which carries a 3-byte preamble, so the detector's BOM-less instance never reaches the guard. Tested against a file beginning with two BOMs: both the automatic path and `-From utf-8` refuse with `MultipleLeadingByteOrderMarks`. |
