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

## The GUI smoke test

**Why it is a gate of its own.** EC's conversion policy, plan binding, and orchestration
sequence are covered by the unit suite. What is left is Windows Forms itself — designer
layout, background-worker marshalling, and the dialog's behaviour under a real message
pump. The suite reaches those by driving the shipped executable through the
accessibility layer, so no part of the application is reshaped to make it drivable.

**Why it is not optional.** EC has already shipped a defect of exactly this shape: every
component was correct and tested while the GUI's *sequence* converted files the CLI
refuses. Nothing failed, because nothing ran the sequence. The orchestration is now
covered, so what remains is genuinely UI, but "genuinely UI" was also the last
description that turned out to be wrong.

### Running it

Nine phases drive the built executable through Windows UI Automation and verify the
resulting bytes.

```powershell
dotnet build sources/EncodingChecker.sln -c Release
sources/EncodingChecker.GuiSmoke/bin/Release/net10.0-windows/EncodingChecker.GuiSmoke.exe
```

Exit 0 is a pass. Each run writes `gui-smoke-report.json` and `gui-smoke-report.md`
carrying the EC version, the executable and managed-assembly hashes, and every phase's
before and after file hashes.

**[What each of the nine phases proves, and what it would catch →](GUI-SMOKE-TEST.md)**

**The release workflow runs this for you, and a failure stops the release.** It drives
the signed, published executable — the bytes that ship, not a rebuild of the same
commit — after signing and before packaging, and uploads the report as a
`gui-smoke-evidence` artifact. Run it locally while developing; before tagging, you no
longer have to remember to.

Two prerequisites, each refused with exit 2 rather than reported as a pass: an
interactive Windows desktop, which a hosted `windows-latest` runner provides, and a
build carrying the review dialog's automation ids — no release up to and including
v3.11.0 has them.

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

The nine phases record themselves. `gui-smoke-report.md` and `gui-smoke-report.json`
already carry the EC version, the executable and managed-assembly hashes, the OS and
.NET versions, and every phase's before and after file hashes — better evidence than a
transcribed letter, and not subject to a typo. Keep both files with the release.

What still needs a person is the spot check above, because nobody has automated a
judgement about whether text is readable. Fill this in and keep it alongside them.

```text
Commit:
Display scaling tested:
High-contrast theme:
Date:
Tester:

Scaling — review, chooser, and confirm button fully visible:  PASS / FAIL
Keyboard-only — Tab, Enter, and Escape behave as described:   PASS / FAIL
High contrast — outcomes and legacy warning distinguishable:  PASS / FAIL

Cases where observed differed from expected:

Result: PASS / FAIL
```

## Documentation and publish

- [ ] README figures match the current audit run; no stale counts.
- [ ] README and release notes describe every changed refusal or safety rule.
- [ ] Publish framework-dependent and self-contained artifacts.
- [ ] Verify archive names and GitHub SHA-256 digests.
- [ ] Link audit evidence and state its limits in the release notes.
