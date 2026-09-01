# Release checklist

Automated coverage is the first gate and is enforced by CI. What follows is what CI
cannot answer.

## Source and version

- [ ] Working tree is clean.
- [ ] `AssemblyInfo.cs` contains the intended version; CLI help and `--version` display it; README and release notes name it.
- [ ] Release tag is exactly `v<project version>`.
- [ ] `SemanticsVersion` changes only when conversion or classification behaviour changes.

## Automated

- [ ] Release build succeeds with no warnings.
- [ ] `dotnet test sources/EncodingChecker.Tests/EncodingChecker.Tests.csproj -c Release` — all green.
- [ ] The scheduled **Shared Unicode detector parity** workflow is green. It compares
      the shared detector source in EncodingChecker, LineEndingNormalizer, and
      CorpusTesters after normalizing namespace, a redundant `using System` import,
      and line-ending differences.
- [ ] Ambiguous BOM-less UTF-16 is refused without changing bytes or creating a backup.
- [ ] Structurally provable BOM-less UTF-16 still converts correctly.
- [ ] Explicit source selection still receives strict decoding and output verification.
- [ ] A stale reviewed plan leaves every selected source unchanged.
- [ ] Backup and recovery-sidecar hashes match the source bytes used for conversion.
- [ ] CSV reports and JSON journals contain a stable reason code and useful diagnostic.

For a release changing detection or conversion policy:

- [ ] Run the four-corpus audit from a clean committed build.
- [ ] Record the exact commit, assembly hash, audit configuration, and limitations.
- [ ] Run the independent-oracle sentinel set when the release checklist requires it.

## Manual: the GUI smoke test

**Why this is manual.** EC's conversion policy, the plan binding, and the whole
orchestration sequence are automated. What is left is Windows Forms itself — designer
layout, background-worker marshalling, and the dialog's behaviour under a real message
pump. Automating those would mean weakening the architecture to make it drivable, which
would trade a real safety property for a test.

**Why it is not optional.** EC has already shipped a defect of exactly this shape: every
component was correct and tested while the GUI's *sequence* converted files the CLI
refuses. Nothing failed, because nothing ran the sequence. The orchestration is now
covered, so what remains is genuinely UI, but "genuinely UI" was also the last
description that turned out to be wrong.

### The evidence that counts

Status messages are not evidence. For every case below that must not modify a file,
record the file's SHA-256 before and after:

```powershell
Get-FileHash -Algorithm SHA256 <path> | Select-Object -ExpandProperty Hash
```

### Core GUI smoke test

[`tools/gui-smoke-test.py`](../tools/gui-smoke-test.py) creates disposable folders on the
Desktop and verifies the resulting bytes. For every phase, set the printed folder as
**Directory to check** and choose **utf-8** in **Convert to**. Then run each short phase
with the Release build:

```powershell
$smoke = (Resolve-Path tools/gui-smoke-test.py)
python $smoke setup A
# perform the displayed GUI steps
python $smoke mark A
python $smoke verify A
```

| Phase | What the GUI check proves |
|---|---|
| A | **View** lists the prepared files; Unicode and ASCII are ready, legacy files need a source choice; **Cancel** changes no bytes and creates no recovery files. |
| B | Unicode and ASCII convert without a source choice and preserve their exact text. |
| C | A chosen legacy source encoding applies only to the ticked files; unselected legacy files stay unchanged. |
| D | Ambiguous BOM-less UTF-16 is refused; cancelling leaves its bytes and folder unchanged. |
| E | An explicit BOM-less UTF-16 source choice converts the file exactly and creates its backup and recovery record. |

The script verifies hashes and decoded output; status messages alone never count as evidence.
Its `tools/smoke-state-*.json` files are local generated state and must not be committed.

### Accessibility spot check

- [ ] At 100%, 125%, and 150% display scaling, the review text, source-encoding
      chooser, and its confirmation button are fully visible without horizontal scrolling.
- [ ] Keyboard-only: Tab reaches the legacy-file list, source chooser, and both final
      actions; Enter performs only the displayed ready conversion; Escape cancels.
- [ ] In a Windows high-contrast theme, the review outcomes and legacy warning remain
      readable and distinguishable.

### Record

Fill this in and keep it with the release. It is the auditable answer to the one question
the test suite cannot reach.

```text
EC version:
Commit:
Windows version:
.NET version:
Date:
Tester:

Phase A (review + cancel):                  PASS / FAIL
Phase B (Unicode + ASCII conversion):       PASS / FAIL
Phase C (scoped legacy source choice):      PASS / FAIL
Phase D (ambiguous BOM-less UTF-16 refusal): PASS / FAIL
Phase E (explicit BOM-less UTF-16 choice):   PASS / FAIL

Cases where observed differed from expected:

Result: PASS / FAIL
```

## Documentation and publish

- [ ] README figures match the current audit run; no stale counts.
- [ ] README and release notes describe every changed refusal or safety rule.
- [ ] Publish framework-dependent and self-contained artifacts.
- [ ] Verify archive names and GitHub SHA-256 digests.
- [ ] Link audit evidence and state its limits in the release notes.
