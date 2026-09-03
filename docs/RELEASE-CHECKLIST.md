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

Nine phases drive the built executable through Windows UI Automation and verify the
resulting bytes. They replace the manual walkthrough this section used to describe.

```powershell
dotnet build sources/EncodingChecker.sln -c Release
sources/EncodingChecker.GuiSmoke/bin/Release/net10.0-windows/EncodingChecker.GuiSmoke.exe
```

Exit 0 is a pass. Each run writes `gui-smoke-report.json` and `gui-smoke-report.md`
carrying the EC version, the executable and managed-assembly hashes, and every phase's
before and after file hashes. Keep that evidence with the release.

**[What each of the nine phases proves, and what it would catch →](GUI-SMOKE-TEST.md)**

An interactive Windows desktop is required; the runner exits 2 rather than reporting a
pass it did not earn. Whether a GitHub-hosted runner provides one is not yet
established, so run this locally before tagging.

Status messages are never evidence on their own. Every phase checks files, and phase I
checks the status line *against* the bytes on disk rather than trusting it.

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
